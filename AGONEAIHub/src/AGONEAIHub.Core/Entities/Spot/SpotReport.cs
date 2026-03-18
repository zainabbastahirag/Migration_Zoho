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

    /// <summary>
    /// The final generated Markdown report content, stored directly in SQL.
    /// No blob storage needed for report files.
    /// </summary>
    public string? ReportMarkdown { get; set; }

    /// <summary>
    /// The final V12 JSON report structure, stored directly in SQL.
    /// </summary>
    public string? ReportJsonContent { get; set; }
}
