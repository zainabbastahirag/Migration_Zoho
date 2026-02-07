namespace OdooToZohoMigration.Core.DTOs.Migration;

public class RetryMigrationDto
{
    /// <summary>
    /// Entity type to retry: "All", "Job", "Candidate", "Application", "Cv", "Comment", "Summary", "History"
    /// </summary>
    public string EntityType { get; set; } = "All";

    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Only retry records with a specific error message pattern (optional).
    /// </summary>
    public string? ErrorFilter { get; set; }
}
