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

public class SpotSettings
{
    public string BlobConnectionString { get; set; } = string.Empty;
    public string BlobContainer { get; set; } = "agonespot";
    public string DocIntEndpoint { get; set; } = string.Empty;
    public string DocIntKey { get; set; } = string.Empty;
    public string ClassifierId { get; set; } = string.Empty;
    public string CtosLogoModelId { get; set; } = "";
    public string CompanyProfileModelId { get; set; } = "";
    public double FileVerificationThreshold { get; set; } = 0.7;
    public long MaxFileSizeBytes { get; set; } = 52_428_800; // 50 MB
}

public class ClassificationService : IClassificationService
{
    private readonly AIHubDbContext _db;
    private readonly BlobServiceClient _blobService;
    private readonly DocumentAnalysisClient _docIntClient;
    private readonly SpotSettings _cfg;
    private readonly INotificationService _notifications;
    private readonly ILogger<ClassificationService> _log;

    public ClassificationService(
        AIHubDbContext db,
        IOptions<SpotSettings> cfg,
        INotificationService notifications,
        ILogger<ClassificationService> log)
    {
        _db = db;
        _cfg = cfg.Value;
        _notifications = notifications;
        _log = log;

        _blobService = new BlobServiceClient(_cfg.BlobConnectionString);
        _docIntClient = new DocumentAnalysisClient(
            new Uri(_cfg.DocIntEndpoint),
            new AzureKeyCredential(_cfg.DocIntKey));
    }

    // ================================================================
    //  POST /classify — upload + queue
    // ================================================================

    public async Task<ClassifyResult> ClassifyAsync(
        string companyId, string userId, string fileName,
        Stream fileStream, long fileSize,
        CancellationToken ct = default)
    {
        companyId = companyId.ToUpper();
        userId = userId.ToUpper();

        if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return new ClassifyResult { StatusCode = 400, Message = "Only PDF files are supported." };

        if (fileSize > _cfg.MaxFileSizeBytes)
            return new ClassifyResult { StatusCode = 413, Message = "File size must not exceed 50 MB." };

        // Read file bytes
        using var ms = new MemoryStream();
        await fileStream.CopyToAsync(ms, ct);
        var fileBytes = ms.ToArray();

        var fileId = Guid.NewGuid().ToString().ToUpper();
        var blobName = $"uploaded_files/{companyId}/{fileId}.pdf";

        // Upload to Azure Blob
        string blobUrl;
        try
        {
            var container = _blobService.GetBlobContainerClient(_cfg.BlobContainer);
            await container.CreateIfNotExistsAsync(cancellationToken: ct);
            var blobClient = container.GetBlobClient(blobName);
            await blobClient.UploadAsync(new MemoryStream(fileBytes), overwrite: true, cancellationToken: ct);
            blobUrl = blobClient.Uri.ToString();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to upload blob {Blob}", blobName);
            return new ClassifyResult { StatusCode = 500, Message = "Failed to upload file." };
        }

        // Create Job
        var job = new SpotJob
        {
            JobType = (int)JobType.Classify,
            CompanyId = companyId,
            UserId = userId,
            BlobUrl = blobUrl,
            Status = JobState.PendingQueue.ToString()
        };
        _db.SpotJobs.Add(job);
        await _db.SaveChangesAsync(ct);
        var jobId = job.JobId.ToString();

        // Compute file hash
        var fileHash = Convert.ToHexString(SHA256.HashData(fileBytes)).ToLower();

        // Insert document metadata
        var doc = new SpotDocumentMetadata
        {
            FileUID = fileId,
            JobId = jobId,
            CompanyId = companyId,
            FileName = fileName,
            FileSize = fileBytes.Length,
            FilePath = blobName,
            FileState = FileStatus.PENDING.ToString(),
            FileType = "pdf",
            FileHash = fileHash,
            FileURL = blobUrl,
            CreatedBy = userId,
            CreatedDate = DateTime.UtcNow
        };

        try
        {
            _db.SpotDocuments.Add(doc);
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Likely duplicate hash
            job.Status = JobState.Failed.ToString();
            await _db.SaveChangesAsync(ct);
            return new ClassifyResult { StatusCode = 400, Message = ex.InnerException?.Message ?? ex.Message };
        }

        _log.LogInformation("Classify queued: company={Company}, jobId={JobId}, file={File}",
            companyId, jobId, fileName);

        return new ClassifyResult
        {
            StatusCode = 200,
            Message = "File uploaded and queued for classification.",
            Data = MapToDto(doc)
        };
    }

