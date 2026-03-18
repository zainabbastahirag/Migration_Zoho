using AGONEAIHub.Core.Enums;
using AGONEAIHub.Core.Interfaces;

namespace AGONEAIHub.Application.DTOs;

public class SearchRequest
{
    public ProjectName Project { get; set; }
    public string IndexName { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public int Top { get; set; } = 10;
    public string? Filter { get; set; }
}

public class SearchResponse
{
    public bool Success { get; set; }
    public List<Dictionary<string, object>> Results { get; set; } = new();
    public long TotalCount { get; set; }
    public long DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
}

public class IndexDocumentsRequest
{
    public ProjectName Project { get; set; }
    public string IndexName { get; set; } = string.Empty;
    public List<Dictionary<string, object>> Documents { get; set; } = new();
}

public class CreateIndexRequest
{
    public ProjectName Project { get; set; }
    public string IndexName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<SearchFieldDefinition> Fields { get; set; } = new();
}
