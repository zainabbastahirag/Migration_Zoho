namespace OdooToZohoMigration.Core.Entities;

public class ApplicationSummary
{
    public int Id { get; set; }
    public int ApplicationOdooId { get; set; }
    public double ExpectedSalary { get; set; }
    public double ProposedSalary { get; set; }
    public DateTime AvailabilityDate { get; set; }
    public string Priority { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Degree { get; set; } = string.Empty;
    public string Recruiter { get; set; } = string.Empty;

    // Migration tracking
    public string? ZohoNoteId { get; set; }
    public bool IsMigrated { get; set; }
    public DateTime? MigratedAt { get; set; }

    // Navigation - ApplicationSummary links to Application via SummaryId FK
    public Application? Application { get; set; }
}
