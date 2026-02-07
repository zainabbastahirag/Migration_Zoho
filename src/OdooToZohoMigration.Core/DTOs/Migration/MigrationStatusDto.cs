namespace OdooToZohoMigration.Core.DTOs.Migration;

public class MigrationStatusDto
{
    public int RunId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public MigrationProgressDto Jobs { get; set; } = new();
    public MigrationProgressDto Candidates { get; set; } = new();
    public MigrationProgressDto Applications { get; set; } = new();
    public MigrationProgressDto CvUploads { get; set; } = new();
}

public class MigrationProgressDto
{
    public int Total { get; set; }
    public int Migrated { get; set; }
    public int Failed { get; set; }
    public int Pending => Total - Migrated - Failed;
    public double PercentComplete => Total > 0 ? Math.Round((double)Migrated / Total * 100, 2) : 0;
}
