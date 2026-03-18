namespace OdooToZohoMigration.Core.Entities;

public class ApplicationHistory
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public int ApplicationOdooId { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime ChangeDate { get; set; }

    // Migration tracking
    public string? ZohoNoteId { get; set; }
    public bool IsMigrated { get; set; }
    public DateTime? MigratedAt { get; set; }

    // Navigation
    public Application Application { get; set; } = null!;
}