    // ================================================================
    //  GET /classify/status
    // ================================================================

    public async Task<ClassifyStatusResult> GetStatusAsync(string fileId, CancellationToken ct = default)
    {
        var doc = await _db.SpotDocuments.FirstOrDefaultAsync(d => d.FileUID == fileId, ct);
        if (doc == null)
            return new ClassifyStatusResult { StatusCode = 404, Message = "File not found." };

        string message;
        if (doc.Confidence.HasValue)
        {
            message = "File classified.";
        }
        else
        {
            var job = await _db.SpotJobs.FirstOrDefaultAsync(j =>
                j.JobId.ToString() == doc.JobId && j.JobType == (int)JobType.Classify, ct);

            if (job?.Status == JobState.Success.ToString())
                message = "File classified.";
            else
            {
                var log = await _db.SpotJobLogs
                    .Where(l => l.JobId == doc.JobId)
                    .OrderByDescending(l => l.CreatedAt)
                    .FirstOrDefaultAsync(ct);
                message = log?.Message ?? "Classification in progress...";
            }
        }

        return new ClassifyStatusResult
        {
            StatusCode = 200,
            Message = message,
            Data = MapToDto(doc)
        };
    }

    // ================================================================
    //  POST /classify-worker — run classification via Azure DocInt
    // ================================================================

