namespace OdooToZohoMigration.Core.DTOs.Migration;

public class MigrationSummaryDto
{
    public int TotalRuns { get; set; }
    public int SuccessfulRuns { get; set; }
    public int FailedRuns { get; set; }
    public EntitySummaryDto Jobs { get; set; } = new();
    public EntitySummaryDto Candidates { get; set; } = new();
    public EntitySummaryDto Applications { get; set; } = new();
    public EntitySummaryDto CvUploads { get; set; } = new();
    public EntitySummaryDto Comments { get; set; } = new();
    public EntitySummaryDto Summaries { get; set; } = new();
    public EntitySummaryDto History { get; set; } = new();
    public DateTime? LastMigrationDate { get; set; }
}

public class EntitySummaryDto
{
    public int Total { get; set; }
    public int Migrated { get; set; }
    public int Pending { get; set; }
    public int Failed { get; set; }
    public double PercentComplete => Total > 0 ? Math.Round((double)Migrated / Total * 100, 2) : 0;
}
