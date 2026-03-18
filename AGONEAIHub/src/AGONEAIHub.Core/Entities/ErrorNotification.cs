namespace AGONEAIHub.Core.Entities;

/// <summary>
/// Error notification record. Created automatically when any service call fails.
/// Can be used to build a dashboard, send emails, or trigger alerts.
/// </summary>
public class ErrorNotification : BaseEntity
{
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>Which service: Chat, DocumentIntelligence, AISearch, Spot, etc.</summary>
    public string Service { get; set; } = string.Empty;

    /// <summary>Which operation failed: ClassifyFile, GenerateReport, etc.</summary>
    public string Operation { get; set; } = string.Empty;

    public string ErrorMessage { get; set; } = string.Empty;
    public string? ErrorStackTrace { get; set; }
    public string? RequestPayload { get; set; }

    /// <summary>New, Acknowledged, Resolved</summary>
    public string Status { get; set; } = "New";

    public DateTime? AcknowledgedAt { get; set; }
    public string? AcknowledgedBy { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedBy { get; set; }
    public string? ResolutionNotes { get; set; }
}
