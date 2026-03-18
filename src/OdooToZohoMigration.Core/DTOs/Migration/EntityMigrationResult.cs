namespace OdooToZohoMigration.Core.DTOs.Migration;

public class EntityMigrationResult
{
    public bool Success { get; set; }
    public string? ZohoId { get; set; }
    public string? ZohoCandidateEmail { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorDetails { get; set; }
}
