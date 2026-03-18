namespace AGONEAIHub.Core.Entities;

/// <summary>
/// Tracks document processing requests via Azure Document Intelligence.
/// </summary>
public class DocumentProcessingJob : BaseEntity
{
    public string DocumentUrl { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string ModelId { get; set; } = "prebuilt-document";
    public string Status { get; set; } = "Pending";
    public string? ExtractedDataJson { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}
