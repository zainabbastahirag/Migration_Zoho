using AGONEAIHub.Application.DTOs;
using AGONEAIHub.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AGONEAIHub.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Tags("AI Search - Azure")]
public class SearchController : ControllerBase
{
    private readonly IAISearchService _search;

    public SearchController(IAISearchService search) => _search = search;

    /// <summary>
    /// Search an Azure AI Search index.
    /// </summary>
    [HttpPost("query")]
    public async Task<ActionResult<SearchResponse>> Search(
        [FromBody] SearchRequest request, CancellationToken ct)
    {
        var result = await _search.SearchAsync(
            request.Project, request.IndexName, request.Query,
            request.Top, request.Filter, ct);

        return Ok(new SearchResponse
        {
            Success = result.Success,
            Results = result.Results,
            TotalCount = result.TotalCount,
            DurationMs = result.DurationMs,
            ErrorMessage = result.ErrorMessage
        });
    }

    /// <summary>
    /// Upload/merge documents into a search index.
    /// </summary>
    [HttpPost("index/documents")]
    public async Task<IActionResult> IndexDocuments(
        [FromBody] IndexDocumentsRequest request, CancellationToken ct)
    {
        var result = await _search.IndexDocumentsAsync(
            request.Project, request.IndexName, request.Documents, ct);

        return Ok(result);
    }

    /// <summary>
    /// Create or update a search index schema.
    /// </summary>
    [HttpPost("index/create")]
    public async Task<IActionResult> CreateIndex(
        [FromBody] CreateIndexRequest request, CancellationToken ct)
    {
        var result = await _search.CreateOrUpdateIndexAsync(
            request.Project, request.IndexName, request.Fields, ct);

        return Ok(result);
    }
}
