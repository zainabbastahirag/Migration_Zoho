using System.Security.Cryptography;
using System.Text.Json;
using AGONEAIHub.Core.Entities.Spot;
using AGONEAIHub.Core.Enums.Spot;
using AGONEAIHub.Core.Interfaces;
using AGONEAIHub.Infrastructure.Configuration;
using AGONEAIHub.Infrastructure.Data;
using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AGONEAIHub.Infrastructure.Services.Spot;

/// <summary>
/// Single service for ALL AGONESPot operations.
/// Classification, dataroom, reports, tags, company registration — everything.
/// </summary>
public class SpotDataService : ISpotDataService
{
    private readonly AIHubDbContext _db;
    private readonly SpotSettings _cfg;
    private readonly BlobServiceClient _blob;
    private readonly DocumentAnalysisClient _docInt;
    private readonly IChatService _chat;
    private readonly INotificationService _notify;
    private readonly SpotServiceBus _serviceBus;
    private readonly ILogger<SpotDataService> _log;

    private static readonly HashSet<string> EligibleDocTypes = new(StringComparer.OrdinalIgnoreCase)
        { "SSM", "CTOS", "AUDIT_REPORTS", "AUDIT_REPORT", "CTOS_REPORT", "Supplementary" };

    private static readonly string[] MalaysiaStates =
        { "Johor","Kedah","Kelantan","Melaka","Negeri Sembilan","Pahang","Perak","Perlis",
          "Pulau Pinang","Sabah","Sarawak","Selangor","Terengganu","Kuala Lumpur","Putrajaya","Labuan" };

    private static readonly string[] ReportSections = { "SectionA", "SectionB", "SectionC", "SectionD" };

    public SpotDataService(
        AIHubDbContext db, IOptions<SpotSettings> cfg,
        IChatService chat, INotificationService notify,
        SpotServiceBus serviceBus, ILogger<SpotDataService> log)
    {
        _db = db; _cfg = cfg.Value; _chat = chat; _notify = notify;
        _serviceBus = serviceBus; _log = log;
        _blob = new BlobServiceClient(_cfg.BlobConnectionString);
        _docInt = new DocumentAnalysisClient(new Uri(_cfg.DocIntEndpoint), new AzureKeyCredential(_cfg.DocIntKey));
    }

    // ================================================================
    //  CLASSIFY
    // ================================================================

    #region Classification

    public async Task<SpotResult> ClassifyAsync(
        string companyId, string userId, string fileName,
        Stream fileStream, long fileSize, CancellationToken ct = default)
    {
        companyId = companyId.ToUpper(); userId = userId.ToUpper();

        if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return Fail(400, "Only PDF files are supported.");
        if (fileSize > _cfg.MaxFileSizeBytes)
            return Fail(413, "File size must not exceed 50 MB.");

        using var ms = new MemoryStream();
        await fileStream.CopyToAsync(ms, ct);
        var fileBytes = ms.ToArray();

        var fileId = Guid.NewGuid().ToString().ToUpper();
        var blobName = $"uploaded_files/{companyId}/{fileId}.pdf";

        // Upload blob
        string blobUrl;
        try
        {
            var container = _blob.GetBlobContainerClient(_cfg.BlobContainer);
            await container.CreateIfNotExistsAsync(cancellationToken: ct);
            var bc = container.GetBlobClient(blobName);
            await bc.UploadAsync(new MemoryStream(fileBytes), true, ct);
            blobUrl = bc.Uri.ToString();
        }
        catch (Exception ex) { _log.LogError(ex, "Blob upload failed"); return Fail(500, "Failed to upload file."); }

        // Create job
        var job = new SpotJob { JobType = (int)JobType.Classify, CompanyId = companyId, UserId = userId, BlobUrl = blobUrl, Status = nameof(JobState.PendingQueue) };
        _db.SpotJobs.Add(job); await _db.SaveChangesAsync(ct);

        // Insert document metadata
        var doc = new SpotDocumentMetadata
        {
            FileUID = fileId, JobId = job.JobId.ToString(), CompanyId = companyId,
            FileName = fileName, FileSize = fileBytes.Length, FilePath = blobName,
            FileState = nameof(FileStatus.PENDING), FileType = "pdf",
            FileHash = Convert.ToHexString(SHA256.HashData(fileBytes)).ToLower(),
            FileURL = blobUrl, CreatedBy = userId
        };

        try { _db.SpotDocuments.Add(doc); await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex)
        {
            job.Status = nameof(JobState.Failed); await _db.SaveChangesAsync(ct);
            return Fail(400, ex.InnerException?.Message ?? ex.Message);
        }

        // Send to Azure Service Bus classify queue (production)
        // In dev mode (UseServiceBus=false), call /api/spot/classify-worker directly
        await _serviceBus.SendClassifyJobAsync(job.JobId.ToString(), (int)JobType.Classify, ct);

        _log.LogInformation("[CLASSIFY] Queued: company={Company}, jobId={JobId}, file={File}",
            companyId, job.JobId, fileName);

        return Ok("File uploaded and queued for classification.", MapDoc(doc));
    }

