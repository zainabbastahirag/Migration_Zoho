namespace OdooToZohoMigration.Infrastructure.Configuration;

/// <summary>
/// Full migration config.  Set Enabled=true → app starts migrating on launch.
/// Every batch size, delay and concurrency level is configurable per entity type.
/// </summary>
public class MigrationSettings
{
    // ── Master switch ─────────────────────────────────────────────────
    /// <summary>true = run migration on startup, false = do nothing.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Also retry records that previously failed (have MigrationError).</summary>
    public bool RetryFailed { get; set; } = false;

    // ── Phase toggles ─────────────────────────────────────────────────
    public bool MigrateJobs { get; set; } = true;
    public bool MigrateCandidates { get; set; } = true;
    public bool MigrateCvs { get; set; } = true;
    public bool MigrateApplications { get; set; } = true;
    public bool MigrateComments { get; set; } = true;
    public bool MigrateSummaries { get; set; } = true;
    public bool MigrateHistory { get; set; } = true;

    // ── Per-entity batch sizes (how many records per Zoho API call) ───
    // Zoho hard limit = 100 per batch insert.  Keep ≤ 100.
    public int JobsBatchSize { get; set; } = 50;
    public int CandidatesBatchSize { get; set; } = 50;
    public int ApplicationsBatchSize { get; set; } = 25;
    public int CvsBatchSize { get; set; } = 20;
    public int CommentsBatchSize { get; set; } = 50;
    public int SummariesBatchSize { get; set; } = 50;
    public int HistoryBatchSize { get; set; } = 50;

    // ── Per-entity delay between individual API calls (ms) ────────────
    // These protect you from Zoho 429 "Too Many Requests".
    // Zoho free plan = ~200 req/min.  Paid plans vary.
    /// <summary>Delay (ms) after each batch of jobs is sent to Zoho.</summary>
    public int JobsDelayMs { get; set; } = 2000;

    /// <summary>Delay (ms) after each batch of candidates is sent to Zoho.</summary>
    public int CandidatesDelayMs { get; set; } = 2000;

    /// <summary>Delay (ms) between each individual CV upload.</summary>
    public int CvsDelayMs { get; set; } = 1500;

    /// <summary>Delay (ms) between each individual application associate call.</summary>
    public int ApplicationsDelayMs { get; set; } = 2000;

    /// <summary>Delay (ms) after each batch of comments is sent to Zoho.</summary>
    public int CommentsDelayMs { get; set; } = 2000;

    /// <summary>Delay (ms) after each batch of summaries is sent to Zoho.</summary>
    public int SummariesDelayMs { get; set; } = 2000;

    /// <summary>Delay (ms) after each batch of history is sent to Zoho.</summary>
    public int HistoryDelayMs { get; set; } = 2000;

    // ── CV concurrency ────────────────────────────────────────────────
    /// <summary>Parallel CV upload threads (1 = sequential).</summary>
    public int MaxConcurrentCvUploads { get; set; } = 3;

    // ── Unlock Job Openings ─────────────────────────────────────────
    /// <summary>
    /// Enable Phase 0: reassign migrated job openings across multiple
    /// Zoho recruiters so they don't exceed per-recruiter limits.
    /// Runs BEFORE Phase 1 (new jobs).
    /// </summary>
    public bool UnlockJobOpenings { get; set; } = false;

    /// <summary>
    /// List of Zoho Recruit recruiter email addresses.
    /// Jobs are distributed round-robin across these recruiters.
    /// Example: ["recruiter1@company.com","recruiter2@company.com"]
    /// </summary>
    public List<string> RecruiterEmails { get; set; } = new();

    public int UnlockJobsBatchSize { get; set; } = 25;
    public int UnlockJobsDelayMs { get; set; } = 2000;

    // ── Repeat ────────────────────────────────────────────────────────
    /// <summary>Seconds between periodic runs (0 = run once then stop).</summary>
    public int RepeatIntervalSeconds { get; set; } = 0;
}
