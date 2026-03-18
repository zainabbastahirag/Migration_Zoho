namespace AGONEAIHub.Core.Interfaces;

public interface IClassificationService
{
    Task<ClassifyResult> ClassifyAsync(
        string companyId, string userId, string fileName,
        Stream fileStream, long fileSize,
        CancellationToken ct = default);

    Task<ClassifyStatusResult> GetStatusAsync(string fileId, CancellationToken ct = default);

    Task<ClassifyResult> ClassifyWorkerAsync(string jobId, int jobType, CancellationToken ct = default);
}

public class ClassifyResult
{
    public int StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public object? Data { get; set; }
}

public class ClassifyStatusResult
{
    public int StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public object? Data { get; set; }
}
