namespace OdooToZohoMigration.Core.Entities;

public class MigrationRun
{
    public int Id { get; set; }
    public string RunType { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string Status { get; set; } = string.Empty;

    public int TotalJobs { get; set; }
    public int MigratedJobs { get; set; }
    public int FailedJobs { get; set; }

    public int TotalCandidates { get; set; }
    public int MigratedCandidates { get; set; }
    public int FailedCandidates { get; set; }

    public int TotalApplications { get; set; }
    public int MigratedApplications { get; set; }
    public int FailedApplications { get; set; }

    public int TotalCvsUploaded { get; set; }
    public int FailedCvUploads { get; set; }

    public string? Notes { get; set; }
}