    public async Task<SpotResult> GetClassifyStatusAsync(string fileId, CancellationToken ct = default)
    {
        var doc = await _db.SpotDocuments.FirstOrDefaultAsync(d => d.FileUID == fileId, ct);
        if (doc == null) return Fail(404, "File not found.");

        string msg = doc.Confidence.HasValue ? "File classified." : "Classification in progress...";
        if (!doc.Confidence.HasValue)
        {
            var logMsg = await _db.SpotJobLogs.Where(l => l.JobId == doc.JobId)
                .OrderByDescending(l => l.CreatedAt).Select(l => l.Message).FirstOrDefaultAsync(ct);
            if (logMsg != null) msg = logMsg;
        }

        return Ok(msg, MapDoc(doc));
    }

    public async Task<SpotResult> ClassifyWorkerAsync(string jobId, int jobType, CancellationToken ct = default)
    {
        var job = await _db.SpotJobs.FirstOrDefaultAsync(j =>
            j.JobId.ToString() == jobId && j.JobType == jobType
            && j.Status == nameof(JobState.PendingQueue), ct);
        if (job == null) return Fail(404, "Job not found.");

        AnalyzeResult classifyResult;
        try
        {
            var url = MakeSasUrl(job.BlobUrl!);
            var op = await _docInt.AnalyzeDocumentFromUriAsync(WaitUntil.Completed, _cfg.ClassifierId, new Uri(url), cancellationToken: ct);
            classifyResult = op.Value;
            if (classifyResult.Documents.Count == 0) { await FailJobAsync(jobId, "No classification results.", ct); return Fail(400, "No classification results."); }
        }
        catch (Exception ex) { await FailJobAsync(jobId, ex.Message, ct); return Fail(500, $"Classification error: {ex.Message}"); }

        var top = classifyResult.Documents[0];
        var classified = top.Confidence > _cfg.FileVerificationThreshold;
        var docType = classified ? top.DocumentType : CoreDocumentTypes.Unknown;

        if (docType == CoreDocumentTypes.CTOS && !string.IsNullOrEmpty(_cfg.CtosLogoModelId))
        {
            if (!await ValidateCtosLogoAsync(job.BlobUrl!, ct)) { docType = CoreDocumentTypes.Unknown; classified = false; }
        }

        var fileState = classified ? nameof(FileStatus.APPROVED) : nameof(FileStatus.PROCESSED);

        // SSM-first enforcement
        var others = await _db.SpotDocuments.Where(d => d.CompanyId == job.CompanyId && d.JobId != jobId).ToListAsync(ct);
        bool ssmExists = others.Any(d => d.DocumentType == CoreDocumentTypes.SSM && !d.Tags.Contains("WrongType") && d.FileState != nameof(FileStatus.REJECTED));
        if (!ssmExists && docType != CoreDocumentTypes.SSM)
        {
            await SetDocFields(jobId, top.Confidence, docType, fileState, "WrongType", ct);
            await CompleteJobAsync(jobId, ct);
            return Fail(400, "Please upload SSM documents first.");
        }

        await SetDocFields(jobId, top.Confidence, docType, fileState, null, ct);

        if (CoreDocumentTypes.IsCoreType(docType))
            await ExtractAndValidateAsync(job, jobId, docType, top.Confidence, fileState, ct);

        await CompleteJobAsync(jobId, ct);
        return Ok("File classified.");
    }

    #endregion

    // ================================================================
    //  DATAROOM
    // ================================================================

    #region Dataroom

    public async Task<SpotResult> GetSummaryAsync(string companyId, CancellationToken ct = default)
    {
        companyId = companyId.ToUpper();
        var docs = await EligibleDocsAsync(companyId, ct);
        int pend = 0, proc = 0, appr = 0, rej = 0;
        foreach (var d in docs)
        {
            _ = d.FileState switch
            {
                nameof(FileStatus.REJECTED) => rej++,
                nameof(FileStatus.APPROVED) => appr++,
                nameof(FileStatus.PROCESSED) => proc++,
                _ => pend++
            };
        }
        return Ok("Retrieved company status.", new { total = docs.Count, reviewed = docs.Count(d => d.Confidence.HasValue), pending = pend, processed = proc, approved = appr, rejected = rej });
    }

    public async Task<SpotResult> GetCompanyFilesAsync(string companyId, CancellationToken ct = default)
    {
        var files = await _db.SpotDocuments.Where(d => d.CompanyId == companyId.ToUpper() && !d.IsDeleted)
            .OrderByDescending(d => d.CreatedDate).ToListAsync(ct);
        files.ForEach(f => f.FileURL = MakeSasUrl(f.FileURL));
        return Ok("Fetched company files.", files.Select(MapDoc));
    }

