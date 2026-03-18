namespace AGONEAIHub.Core.Interfaces;

/// <summary>
/// Single service interface for ALL AGONESPot operations:
/// classification, dataroom, reports, tags, company registration.
/// </summary>
public interface ISpotDataService
{
    // ── Classification ───────────────────────────────────────────────
    Task<SpotResult> ClassifyAsync(string companyId, string userId, string fileName, Stream fileStream, long fileSize, CancellationToken ct = default);
    Task<SpotResult> GetClassifyStatusAsync(string fileId, CancellationToken ct = default);
    Task<SpotResult> ClassifyWorkerAsync(string jobId, int jobType, CancellationToken ct = default);

    // ── Dataroom ─────────────────────────────────────────────────────
    Task<SpotResult> GetSummaryAsync(string companyId, CancellationToken ct = default);
    Task<SpotResult> GetCompanyFilesAsync(string companyId, CancellationToken ct = default);
    Task<SpotResult> DeleteFileAsync(string companyId, string userId, string fileId, CancellationToken ct = default);
    Task<SpotResult> UpdateDocumentStatusAsync(string companyId, string fileId, string status, CancellationToken ct = default);
    Task<SpotResult> ExtractCompanyProfileAsync(string companyId, CancellationToken ct = default);
    Task<SpotResult> GetCompaniesStatusAsync(List<string> companyIds, CancellationToken ct = default);
    Task<SpotResult> ValidateCompanyDocumentsAsync(string companyId, CancellationToken ct = default);

    // ── Tags ─────────────────────────────────────────────────────────
    Task<SpotResult> UpdateDocumentTagsAsync(string companyId, string fileId, List<string> tags, CancellationToken ct = default);

    // ── Company Registration ─────────────────────────────────────────
    Task<SpotResult> RegisterCompanyAsync(string companyId, string companyName, string? representativeName, string? phone, string? address, string? email, CancellationToken ct = default);

    // ── Report Generation ────────────────────────────────────────────
    Task<SpotResult> StartReportGenerationAsync(string companyId, string type, CancellationToken ct = default);
    Task<SpotResult> GetReportStatusAsync(string companyId, string type, CancellationToken ct = default);
    Task<SpotResult> ReportWorkerAsync(string jobId, CancellationToken ct = default);
    Task<SpotResult> GetSpotStatisticsAsync(CancellationToken ct = default);
    Task<SpotResult> GetSpotHistoryAsync(string companyId, CancellationToken ct = default);
    Task<SpotResult> DeleteReportAsync(string companyId, string userId, string reportId, CancellationToken ct = default);
    Task<SpotResult> GetRecentReportsAsync(CancellationToken ct = default);
}

public class SpotResult
{
    public int StatusCode { get; set; } = 200;
    public string Message { get; set; } = string.Empty;
    public object? Data { get; set; }
}
