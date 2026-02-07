namespace OdooToZohoMigration.Core.Entities;

public class Stage
{
    public int Id { get; set; }
    public int OdooId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public bool IsFolded { get; set; }
}
