namespace AGONEAIHub.Core.Entities;

/// <summary>
/// Tracks Azure AI Search indexes used by each project.
/// </summary>
public class SearchIndexConfig : BaseEntity
{
    public string IndexName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? FieldsJson { get; set; }
    public bool IsActive { get; set; } = true;
}
