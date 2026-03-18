using System.Text.Json;
using AGONEAIHub.Core.Entities.Spot;
using AGONEAIHub.Core.Enums.Spot;
using AGONEAIHub.Core.Interfaces;
using AGONEAIHub.Infrastructure.Data;
using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AGONEAIHub.Infrastructure.Services.Spot;

public class DataroomService : IDataroomService
{
    private readonly AIHubDbContext _db;
    private readonly SpotSettings _cfg;
    private readonly BlobServiceClient _blob;
    private readonly DocumentAnalysisClient _docInt;
    private readonly ILogger<DataroomService> _log;

    private static readonly HashSet<string> EligibleTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SSM", "CTOS", "AUDIT_REPORTS", "AUDIT_REPORT", "CTOS_REPORT", "Supplementary"
    };

    private static readonly string[] MalaysiaStates =
    {
        "Johor","Kedah","Kelantan","Melaka","Negeri Sembilan","Pahang","Perak","Perlis",
        "Pulau Pinang","Sabah","Sarawak","Selangor","Terengganu","Kuala Lumpur","Putrajaya","Labuan"
    };

    public DataroomService(
        AIHubDbContext db, IOptions<SpotSettings> cfg, ILogger<DataroomService> log)
    {
        _db = db;
        _cfg = cfg.Value;
        _log = log;
        _blob = new BlobServiceClient(_cfg.BlobConnectionString);
        _docInt = new DocumentAnalysisClient(new Uri(_cfg.DocIntEndpoint), new AzureKeyCredential(_cfg.DocIntKey));
    }

    // ================================================================
    //  GET /dataroom/summary/{companyId}
    // ================================================================

    public async Task<DataroomResult> GetSummaryAsync(string companyId, CancellationToken ct = default)
    {
        companyId = companyId.ToUpper();
        var docs = await GetEligibleDocsAsync(companyId, ct);

        int pending = 0, processed = 0, approved = 0, rejected = 0;
        foreach (var d in docs)
        {
            switch (d.FileState)
            {
                case nameof(FileStatus.REJECTED): rejected++; break;
                case nameof(FileStatus.APPROVED): approved++; break;
                case nameof(FileStatus.PROCESSED): processed++; break;
                default: pending++; break;
            }
        }

        return new DataroomResult
        {
            StatusCode = 200,
            Message = "Retrieved company status.",
            Data = new
            {
                total = docs.Count,
                reviewed = docs.Count(d => d.Confidence.HasValue),
                pending, processed, approved, rejected
            }
        };
    }

    // ================================================================
    //  GET /company-files/{companyId}
    // ================================================================

    public async Task<DataroomResult> GetCompanyFilesAsync(string companyId, CancellationToken ct = default)
    {
        companyId = companyId.ToUpper();
        var files = await _db.SpotDocuments
            .Where(d => d.CompanyId == companyId && !d.IsDeleted)
            .OrderByDescending(d => d.CreatedDate)
            .ToListAsync(ct);

        var result = files.Select(d =>
        {
            d.FileURL = GenerateSasUrl(d.FileURL);
            return MapDoc(d);
        }).ToList();

        return new DataroomResult
        {
            StatusCode = 200,
            Message = "Fetched company files.",
            Data = result
        };
    }

    // ================================================================
    //  DELETE /delete-file/company/{cid}/user/{uid}/file/{fid}
    // ================================================================

    public async Task<DataroomResult> DeleteFileAsync(
        string companyId, string userId, string fileId, CancellationToken ct = default)
    {
        companyId = companyId.ToUpper();
        fileId = fileId.ToUpper();
        userId = userId.ToUpper();

        var doc = await _db.SpotDocuments.FirstOrDefaultAsync(d =>
            d.CompanyId == companyId && d.FileUID == fileId, ct);

        if (doc == null)
            return new DataroomResult { StatusCode = 404, Message = "File not found." };

        doc.IsDeleted = true;
        doc.DeletedDate = DateTime.UtcNow;
        doc.DeletedBy = userId;
        await _db.SaveChangesAsync(ct);

        return new DataroomResult
        {
            StatusCode = 200,
            Message = "File deleted.",
            Data = new { fileUID = fileId }
        };
    }

    // ================================================================
    //  POST /dataroom/update-document-status/{companyId}
    // ================================================================

    public async Task<DataroomResult> UpdateDocumentStatusAsync(
        string companyId, string fileId, string status, CancellationToken ct = default)
    {
        if (!Enum.TryParse<FileStatus>(status, true, out _))
            return new DataroomResult { StatusCode = 400, Message = $"Invalid status: {status}" };

        var doc = await _db.SpotDocuments.FirstOrDefaultAsync(d => d.FileUID == fileId, ct);
        if (doc == null)
            return new DataroomResult { StatusCode = 404, Message = "File not found." };

        doc.FileState = status;
        await _db.SaveChangesAsync(ct);

        doc.FileURL = GenerateSasUrl(doc.FileURL);

        return new DataroomResult
        {
            StatusCode = 200,
            Message = "Status updated.",
            Data = MapDoc(doc)
        };
    }

    // ================================================================
    //  GET /extract/company-profile?companyId=
    // ================================================================

    public async Task<DataroomResult> ExtractCompanyProfileAsync(string companyId, CancellationToken ct = default)
    {
        companyId = companyId.ToUpper();

        var ssmDoc = await _db.SpotDocuments.FirstOrDefaultAsync(d =>
            d.CompanyId == companyId
            && d.DocumentType == CoreDocumentTypes.SSM
            && !d.IsDeleted, ct);

        if (ssmDoc == null)
            return new DataroomResult { StatusCode = 404, Message = "No SSM document found." };

        // Check cache — if profile was already extracted from this same SSM file
        var existing = await _db.SpotCompanies.FirstOrDefaultAsync(c => c.CompanyId == companyId, ct);
        if (existing?.CompanyProfileSourceFileId == ssmDoc.FileUID && existing.CompanyProfileJson != null)
        {
            try
            {
                var cached = JsonSerializer.Deserialize<Dictionary<string, object>>(existing.CompanyProfileJson);
                return new DataroomResult { StatusCode = 200, Message = "Company profile extracted.", Data = cached };
            }
            catch { /* re-extract if JSON corrupt */ }
        }

        // Extract using Azure Document Intelligence custom model
        Dictionary<string, string> fields;
        try
        {
            var secureUrl = GenerateSasUrl(ssmDoc.FileURL);
            var poller = await _docInt.AnalyzeDocumentFromUriAsync(
                WaitUntil.Completed, _cfg.CompanyProfileModelId, new Uri(secureUrl), cancellationToken: ct);

            var result = poller.Value;
            if (result.Documents.Count == 0)
                return new DataroomResult { StatusCode = 400, Message = "No data extracted from SSM." };

            var docFields = result.Documents[0].Fields;
            fields = new Dictionary<string, string>
            {
                ["companyName"] = GetField(docFields, "company-name"),
                ["companyRegistrationNumber"] = GetField(docFields, "registeration-no"),
                ["address"] = GetField(docFields, "address"),
                ["principalBusiness"] = GetField(docFields, "principle-business"),
                ["dateOfIncorporation"] = GetField(docFields, "date-of-incorporation"),
                ["auditor"] = GetField(docFields, "auditor"),
                ["businessLicenseNumber"] = GetField(docFields, "business-license-num"),
                ["taxRegistrationNumber"] = GetField(docFields, "tax-registration-num"),
                ["keyPersonnel"] = GetField(docFields, "key-personal"),
            };

            // Derive location from address
            fields["location"] = ExtractState(fields.GetValueOrDefault("address"));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "DI extraction failed for company {Company}", companyId);
            return new DataroomResult { StatusCode = 500, Message = "DI extraction failed." };
        }

        // Save to DB
        var profileJson = JsonSerializer.Serialize(fields);
        if (existing != null)
        {
            existing.CompanyProfileJson = profileJson;
            existing.CompanyProfileSourceFileId = ssmDoc.FileUID;
            existing.CreatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.SpotCompanies.Add(new SpotCompany
            {
                CompanyId = companyId,
                CompanyProfileJson = profileJson,
                CompanyProfileSourceFileId = ssmDoc.FileUID,
                CreatedAt = DateTime.UtcNow
            });
        }
        await _db.SaveChangesAsync(ct);

        return new DataroomResult { StatusCode = 200, Message = "Company profile extracted.", Data = fields };
    }

    // ================================================================
    //  POST /dataroom/companies-status
    // ================================================================

    public async Task<DataroomResult> GetCompaniesStatusAsync(List<string> companyIds, CancellationToken ct = default)
    {
        var upperIds = companyIds.Select(c => c.ToUpper()).ToList();

        var counts = await _db.SpotDocuments
            .Where(d => upperIds.Contains(d.CompanyId) && !d.IsDeleted)
            .GroupBy(d => d.CompanyId)
            .Select(g => new { companyId = g.Key, totalDocument = g.Count() })
            .ToListAsync(ct);

        var mapped = counts.ToDictionary(x => x.companyId, x => x.totalDocument);
        var result = upperIds.Select(cid => new
        {
            companyId = cid,
            totalDocument = mapped.GetValueOrDefault(cid, 0)
        });

        return new DataroomResult { StatusCode = 200, Message = "OK", Data = result };
    }

    // ================================================================
    //  GET /validate-company-documents?companyId=
    // ================================================================

    public async Task<DataroomResult> ValidateCompanyDocumentsAsync(string companyId, CancellationToken ct = default)
    {
        companyId = companyId.ToUpper();

        var coreDocs = await _db.SpotDocuments
            .Where(d => d.CompanyId == companyId
                && CoreDocumentTypes.CoreTypes.Contains(d.DocumentType)
                && !d.IsDeleted
                && (d.Tags == null || !d.Tags.Contains("WrongType")))
            .ToListAsync(ct);

        var ssm = coreDocs.FirstOrDefault(d => d.DocumentType == CoreDocumentTypes.SSM);
        if (ssm == null)
            return new DataroomResult
            {
                StatusCode = 400,
                Message = "No SSM document found for validation.",
                Data = new { valid = false, ssmName = (string?)null, mismatches = new List<string>() }
            };

        var ssmName = ssm.DocOfCompany;
        var ssmReg = ssm.DocOfCompanyRegNo;
        var mismatches = new List<string>();

        foreach (var doc in coreDocs.Where(d => d.DocumentType != CoreDocumentTypes.SSM))
        {
            bool match = false;
            if (!string.IsNullOrEmpty(ssmReg) && !string.IsNullOrEmpty(doc.DocOfCompanyRegNo))
                match = ssmReg.Replace(" ", "").Equals(doc.DocOfCompanyRegNo.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);

            if (!match && !string.IsNullOrEmpty(ssmName) && !string.IsNullOrEmpty(doc.DocOfCompany))
                match = ssmName.Contains(doc.DocOfCompany, StringComparison.OrdinalIgnoreCase)
                     || doc.DocOfCompany.Contains(ssmName, StringComparison.OrdinalIgnoreCase);

            if (!match)
                mismatches.Add($"{doc.DocumentType} ({doc.FileUID}): name='{doc.DocOfCompany}', reg='{doc.DocOfCompanyRegNo}'");
        }

        var valid = mismatches.Count == 0;
        return new DataroomResult
        {
            StatusCode = valid ? 200 : 400,
            Message = valid ? "All documents verified." : "Company mismatch detected.",
            Data = new { valid, ssmName, mismatches }
        };
    }

    // ================================================================
    //  HELPERS
    // ================================================================

    private async Task<List<SpotDocumentMetadata>> GetEligibleDocsAsync(string companyId, CancellationToken ct) =>
        await _db.SpotDocuments
            .Where(d => d.CompanyId == companyId
                && !d.IsDeleted
                && EligibleTypes.Contains(d.DocumentType)
                && (d.Tags == null || !d.Tags.Contains("WrongType")))
            .OrderByDescending(d => d.CreatedDate)
            .ToListAsync(ct);

    private static string GetField(IReadOnlyDictionary<string, DocumentField> fields, string key)
    {
        if (!fields.TryGetValue(key, out var f)) return "";
        return (f.Content ?? "").TrimStart(':').Trim();
    }

    private static string ExtractState(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return "";
        foreach (var state in MalaysiaStates)
            if (address.Contains(state, StringComparison.OrdinalIgnoreCase)) return state;
        if (address.Contains("wp", StringComparison.OrdinalIgnoreCase) && address.Contains("kuala", StringComparison.OrdinalIgnoreCase))
            return "Kuala Lumpur";
        return "";
    }

    private string GenerateSasUrl(string? blobUrl)
    {
        if (string.IsNullOrEmpty(blobUrl)) return "";
        try
        {
            var uri = new Uri(blobUrl);
            var parts = uri.AbsolutePath.TrimStart('/').Split('/', 2);
            if (parts.Length < 2) return blobUrl;
            var client = _blob.GetBlobContainerClient(parts[0]).GetBlobClient(parts[1]);
            var sas = new BlobSasBuilder { BlobContainerName = parts[0], BlobName = parts[1], Resource = "b", ExpiresOn = DateTimeOffset.UtcNow.AddHours(1) };
            sas.SetPermissions(BlobSasPermissions.Read);
            return client.GenerateSasUri(sas).ToString();
        }
        catch { return blobUrl; }
    }

    private static object MapDoc(SpotDocumentMetadata d) => new
    {
        d.FileUID, d.JobId, d.CompanyId, d.FileName, d.FileSize, d.FileType,
        d.FileHash, d.FileState, d.DocumentType, d.Confidence,
        d.FileURL, d.Tags, d.DocOfCompany, d.DocOfCompanyRegNo, d.CreatedDate
    };
}
