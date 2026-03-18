namespace AGONEAIHub.Core.Interfaces;

public interface IDataroomService
{
    Task<DataroomResult> GetSummaryAsync(string companyId, CancellationToken ct = default);
    Task<DataroomResult> GetCompanyFilesAsync(string companyId, CancellationToken ct = default);
    Task<DataroomResult> DeleteFileAsync(string companyId, string userId, string fileId, CancellationToken ct = default);
    Task<DataroomResult> UpdateDocumentStatusAsync(string companyId, string fileId, string status, CancellationToken ct = default);
    Task<DataroomResult> ExtractCompanyProfileAsync(string companyId, CancellationToken ct = default);
    Task<DataroomResult> GetCompaniesStatusAsync(List<string> companyIds, CancellationToken ct = default);
    Task<DataroomResult> ValidateCompanyDocumentsAsync(string companyId, CancellationToken ct = default);
}

public class DataroomResult
{
    public int StatusCode { get; set; } = 200;
    public string Message { get; set; } = string.Empty;
    public object? Data { get; set; }
}
