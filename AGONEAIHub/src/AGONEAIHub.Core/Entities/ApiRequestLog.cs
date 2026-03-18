namespace AGONEAIHub.Core.Entities;

/// <summary>
/// Logs every HTTP request + response to the API.
/// Auto-captured by middleware — no manual code needed in controllers.
/// </summary>
public class ApiRequestLog
{
    public long Id { get; set; }
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");

    public string HttpMethod { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? QueryString { get; set; }
    public string? RequestBody { get; set; }
    public string? RequestHeaders { get; set; }

    public int StatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public long DurationMs { get; set; }

    public string? Project { get; set; }
    public string? UserAgent { get; set; }
    public string? ClientIp { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
