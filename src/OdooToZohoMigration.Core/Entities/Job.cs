namespace OdooToZohoMigration.Core.Entities;

public class Job
{
    public int Id { get; set; }
    public int OdooId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Department { get; set; }
    public string? Status { get; set; }
    public DateTime LastSyncedAt { get; set; }

    // Migration tracking
    public string? ZohoJobId { get; set; }
    public bool IsMigrated { get; set; }
    public DateTime? MigratedAt { get; set; }
    public string? MigrationError { get; set; }
}
