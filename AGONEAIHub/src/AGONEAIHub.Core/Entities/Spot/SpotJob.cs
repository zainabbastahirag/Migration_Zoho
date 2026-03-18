namespace AGONEAIHub.Core.Entities.Spot;

public class SpotJob
{
    public Guid JobId { get; set; } = Guid.NewGuid();
    public int JobType { get; set; }
    public string Status { get; set; } = "PendingQueue";
    public string CompanyId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? BlobUrl { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? ReportType { get; set; } = "";
    public string? Notes { get; set; } = "Job queued";
}