    public async Task<SpotResult> DeleteFileAsync(string companyId, string userId, string fileId, CancellationToken ct = default)
    {
        var doc = await _db.SpotDocuments.FirstOrDefaultAsync(d => d.CompanyId == companyId.ToUpper() && d.FileUID == fileId.ToUpper(), ct);
        if (doc == null) return Fail(404, "File not found.");
        doc.IsDeleted = true; doc.DeletedDate = DateTime.UtcNow; doc.DeletedBy = userId.ToUpper();
        await _db.SaveChangesAsync(ct);
        return Ok("File deleted.", new { fileUID = fileId.ToUpper() });
    }

    public async Task<SpotResult> UpdateDocumentStatusAsync(string companyId, string fileId, string status, CancellationToken ct = default)
    {
        if (!Enum.TryParse<FileStatus>(status, true, out _)) return Fail(400, $"Invalid status: {status}");
        var doc = await _db.SpotDocuments.FirstOrDefaultAsync(d => d.FileUID == fileId, ct);
        if (doc == null) return Fail(404, "File not found.");
        doc.FileState = status; await _db.SaveChangesAsync(ct);
        doc.FileURL = MakeSasUrl(doc.FileURL);
        return Ok("Status updated.", MapDoc(doc));
    }

    public async Task<SpotResult> ExtractCompanyProfileAsync(string companyId, CancellationToken ct = default)
    {
        companyId = companyId.ToUpper();
        var ssm = await _db.SpotDocuments.FirstOrDefaultAsync(d => d.CompanyId == companyId && d.DocumentType == CoreDocumentTypes.SSM && !d.IsDeleted, ct);
        if (ssm == null) return Fail(404, "No SSM document found.");

        var existing = await _db.SpotCompanies.FirstOrDefaultAsync(c => c.CompanyId == companyId, ct);
        if (existing?.CompanyProfileSourceFileId == ssm.FileUID && existing.CompanyProfileJson != null)
        {
            try { return Ok("Company profile extracted.", JsonSerializer.Deserialize<object>(existing.CompanyProfileJson)); } catch { }
        }

        try
        {
            var op = await _docInt.AnalyzeDocumentFromUriAsync(WaitUntil.Completed, _cfg.CompanyProfileModelId, new Uri(MakeSasUrl(ssm.FileURL)), cancellationToken: ct);
            if (op.Value.Documents.Count == 0) return Fail(400, "No data extracted.");
            var f = op.Value.Documents[0].Fields;
            var profile = new Dictionary<string, string?>
            {
                ["companyName"] = GetField(f, "company-name"), ["companyRegistrationNumber"] = GetField(f, "registeration-no"),
                ["address"] = GetField(f, "address"), ["principalBusiness"] = GetField(f, "principle-business"),
                ["dateOfIncorporation"] = GetField(f, "date-of-incorporation"), ["auditor"] = GetField(f, "auditor"),
                ["businessLicenseNumber"] = GetField(f, "business-license-num"), ["taxRegistrationNumber"] = GetField(f, "tax-registration-num"),
                ["keyPersonnel"] = GetField(f, "key-personal"),
                ["location"] = ExtractState(GetField(f, "address"))
            };

            var json = JsonSerializer.Serialize(profile);
            if (existing != null) { existing.CompanyProfileJson = json; existing.CompanyProfileSourceFileId = ssm.FileUID; }
            else _db.SpotCompanies.Add(new SpotCompany { CompanyId = companyId, CompanyProfileJson = json, CompanyProfileSourceFileId = ssm.FileUID, CreatedAt = DateTime.UtcNow });
            await _db.SaveChangesAsync(ct);

            return Ok("Company profile extracted.", profile);
        }
        catch (Exception ex) { _log.LogError(ex, "Profile extraction failed"); return Fail(500, "DI extraction failed."); }
    }

    public async Task<SpotResult> GetCompaniesStatusAsync(List<string> companyIds, CancellationToken ct = default)
    {
        var ids = companyIds.Select(c => c.ToUpper()).ToList();
        var counts = await _db.SpotDocuments.Where(d => ids.Contains(d.CompanyId) && !d.IsDeleted)
            .GroupBy(d => d.CompanyId).Select(g => new { companyId = g.Key, totalDocument = g.Count() }).ToListAsync(ct);
        var map = counts.ToDictionary(x => x.companyId, x => x.totalDocument);
        return Ok("OK", ids.Select(c => new { companyId = c, totalDocument = map.GetValueOrDefault(c, 0) }));
    }