    public async Task<ClassifyResult> ClassifyWorkerAsync(string jobId, int jobType, CancellationToken ct = default)
    {
        var job = await _db.SpotJobs.FirstOrDefaultAsync(j =>
            j.JobId.ToString() == jobId
            && j.JobType == jobType
            && j.Status == JobState.PendingQueue.ToString(), ct);

        if (job == null)
            return new ClassifyResult { StatusCode = 404, Message = "Job not found." };

        // Run classifier
        AnalyzeResult classifyResult;
        try
        {
            var secureUrl = GenerateSecureBlobUrl(job.BlobUrl!);
            var poller = await _docIntClient.AnalyzeDocumentFromUriAsync(
                WaitUntil.Completed, _cfg.ClassifierId, new Uri(secureUrl), cancellationToken: ct);
            classifyResult = poller.Value;

            if (classifyResult.Documents.Count == 0)
            {
                await FailJob(jobId, "No classification results returned.", ct);
                return new ClassifyResult { StatusCode = 400, Message = "No classification results." };
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Classification failed for job {JobId}", jobId);
            await FailJob(jobId, ex.Message, ct);
            return new ClassifyResult { StatusCode = 500, Message = $"Classification error: {ex.Message}" };
        }

        var topDoc = classifyResult.Documents[0];
        var classified = topDoc.Confidence > _cfg.FileVerificationThreshold;
        var docType = classified ? topDoc.DocumentType : CoreDocumentTypes.Unknown;

        // CTOS logo validation
        if (docType == CoreDocumentTypes.CTOS && !string.IsNullOrEmpty(_cfg.CtosLogoModelId))
        {
            var hasLogo = await ValidateCtosLogoAsync(job.BlobUrl!, ct);
            if (!hasLogo) { docType = CoreDocumentTypes.Unknown; classified = false; }
        }

        var fileState = classified ? FileStatus.APPROVED.ToString() : FileStatus.PROCESSED.ToString();

        // SSM-first enforcement
        var otherDocs = await _db.SpotDocuments
            .Where(d => d.CompanyId == job.CompanyId && d.JobId != jobId)
            .ToListAsync(ct);

        bool validSsmExists = otherDocs.Any(d =>
            d.DocumentType == CoreDocumentTypes.SSM
            && !d.Tags.Contains("WrongType")
            && d.FileState != FileStatus.REJECTED.ToString());

        if (!validSsmExists && docType != CoreDocumentTypes.SSM)
        {
            _log.LogInformation("SSM-first enforcement: blocking non-SSM for company {Company}", job.CompanyId);
            await UpdateDocMeta(jobId, topDoc.Confidence, docType, fileState, "WrongType", ct);
            await CompleteJob(jobId, ct);
            return new ClassifyResult { StatusCode = 400, Message = "Please upload SSM documents first." };
        }

        // Update document metadata
        await UpdateDocMeta(jobId, topDoc.Confidence, docType, fileState, null, ct);

        // Company name extraction + cross-validation for core docs
        if (CoreDocumentTypes.IsCoreType(docType))
        {
            await ExtractAndValidateCompanyAsync(job, jobId, docType, topDoc.Confidence, fileState, ct);
        }

        await CompleteJob(jobId, ct);

        return new ClassifyResult { StatusCode = 200, Message = "File classified." };
    }

    // ================================================================
    //  PRIVATE HELPERS
    // ================================================================

    private async Task<bool> ValidateCtosLogoAsync(string blobUrl, CancellationToken ct)
    {
        try
        {
            var secureUrl = GenerateSecureBlobUrl(blobUrl);
            var poller = await _docIntClient.AnalyzeDocumentFromUriAsync(
                WaitUntil.Completed, _cfg.CtosLogoModelId, new Uri(secureUrl), cancellationToken: ct);
            var result = poller.Value;

            if (result.Documents.Count > 0)
            {
                var fields = result.Documents[0].Fields;
                if (fields.TryGetValue("CTOS-LOGO", out var logoField))
                {
                    var content = logoField.Content;
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        _log.LogInformation("CTOS logo confirmed via content: {Content}", content);
                        return true;
                    }
                }
            }

            _log.LogWarning("CTOS logo NOT found — reverting to Unknown");
            return false;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "CTOS logo extraction failed");
            return false;
        }
    }

    private async Task ExtractAndValidateCompanyAsync(
        SpotJob job, string jobId, string docType, double confidence,
        string fileState, CancellationToken ct)
    {
        try
        {
            string companyName = "", companyRegNo = "";

            if (docType == CoreDocumentTypes.SSM && !string.IsNullOrEmpty(_cfg.CompanyProfileModelId))
            {
                // SSM: extract with custom model
                var secureUrl = GenerateSecureBlobUrl(job.BlobUrl!);
                var poller = await _docIntClient.AnalyzeDocumentFromUriAsync(
                    WaitUntil.Completed, _cfg.CompanyProfileModelId, new Uri(secureUrl), cancellationToken: ct);
                var result = poller.Value;

                if (result.Documents.Count > 0)
                {
                    var fields = result.Documents[0].Fields;
                    companyName = GetFieldValue(fields, "company-name");
                    companyRegNo = GetFieldValue(fields, "registeration-no");
                }
            }

            if (!string.IsNullOrEmpty(companyName) || !string.IsNullOrEmpty(companyRegNo))
            {
                var doc = await _db.SpotDocuments.FirstOrDefaultAsync(d => d.JobId == jobId, ct);
                if (doc != null)
                {
                    doc.DocOfCompany = companyName;
                    doc.DocOfCompanyRegNo = companyRegNo;
                    await _db.SaveChangesAsync(ct);
                }
            }

            // Cross-validate against SSM for non-SSM core docs
            if (docType != CoreDocumentTypes.SSM)
            {
                var ssmDoc = await _db.SpotDocuments
                    .Where(d => d.CompanyId == job.CompanyId && d.DocumentType == CoreDocumentTypes.SSM
                        && !d.Tags.Contains("WrongType") && d.FileState != FileStatus.REJECTED.ToString())
                    .FirstOrDefaultAsync(ct);

                if (ssmDoc != null && (!string.IsNullOrEmpty(ssmDoc.DocOfCompany) || !string.IsNullOrEmpty(ssmDoc.DocOfCompanyRegNo)))
                {
                    bool isMatch = false;

                    if (!string.IsNullOrEmpty(ssmDoc.DocOfCompanyRegNo) && !string.IsNullOrEmpty(companyRegNo))
                        isMatch = ssmDoc.DocOfCompanyRegNo.Replace(" ", "").Equals(companyRegNo.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);

                    if (!isMatch && !string.IsNullOrEmpty(ssmDoc.DocOfCompany) && !string.IsNullOrEmpty(companyName))
                        isMatch = FuzzyMatch(ssmDoc.DocOfCompany, companyName);

                    if (!isMatch)
                    {
                        _log.LogWarning("Company mismatch: SSM='{SsmName}' vs {DocType}='{ExtractedName}'",
                            ssmDoc.DocOfCompany, docType, companyName);

                        await UpdateDocMeta(jobId, confidence, docType,
                            FileStatus.REJECTED.ToString(), "WrongCompany", ct);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Company extraction/validation failed for job {JobId}", jobId);
        }
    }

    private static bool FuzzyMatch(string a, string b)
    {
        static string Normalize(string s) => s.ToUpperInvariant()
            .Replace("SDN BHD", "").Replace("SDN. BHD.", "")
            .Replace("BHD", "").Replace(".", "").Replace(",", "")
            .Trim();

        var na = Normalize(a);
        var nb = Normalize(b);
        if (na == nb) return true;
        if (na.Contains(nb) || nb.Contains(na)) return true;

        // Simple Levenshtein-based similarity
        int maxLen = Math.Max(na.Length, nb.Length);
        if (maxLen == 0) return true;
        int dist = LevenshteinDistance(na, nb);
        double similarity = 1.0 - (double)dist / maxLen;
        return similarity >= 0.80;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        int[,] d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    private static string GetFieldValue(IReadOnlyDictionary<string, DocumentField> fields, string key)
    {
        if (!fields.TryGetValue(key, out var field)) return "";
        return (field.Content ?? "").TrimStart(':').Trim();
    }

    private async Task UpdateDocMeta(string jobId, double confidence, string docType,
        string fileState, string? tags, CancellationToken ct)
    {
        var doc = await _db.SpotDocuments.FirstOrDefaultAsync(d => d.JobId == jobId, ct);
        if (doc == null) return;

        doc.Confidence = confidence;
        doc.DocumentType = docType;
        doc.FileState = fileState;
        doc.FileProcessState = JobState.Success.ToString();

        if (tags != null)
        {
            doc.Tags = string.IsNullOrEmpty(doc.Tags) ? tags :
                doc.Tags.Contains(tags) ? doc.Tags : $"{doc.Tags},{tags}";
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task CompleteJob(string jobId, CancellationToken ct)
    {
        var job = await _db.SpotJobs.FirstOrDefaultAsync(j => j.JobId.ToString() == jobId, ct);
        if (job != null)
        {
            job.Status = JobState.Success.ToString();
            job.FinishedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task FailJob(string jobId, string message, CancellationToken ct)
    {
        var job = await _db.SpotJobs.FirstOrDefaultAsync(j => j.JobId.ToString() == jobId, ct);
        if (job != null)
        {
            job.Status = JobState.Failed.ToString();
            job.FinishedAt = DateTime.UtcNow;
            job.Notes = message;
            await _db.SaveChangesAsync(ct);
        }

        await _notifications.CreateErrorNotificationAsync(
            Core.Enums.ProjectName.AGONESPot, "ClassificationService", "ClassifyWorker",
            message, null, jobId, null, ct);
    }

    private string GenerateSecureBlobUrl(string blobUrl)
    {
        try
        {
            var uri = new Uri(blobUrl);
            var pathParts = uri.AbsolutePath.TrimStart('/').Split('/', 2);
            if (pathParts.Length < 2) return blobUrl;

            var containerClient = _blobService.GetBlobContainerClient(pathParts[0]);
            var blobClient = containerClient.GetBlobClient(pathParts[1]);

            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = pathParts[0],
                BlobName = pathParts[1],
                Resource = "b",
                ExpiresOn = DateTimeOffset.UtcNow.AddHours(1)
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            return blobClient.GenerateSasUri(sasBuilder).ToString();
        }
        catch
        {
            return blobUrl;
        }
    }

    private static object MapToDto(SpotDocumentMetadata d) => new
    {
        d.FileUID, d.JobId, d.CompanyId, d.FileName, d.FileSize, d.FileType,
        d.FileHash, d.FileState, d.DocumentType, d.Confidence,
        d.FileURL, d.Tags, d.DocOfCompany, d.DocOfCompanyRegNo, d.CreatedDate
    };
}
