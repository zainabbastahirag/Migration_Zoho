namespace OdooToZohoMigration.Core.Entities;

public class ApplicationComment
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public int ApplicationOdooId { get; set; }
    public string Author { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    // Migration tracking
    public string? ZohoNoteId { get; set; }
    public bool IsMigrated { get; set; }
    public DateTime? MigratedAt { get; set; }

    // Navigation
    public Application Application { get; set; } = null!;
}
