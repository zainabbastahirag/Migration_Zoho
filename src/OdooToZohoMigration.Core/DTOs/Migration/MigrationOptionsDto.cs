namespace OdooToZohoMigration.Core.DTOs.Migration;

public class MigrationOptionsDto
{
    public bool MigrateJobs { get; set; }
    public bool MigrateCandidates { get; set; }
    public bool MigrateApplications { get; set; }
    public bool MigrateCvs { get; set; }
    public bool MigrateComments { get; set; }
    public bool MigrateSummaries { get; set; }
    public bool MigrateHistory { get; set; }
    public bool RetryFailed { get; set; }

    /// <summary>
    /// Number of records to process per batch (sent to Zoho API).
    /// Zoho supports max 100 per batch call.
    /// </summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Number of concurrent operations for file uploads (CVs).
    /// </summary>
    public int ConcurrencyLevel { get; set; } = 3;
}
