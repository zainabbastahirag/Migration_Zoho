namespace OdooToZohoMigration.Core.Entities;

public class SyncLog
{
    public int Id { get; set; }
    public string SyncType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int RecordsProcessed { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime Timestamp { get; set; }
}