    public async Task<SpotResult> ValidateCompanyDocumentsAsync(string companyId, CancellationToken ct = default)
    {
        companyId = companyId.ToUpper();
        var core = await _db.SpotDocuments.Where(d => d.CompanyId == companyId && CoreDocumentTypes.CoreTypes.Contains(d.DocumentType) && !d.IsDeleted && (d.Tags == null || !d.Tags.Contains("WrongType"))).ToListAsync(ct);
        var ssm = core.FirstOrDefault(d => d.DocumentType == CoreDocumentTypes.SSM);
        if (ssm == null) return new SpotResult { StatusCode = 400, Message = "No SSM document found.", Data = new { valid = false, ssmName = (string?)null, mismatches = new List<string>() } };

        var mismatches = new List<string>();
        foreach (var d in core.Where(x => x.DocumentType != CoreDocumentTypes.SSM))
        {
            bool match = (!string.IsNullOrEmpty(ssm.DocOfCompanyRegNo) && !string.IsNullOrEmpty(d.DocOfCompanyRegNo) && ssm.DocOfCompanyRegNo.Replace(" ", "").Equals(d.DocOfCompanyRegNo.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                      || (!string.IsNullOrEmpty(ssm.DocOfCompany) && !string.IsNullOrEmpty(d.DocOfCompany) && (ssm.DocOfCompany.Contains(d.DocOfCompany, StringComparison.OrdinalIgnoreCase) || d.DocOfCompany.Contains(ssm.DocOfCompany, StringComparison.OrdinalIgnoreCase)));
            if (!match) mismatches.Add($"{d.DocumentType} ({d.FileUID})");
        }
        bool valid = mismatches.Count == 0;
        return new SpotResult { StatusCode = valid ? 200 : 400, Message = valid ? "All documents verified." : "Company mismatch detected.", Data = new { valid, ssmName = ssm.DocOfCompany, mismatches } };
    }

    #endregion

    // ================================================================
    //  TAGS
    // ================================================================

    #region Tags

    public async Task<SpotResult> UpdateDocumentTagsAsync(string companyId, string fileId, List<string> tags, CancellationToken ct = default)
    {
        companyId = companyId.ToUpper(); fileId = fileId.ToUpper();
        var doc = await _db.SpotDocuments.FirstOrDefaultAsync(d => d.CompanyId == companyId && d.FileUID == fileId, ct);
        if (doc == null) return Fail(404, $"Document '{fileId}' not found.");
        doc.Tags = string.Join(",", tags);
        await _db.SaveChangesAsync(ct);
        return Ok($"Tags updated for file '{fileId}'.", new { tags = doc.Tags, fileUID = fileId, companyId });
    }

    #endregion

    // ================================================================
    //  COMPANY REGISTRATION
    // ================================================================

    #region Company Registration

    public async Task<SpotResult> RegisterCompanyAsync(
        string companyId, string companyName, string? representativeName,
        string? phone, string? address, string? email, CancellationToken ct = default)
    {
        companyId = companyId.ToUpper();
        if (await _db.SpotRegisteredCompanies.AnyAsync(c => c.CompanyId == companyId, ct))
            return Fail(400, "Company already registered.");

        _db.SpotRegisteredCompanies.Add(new SpotRegisteredCompany
        {
            CompanyId = companyId, CompanyName = companyName,
            RepresentativeName = representativeName, PhoneNumber = phone,
            CompanyAddress = address, Email = email
        });
        await _db.SaveChangesAsync(ct);
        return Ok("Company registered.", new { CompanyId = companyId });
    }

    #endregion

    // ================================================================
    //  REPORT GENERATION
    // ================================================================

    #region Reports

    public async Task<SpotResult> StartReportGenerationAsync(string companyId, string type, CancellationToken ct = default)
    {
        companyId = companyId.ToUpper();
        var files = await _db.SpotDocuments.Where(d => d.CompanyId == companyId && !d.IsDeleted).ToListAsync(ct);
        if (files.Count == 0) return Ok("No files found.", null);

        // Check if already running
        var existingJob = await _db.SpotJobs.FirstOrDefaultAsync(j =>
            j.CompanyId == companyId && j.JobType == (int)JobType.Report
            && (j.Status == nameof(JobState.PendingQueue) || j.Status == nameof(JobState.Processing)), ct);
        if (existingJob != null) return Ok("Report generation already in progress.", new { status = existingJob.Status });

        var idsString = string.Join(",", files.Select(f => f.FileUID));
        var hashesString = string.Join(",", files.Where(f => !string.IsNullOrEmpty(f.FileHash)).Select(f => f.FileHash).OrderBy(h => h));

        // Check for existing report with same source hash
        var existingReport = await _db.SpotReports.FirstOrDefaultAsync(r =>
            r.CompanyId == companyId && r.SourceFilesSortedHash == hashesString && !r.IsDeleted, ct);
        if (existingReport != null)
        {
            existingReport.FileURL = MakeSasUrl(existingReport.FileURL);
            return Ok("Report already generated.", MapReport(existingReport));
        }

        var job = new SpotJob { JobType = (int)JobType.Report, CompanyId = companyId, Status = nameof(JobState.PendingQueue), ReportType = type };
        _db.SpotJobs.Add(job); await _db.SaveChangesAsync(ct);

        var report = new SpotReport { JobId = job.JobId.ToString(), CompanyId = companyId, ReportType = type, SourceFiles = idsString, SourceFilesSortedHash = hashesString };
        _db.SpotReports.Add(report); await _db.SaveChangesAsync(ct);

        // Send to Azure Service Bus report queue (production)
        // In dev mode (UseServiceBus=false), call /api/spot/generate-report-worker directly
        await _serviceBus.SendReportJobAsync(job.JobId.ToString(), (int)JobType.Report, ct);

        _log.LogInformation("[REPORT] Queued: company={Company}, jobId={JobId}, type={Type}",
            companyId, job.JobId, type);

        return Ok("Report generation queued.", MapReport(report));
    }

    public async Task<SpotResult> GetReportStatusAsync(string companyId, string type, CancellationToken ct = default)
    {
        companyId = companyId.ToUpper();
        var job = await _db.SpotJobs.Where(j => j.CompanyId == companyId && j.JobType == (int)JobType.Report && (j.ReportType == type || j.ReportType == null))
            .OrderByDescending(j => j.CreatedAt).FirstOrDefaultAsync(ct);

        if (job == null) return Ok("No report job found.", new { status = "not_started" });

        if (job.Status == nameof(JobState.Success))
        {
            var report = await _db.SpotReports.FirstOrDefaultAsync(r => r.JobId == job.JobId.ToString() && !r.IsDeleted, ct);
            if (report != null) report.FileURL = MakeSasUrl(report.FileURL);
            return Ok("Report generated.", new { status = "done", fileUrl = report?.FileURL, startedAt = job.StartedAt, finishedAt = job.FinishedAt });
        }

        if (job.Status == nameof(JobState.Failed))
        {
            var logMsg = await _db.SpotJobLogs.Where(l => l.JobId == job.JobId.ToString()).OrderByDescending(l => l.CreatedAt).Select(l => l.Message).FirstOrDefaultAsync(ct);
            return Ok("Report generation failed.", new { status = "error", message = logMsg ?? "Failed", startedAt = job.StartedAt, finishedAt = job.FinishedAt });
        }

        var stepMsg = await _db.SpotJobLogs.Where(l => l.JobId == job.JobId.ToString()).OrderByDescending(l => l.CreatedAt).Select(l => l.Message).FirstOrDefaultAsync(ct);
        return Ok("Report in progress.", new { status = job.Status, currentStep = stepMsg ?? "Queued", startedAt = job.StartedAt });
    }

    /// <summary>
    /// SPOT Report Generation — 6 clean steps.
    ///
    /// STORAGE RULE:
    ///   - Uploaded PDFs → Azure Blob (already there from /classify)
    ///   - Extracted text → SQL (DocumentMetadata.ExtractedText)
    ///   - Final report MD + JSON → SQL (Reports.ReportMarkdown + ReportJsonContent)
    ///   - NO blob storage for intermediate or output files
    /// </summary>
    public async Task<SpotResult> ReportWorkerAsync(string jobId, CancellationToken ct = default)
    {
        // ────────────────────────────────────────────────────────────
        // STEP 0: Find the job
        // ────────────────────────────────────────────────────────────
        var job = await _db.SpotJobs.FirstOrDefaultAsync(j =>
            j.JobId.ToString() == jobId && j.JobType == (int)JobType.Report
            && (j.Status == nameof(JobState.PendingQueue) || j.Status == nameof(JobState.Processing)), ct);

        if (job == null) return Fail(404, "Job not found.");

        job.Status = nameof(JobState.Processing);
        job.StartedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        try
        {
            // ────────────────────────────────────────────────────────
            // STEP 1: Load all company files from SQL
            // ────────────────────────────────────────────────────────
            await LogStep(jobId, "Step 1/6: Loading company files...", ct);

            var files = await _db.SpotDocuments
                .Where(d => d.CompanyId == job.CompanyId && !d.IsDeleted)
                .ToListAsync(ct);

            if (files.Count == 0)
            {
                await FailReportAsync(jobId, job.CompanyId, "No company files found.", ct);
                return Fail(400, "No company files found.");
            }

            _log.LogInformation("[REPORT] Step 1: Found {Count} files for {Company}", files.Count, job.CompanyId);

            // ────────────────────────────────────────────────────────
            // STEP 2: Validate company documents (SSM cross-check)
            // ────────────────────────────────────────────────────────
            await LogStep(jobId, "Step 2/6: Validating documents...", ct);

            var validation = await ValidateCompanyDocumentsAsync(job.CompanyId, ct);
            if (validation.StatusCode != 200)
            {
                await FailReportAsync(jobId, job.CompanyId, validation.Message, ct);
                return Fail(400, validation.Message);
            }

            // ────────────────────────────────────────────────────────
            // STEP 3: Extract text from each PDF (Azure Doc Intelligence)
            //         Save extracted text directly to SQL — no blob needed
            // ────────────────────────────────────────────────────────
            await LogStep(jobId, "Step 3/6: Extracting text from PDFs...", ct);

            var container = _blob.GetBlobContainerClient(_cfg.BlobContainer);
            var extractedDocs = new List<ExtractedDocument>();

            foreach (var file in files)
            {
                // If we already extracted text before, reuse it (no need to call Azure again)
                if (!string.IsNullOrEmpty(file.ExtractedText))
                {
                    extractedDocs.Add(new ExtractedDocument
                    {
                        FileName = file.FileName,
                        DocType = file.DocumentType ?? "Unknown",
                        Text = file.ExtractedText,
                        PageCount = file.ExtractedPageCount ?? 0
                    });
                    _log.LogDebug("  {File}: using cached extracted text from SQL", file.FileName);
                    continue;
                }

                // Download PDF from blob and extract text via Azure Doc Intelligence
                if (string.IsNullOrEmpty(file.FilePath)) continue;
                try
                {
                    var blobClient = container.GetBlobClient(file.FilePath);
                    if (!await blobClient.ExistsAsync(ct)) continue;

                    var download = await blobClient.DownloadContentAsync(ct);
                    var pdfBytes = download.Value.Content.ToArray();

                    var layoutResult = await _docInt.AnalyzeDocumentAsync(
                        WaitUntil.Completed, "prebuilt-layout",
                        new MemoryStream(pdfBytes), cancellationToken: ct);

                    var text = layoutResult.Value.Content ?? "";
                    var pageCount = layoutResult.Value.Pages.Count;

                    // Save extracted text to SQL (so next run doesn't need to re-extract)
                    file.ExtractedText = text;
                    file.ExtractedPageCount = pageCount;

                    extractedDocs.Add(new ExtractedDocument
                    {
                        FileName = file.FileName,
                        DocType = file.DocumentType ?? "Unknown",
                        Text = text,
                        PageCount = pageCount
                    });

                    _log.LogInformation("  {File}: extracted {Pages} pages, {Chars} chars",
                        file.FileName, pageCount, text.Length);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "  {File}: extraction failed, skipping", file.FileName);
                    await LogStep(jobId, $"Warning: skipping {file.FileName}", ct);
                }
            }

            // Save all extracted text to SQL
            await _db.SaveChangesAsync(ct);

            if (extractedDocs.Count == 0)
            {
                await FailReportAsync(jobId, job.CompanyId, "No files could be processed.", ct);
                return Fail(500, "No files could be processed.");
            }

            _log.LogInformation("[REPORT] Step 3: Extracted text from {Count}/{Total} files",
                extractedDocs.Count, files.Count);

            // ────────────────────────────────────────────────────────
            // STEP 4: Generate all 16 report sections via OpenAI
            //         (parallel, 5 concurrent, prompts from DB)
            // ────────────────────────────────────────────────────────
            await LogStep(jobId, "Step 4/6: Generating report via AI (16 sections)...", ct);

            var orchestrator = new SpotReportOrchestrator(_chat, _log);
            var spotResult = await orchestrator.GenerateAsync(job.CompanyId, extractedDocs, ct);

            if (!spotResult.Success)
            {
                await FailReportAsync(jobId, job.CompanyId,
                    spotResult.ErrorMessage ?? "AI generation failed.", ct);
                return Fail(500, spotResult.ErrorMessage ?? "AI generation failed.");
            }

            _log.LogInformation("[REPORT] Step 4: Generated report in {Ms}ms", spotResult.DurationMs);

            // ────────────────────────────────────────────────────────
            // STEP 5: Save final report to SQL (not blob)
            // ────────────────────────────────────────────────────────
            await LogStep(jobId, "Step 5/6: Saving report to database...", ct);

            var report = await _db.SpotReports.FirstOrDefaultAsync(r => r.JobId == jobId, ct);
            if (report != null)
            {
                report.ReportMarkdown = spotResult.ReportMarkdown;
                report.ReportJsonContent = spotResult.ReportJson;
            }

            // ────────────────────────────────────────────────────────
            // STEP 6: Mark job as done
            // ────────────────────────────────────────────────────────
            job.Status = nameof(JobState.Success);
            job.FinishedAt = DateTime.UtcNow;
            await LogStep(jobId, "Step 6/6: Complete!", ct);
            await _db.SaveChangesAsync(ct);

            _log.LogInformation("[REPORT] Done! Company={Company}, JobId={JobId}, Duration={Ms}ms",
                job.CompanyId, jobId, spotResult.DurationMs);

            return Ok("Report generated.", new
            {
                reportId = report?.ReportId,
                companyId = job.CompanyId,
                durationMs = spotResult.DurationMs
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[REPORT] Failed for job {JobId}", jobId);
            await FailReportAsync(jobId, job.CompanyId, ex.Message, ct);
            return Fail(500, $"Report generation failed: {ex.Message}");
        }
    }

    private async Task LogStep(string jobId, string message, CancellationToken ct)
    {
        _db.SpotJobLogs.Add(new SpotJobLog { JobId = jobId, Message = message });
        await _db.SaveChangesAsync(ct);
    }

    private async Task FailReportAsync(string jobId, string companyId, string message, CancellationToken ct)
    {
        await LogStep(jobId, message, ct);
        await FailJobAsync(jobId, message, ct);
    }

    public async Task<SpotResult> GetSpotStatisticsAsync(CancellationToken ct = default)
    {
        var jobs = await _db.SpotJobs.Where(j => j.JobType == (int)JobType.Report).ToListAsync(ct);
        int total = jobs.Count, completed = jobs.Count(j => j.Status == nameof(JobState.Success)),
            pending = jobs.Count(j => j.Status is nameof(JobState.PendingQueue) or nameof(JobState.Processing));
        return Ok("Retrieved statistics.", new { totalSpots = total, completed, pending, successRate = total > 0 ? Math.Round((double)completed / total * 100) : 0 });
    }

    public async Task<SpotResult> GetSpotHistoryAsync(string companyId, CancellationToken ct = default)
    {
        companyId = companyId.ToUpper();
        var reports = await _db.SpotReports.Where(r => r.CompanyId == companyId && !r.IsDeleted && r.FileURL != null)
            .OrderByDescending(r => r.CreatedAt).ToListAsync(ct);

        var result = new List<object>();
        foreach (var r in reports)
        {
            var job = await _db.SpotJobs.FirstOrDefaultAsync(j => j.JobId.ToString() == r.JobId, ct);
            result.Add(new { r.ReportId, fileURL = MakeSasUrl(r.FileURL), r.SourceFilesSortedHash, r.GenerationType, startedAt = job?.StartedAt, finishedAt = job?.FinishedAt });
        }

        return Ok("Retrieved report history.", result);
    }

    public async Task<SpotResult> DeleteReportAsync(string companyId, string userId, string reportId, CancellationToken ct = default)
    {
        companyId = companyId.ToUpper(); reportId = reportId.ToUpper();
        var report = await _db.SpotReports.FirstOrDefaultAsync(r => r.CompanyId == companyId && r.ReportId.ToString().ToUpper() == reportId, ct);
        if (report == null) return Fail(404, "Report not found.");
        report.IsDeleted = true; report.DeletedDate = DateTime.UtcNow; report.DeletedBy = userId.ToUpper();
        var job = await _db.SpotJobs.FirstOrDefaultAsync(j => j.JobId.ToString() == report.JobId, ct);
        if (job != null) _db.SpotJobs.Remove(job);
        await _db.SaveChangesAsync(ct);
        return Ok("Report deleted.", new { ReportId = reportId });
    }

    public async Task<SpotResult> GetRecentReportsAsync(CancellationToken ct = default)
    {
        var reports = await _db.SpotReports.Where(r => !r.IsDeleted && r.FileURL != null)
            .OrderByDescending(r => r.CreatedAt).Take(5).ToListAsync(ct);
        var result = reports.Select(r => new { r.ReportId, r.CompanyId, fileURL = MakeSasUrl(r.FileURL), r.CreatedAt, r.GenerationType });
        return Ok("Retrieved recent reports.", result);
    }

    #endregion

    // ================================================================
    //  PRIVATE HELPERS (shared by all methods above)
    // ================================================================

    #region Helpers

    private async Task<bool> ValidateCtosLogoAsync(string blobUrl, CancellationToken ct)
    {
        try
        {
            var op = await _docInt.AnalyzeDocumentFromUriAsync(WaitUntil.Completed, _cfg.CtosLogoModelId, new Uri(MakeSasUrl(blobUrl)), cancellationToken: ct);
            return op.Value.Documents.Count > 0 && op.Value.Documents[0].Fields.TryGetValue("CTOS-LOGO", out var f) && !string.IsNullOrWhiteSpace(f.Content);
        }
        catch { return false; }
    }

    private async Task ExtractAndValidateAsync(SpotJob job, string jobId, string docType, double confidence, string fileState, CancellationToken ct)
    {
        try
        {
            string name = "", reg = "";
            if (docType == CoreDocumentTypes.SSM && !string.IsNullOrEmpty(_cfg.CompanyProfileModelId))
            {
                var op = await _docInt.AnalyzeDocumentFromUriAsync(WaitUntil.Completed, _cfg.CompanyProfileModelId, new Uri(MakeSasUrl(job.BlobUrl!)), cancellationToken: ct);
                if (op.Value.Documents.Count > 0) { var f = op.Value.Documents[0].Fields; name = GetField(f, "company-name"); reg = GetField(f, "registeration-no"); }
            }
            if (!string.IsNullOrEmpty(name) || !string.IsNullOrEmpty(reg))
            {
                var doc = await _db.SpotDocuments.FirstOrDefaultAsync(d => d.JobId == jobId, ct);
                if (doc != null) { doc.DocOfCompany = name; doc.DocOfCompanyRegNo = reg; await _db.SaveChangesAsync(ct); }
            }
            if (docType != CoreDocumentTypes.SSM)
            {
                var ssm = await _db.SpotDocuments.FirstOrDefaultAsync(d => d.CompanyId == job.CompanyId && d.DocumentType == CoreDocumentTypes.SSM && !d.Tags.Contains("WrongType") && d.FileState != nameof(FileStatus.REJECTED), ct);
                if (ssm != null && (!string.IsNullOrEmpty(ssm.DocOfCompanyRegNo) || !string.IsNullOrEmpty(ssm.DocOfCompany)))
                {
                    bool match = (!string.IsNullOrEmpty(ssm.DocOfCompanyRegNo) && !string.IsNullOrEmpty(reg) && ssm.DocOfCompanyRegNo.Replace(" ", "").Equals(reg.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                              || (!string.IsNullOrEmpty(ssm.DocOfCompany) && !string.IsNullOrEmpty(name) && (ssm.DocOfCompany.Contains(name, StringComparison.OrdinalIgnoreCase) || name.Contains(ssm.DocOfCompany, StringComparison.OrdinalIgnoreCase)));
                    if (!match) await SetDocFields(jobId, confidence, docType, nameof(FileStatus.REJECTED), "WrongCompany", ct);
                }
            }
        }
        catch (Exception ex) { _log.LogError(ex, "ExtractAndValidate failed for {JobId}", jobId); }
    }

    private async Task SetDocFields(string jobId, double confidence, string docType, string fileState, string? tag, CancellationToken ct)
    {
        var doc = await _db.SpotDocuments.FirstOrDefaultAsync(d => d.JobId == jobId, ct);
        if (doc == null) return;
        doc.Confidence = confidence; doc.DocumentType = docType; doc.FileState = fileState; doc.FileProcessState = nameof(JobState.Success);
        if (tag != null) doc.Tags = string.IsNullOrEmpty(doc.Tags) ? tag : doc.Tags.Contains(tag) ? doc.Tags : $"{doc.Tags},{tag}";
        await _db.SaveChangesAsync(ct);
    }

    private async Task CompleteJobAsync(string jobId, CancellationToken ct)
    {
        var job = await _db.SpotJobs.FirstOrDefaultAsync(j => j.JobId.ToString() == jobId, ct);
        if (job != null) { job.Status = nameof(JobState.Success); job.FinishedAt = DateTime.UtcNow; await _db.SaveChangesAsync(ct); }
    }

    private async Task FailJobAsync(string jobId, string msg, CancellationToken ct)
    {
        var job = await _db.SpotJobs.FirstOrDefaultAsync(j => j.JobId.ToString() == jobId, ct);
        if (job != null) { job.Status = nameof(JobState.Failed); job.FinishedAt = DateTime.UtcNow; job.Notes = msg; await _db.SaveChangesAsync(ct); }
        await _notify.CreateErrorNotificationAsync(Core.Enums.ProjectName.AGONESPot, "SpotDataService", "ClassifyWorker", msg, null, jobId, null, ct);
    }

    private async Task<List<SpotDocumentMetadata>> EligibleDocsAsync(string companyId, CancellationToken ct) =>
        await _db.SpotDocuments.Where(d => d.CompanyId == companyId && !d.IsDeleted && EligibleDocTypes.Contains(d.DocumentType) && (d.Tags == null || !d.Tags.Contains("WrongType"))).OrderByDescending(d => d.CreatedDate).ToListAsync(ct);

    private static string GetField(IReadOnlyDictionary<string, DocumentField> f, string k) =>
        f.TryGetValue(k, out var v) ? (v.Content ?? "").TrimStart(':').Trim() : "";

    private static string ExtractState(string? addr)
    {
        if (string.IsNullOrWhiteSpace(addr)) return "";
        foreach (var s in MalaysiaStates) if (addr.Contains(s, StringComparison.OrdinalIgnoreCase)) return s;
        return "";
    }

    private string MakeSasUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        try
        {
            var parts = new Uri(url).AbsolutePath.TrimStart('/').Split('/', 2);
            if (parts.Length < 2) return url;
            var bc = _blob.GetBlobContainerClient(parts[0]).GetBlobClient(parts[1]);
            var sas = new BlobSasBuilder { BlobContainerName = parts[0], BlobName = parts[1], Resource = "b", ExpiresOn = DateTimeOffset.UtcNow.AddHours(1) };
            sas.SetPermissions(BlobSasPermissions.Read);
            return bc.GenerateSasUri(sas).ToString();
        }
        catch { return url; }
    }

    private static object MapDoc(SpotDocumentMetadata d) => new
    { d.FileUID, d.JobId, d.CompanyId, d.FileName, d.FileSize, d.FileType, d.FileHash, d.FileState, d.DocumentType, d.Confidence, d.FileURL, d.Tags, d.DocOfCompany, d.DocOfCompanyRegNo, d.CreatedDate };

    private static object MapReport(SpotReport r) => new
    { r.ReportId, r.JobId, r.CompanyId, r.ReportType, r.FileURL, r.CreatedAt, r.SourceFilesSortedHash, r.GenerationType };

    private static SpotResult Ok(string msg, object? data = null) => new() { StatusCode = 200, Message = msg, Data = data };
    private static SpotResult Fail(int code, string msg) => new() { StatusCode = code, Message = msg };

    #endregion
}
