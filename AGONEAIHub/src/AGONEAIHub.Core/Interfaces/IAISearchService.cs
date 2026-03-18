using AGONEAIHub.Core.Enums;

namespace AGONEAIHub.Core.Interfaces;

public interface IAISearchService
{
    /// <summary>
    /// Search an Azure AI Search index.
    /// </summary>
    Task<SearchResult> SearchAsync(
        ProjectName project, string indexName, string query,
        int top = 10, string? filter = null,
        CancellationToken ct = default);

    /// <summary>
    /// Upload/merge documents into an Azure AI Search index.
    /// </summary>
    Task<IndexResult> IndexDocumentsAsync(
        ProjectName project, string indexName,
        List<Dictionary<string, object>> documents,
        CancellationToken ct = default);

    /// <summary>
    /// Create or update an Azure AI Search index schema.
    /// </summary>
    Task<IndexResult> CreateOrUpdateIndexAsync(
        ProjectName project, string indexName,
        List<SearchFieldDefinition> fields,
        CancellationToken ct = default);
}

public class SearchResult
{
    public bool Success { get; set; }
    public List<Dictionary<string, object>> Results { get; set; } = new();
    public long TotalCount { get; set; }
    public string? ErrorMessage { get; set; }
    public long DurationMs { get; set; }
}

public class IndexResult
{
    public bool Success { get; set; }
    public int DocumentsProcessed { get; set; }
    public string? ErrorMessage { get; set; }
}

public class SearchFieldDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "Edm.String";
    public bool IsKey { get; set; }
    public bool IsSearchable { get; set; } = true;
    public bool IsFilterable { get; set; }
    public bool IsSortable { get; set; }
}
