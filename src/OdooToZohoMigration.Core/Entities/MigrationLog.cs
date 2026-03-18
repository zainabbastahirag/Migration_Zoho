namespace OdooToZohoMigration.Core.Entities;

public class MigrationLog
{
    public int Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public int? OdooId { get; set; }
    public string? ZohoId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public string? ErrorDetails { get; set; }
    public int? RetryCount { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? RequestPayload { get; set; }
    public string? ResponsePayload { get; set; }
}
