namespace AGONEAIHub.Core.Entities.Spot;

public class SpotReport
{
    public Guid ReportId { get; set; } = Guid.NewGuid();
    public string JobId { get; set; } = string.Empty;
    public string CompanyId { get; set; } = string.Empty;
    public string ReportType { get; set; } = string.Empty;
    public string? FileURL { get; set; }
    public string? JsonURL { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? SourceFiles { get; set; }
    public string? SourceFilesSortedHash { get; set; }
    public string? GenerationType { get; set; } = "new_generated";
    public bool IsDeleted { get; set; } = false;
    public string DeletedBy { get; set; } = "";
    public DateTime? DeletedDate { get; set; }
}
