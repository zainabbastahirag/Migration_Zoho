namespace OdooToZohoMigration.Core.DTOs.Migration;

/// <summary>
/// Comprehensive sync dashboard showing the state of every entity type.
/// Use this to track exactly what's been synced and what hasn't.
/// </summary>
public class SyncDashboardDto
{
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    public EntitySyncOverview Jobs { get; set; } = new();
    public EntitySyncOverview Candidates { get; set; } = new();
    public CandidateCvSyncOverview CandidateCvs { get; set; } = new();
    public EntitySyncOverview Applications { get; set; } = new();
    public EntitySyncOverview Comments { get; set; } = new();
    public EntitySyncOverview Summaries { get; set; } = new();
    public EntitySyncOverview History { get; set; } = new();

    public int TotalRecords { get; set; }
    public int TotalSynced { get; set; }
    public int TotalPending { get; set; }
    public int TotalFailed { get; set; }
    public double OverallPercentComplete => TotalRecords > 0
        ? Math.Round((double)TotalSynced / TotalRecords * 100, 2) : 0;

    /// <summary>
    /// Last migration run info
    /// </summary>
    public LastRunInfo? LastRun { get; set; }
}

public class EntitySyncOverview
{
    public string EntityType { get; set; } = string.Empty;
    public int Total { get; set; }
    public int Synced { get; set; }
    public int Pending { get; set; }
    public int Failed { get; set; }
    public double PercentComplete => Total > 0
        ? Math.Round((double)Synced / Total * 100, 2) : 0;
    public DateTime? LastSyncedAt { get; set; }
}

public class CandidateCvSyncOverview : EntitySyncOverview
{
    /// <summary>
    /// Candidates that are migrated but have a CV that hasn't been uploaded yet
    /// </summary>
    public int PendingCvUploads { get; set; }

    /// <summary>
    /// Candidates that have no ResumeUrl at all (nothing to upload)
    /// </summary>
    public int NoCvAvailable { get; set; }
}

public class LastRunInfo
{
    public int RunId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string RunType { get; set; } = string.Empty;
}
