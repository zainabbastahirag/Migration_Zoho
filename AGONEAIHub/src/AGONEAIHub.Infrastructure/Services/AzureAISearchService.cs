using System.Diagnostics;
using System.Text.Json;
using AGONEAIHub.Core.Entities;
using AGONEAIHub.Core.Enums;
using AGONEAIHub.Core.Interfaces;
using AGONEAIHub.Infrastructure.Configuration;
using AGONEAIHub.Infrastructure.Data;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AGONEAIHub.Infrastructure.Services;

public class AzureAISearchService : IAISearchService
{
    private readonly SearchIndexClient _indexClient;
    private readonly AzureAISearchSettings _settings;
    private readonly AIHubDbContext _db;
    private readonly ILogger<AzureAISearchService> _log;

    public AzureAISearchService(
        IOptions<AzureAISearchSettings> settings,
        AIHubDbContext db,
        ILogger<AzureAISearchService> log)
    {
        _settings = settings.Value;
        _db = db;
        _log = log;
        _indexClient = new SearchIndexClient(
            new Uri(_settings.Endpoint),
            new AzureKeyCredential(_settings.ApiKey));
    }

    public async Task<SearchResult> SearchAsync(
        ProjectName project, string indexName, string query,
        int top = 10, string? filter = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var searchClient = _indexClient.GetSearchClient(indexName);
            var options = new SearchOptions
            {
                Size = top,
                IncludeTotalCount = true
            };

            if (!string.IsNullOrWhiteSpace(filter))
                options.Filter = filter;

            var response = await searchClient.SearchAsync<SearchDocument>(query, options, ct);
            var results = new List<Dictionary<string, object>>();

            await foreach (var result in response.Value.GetResultsAsync())
            {
                results.Add(result.Document.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value));
            }

            sw.Stop();
            return new SearchResult
            {
                Success = true,
                Results = results,
                TotalCount = response.Value.TotalCount ?? results.Count,
                DurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _log.LogError(ex, "Search failed on index {Index} for {Project}", indexName, project);
            return new SearchResult { Success = false, ErrorMessage = ex.Message, DurationMs = sw.ElapsedMilliseconds };
        }
    }

    public async Task<IndexResult> IndexDocumentsAsync(
        ProjectName project, string indexName,
        List<Dictionary<string, object>> documents,
        CancellationToken ct = default)
    {
        try
        {
            var searchClient = _indexClient.GetSearchClient(indexName);
            var batch = IndexDocumentsBatch.MergeOrUpload(
                documents.Select(d =>
                {
                    var doc = new SearchDocument();
                    foreach (var kvp in d) doc[kvp.Key] = kvp.Value;
                    return doc;
                }));

            var response = await searchClient.IndexDocumentsAsync(batch, cancellationToken: ct);

            return new IndexResult
            {
                Success = true,
                DocumentsProcessed = response.Value.Results.Count
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Index documents failed on {Index} for {Project}", indexName, project);
            return new IndexResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<IndexResult> CreateOrUpdateIndexAsync(
        ProjectName project, string indexName,
        List<SearchFieldDefinition> fields,
        CancellationToken ct = default)
    {
        try
        {
            var searchFields = fields.Select(f =>
            {
                var field = new SearchField(f.Name, ConvertFieldType(f.Type))
                {
                    IsKey = f.IsKey,
                    IsSearchable = f.IsSearchable,
                    IsFilterable = f.IsFilterable,
                    IsSortable = f.IsSortable
                };
                return field;
            }).ToList();

            var index = new SearchIndex(indexName) { Fields = searchFields };
            await _indexClient.CreateOrUpdateIndexAsync(index, cancellationToken: ct);

            // Track in DB
            var existing = _db.SearchIndexConfigs.FirstOrDefault(s =>
                s.Project == project && s.IndexName == indexName);
            if (existing == null)
            {
                _db.SearchIndexConfigs.Add(new SearchIndexConfig
                {
                    Project = project,
                    IndexName = indexName,
                    FieldsJson = JsonSerializer.Serialize(fields),
                    CreatedAt = DateTime.UtcNow
                });
            }
            else
            {
                existing.FieldsJson = JsonSerializer.Serialize(fields);
                existing.UpdatedAt = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync(ct);

            return new IndexResult { Success = true };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Create/update index failed: {Index} for {Project}", indexName, project);
            return new IndexResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private static SearchFieldDataType ConvertFieldType(string type) => type switch
    {
        "Edm.String" => SearchFieldDataType.String,
        "Edm.Int32" => SearchFieldDataType.Int32,
        "Edm.Int64" => SearchFieldDataType.Int64,
        "Edm.Double" => SearchFieldDataType.Double,
        "Edm.Boolean" => SearchFieldDataType.Boolean,
        "Edm.DateTimeOffset" => SearchFieldDataType.DateTimeOffset,
        _ => SearchFieldDataType.String
    };
}
