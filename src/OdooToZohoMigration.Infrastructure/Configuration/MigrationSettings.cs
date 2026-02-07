namespace OdooToZohoMigration.Infrastructure.Configuration;

/// <summary>
/// All migration config lives here. Set Enabled=true to start migration on app launch.
/// Set Enabled=false to stop. Each phase has its own on/off toggle.
/// </summary>
public class MigrationSettings
{
    /// <summary>Master switch: set to true to run migration on startup, false to skip.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Also retry records that previously failed (have MigrationError set).</summary>
    public bool RetryFailed { get; set; } = false;

    // ── Phase toggles ────────────────────────────────────────────────
    public bool MigrateJobs { get; set; } = true;
    public bool MigrateCandidates { get; set; } = true;
    public bool MigrateCvs { get; set; } = true;
    public bool MigrateApplications { get; set; } = true;
    public bool MigrateComments { get; set; } = true;
    public bool MigrateSummaries { get; set; } = true;
    public bool MigrateHistory { get; set; } = true;

    // ── Performance tuning ───────────────────────────────────────────
    /// <summary>How many records to process per batch (Zoho max = 100).</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>Milliseconds to wait between batches.</summary>
    public int DelayBetweenBatchesMs { get; set; } = 2000;

    /// <summary>Parallel CV upload threads.</summary>
    public int MaxConcurrentCvUploads { get; set; } = 3;

    /// <summary>Seconds between periodic sync runs (0 = run once then stop).</summary>
    public int RepeatIntervalSeconds { get; set; } = 0;
}
