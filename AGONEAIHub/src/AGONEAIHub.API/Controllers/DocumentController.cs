using AGONEAIHub.Application.DTOs;
using AGONEAIHub.Core.Enums;
using AGONEAIHub.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AGONEAIHub.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Tags("Document Intelligence - Azure IDP")]
public class DocumentController : ControllerBase
{
    private readonly IDocumentIntelligenceService _docService;

    public DocumentController(IDocumentIntelligenceService docService) => _docService = docService;

    /// <summary>
    /// Analyze a document from a URL using Azure Document Intelligence.
    /// </summary>
    [HttpPost("analyze/url")]
    public async Task<ActionResult<DocumentAnalyzeResponse>> AnalyzeFromUrl(
        [FromBody] DocumentAnalyzeUrlRequest request, CancellationToken ct)
    {
        var result = await _docService.AnalyzeFromUrlAsync(
            request.Project, request.DocumentUrl, request.ModelId, ct);

        return Ok(new DocumentAnalyzeResponse
        {
            Success = result.Success,
            ExtractedDataJson = result.ExtractedDataJson,
            RawText = result.RawText,
            PageCount = result.PageCount,
            DurationMs = result.DurationMs,
            ErrorMessage = result.ErrorMessage
        });
    }

    /// <summary>
    /// Upload and analyze a document file.
    /// </summary>
    [HttpPost("analyze/upload")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<DocumentAnalyzeResponse>> AnalyzeFromUpload(
        [FromQuery] ProjectName project,
        [FromQuery] string modelId,
        IFormFile file,
        CancellationToken ct)
    {
        using var stream = file.OpenReadStream();
        var result = await _docService.AnalyzeFromStreamAsync(
            project, stream, file.FileName, modelId, ct);

        return Ok(new DocumentAnalyzeResponse
        {
            Success = result.Success,
            ExtractedDataJson = result.ExtractedDataJson,
            RawText = result.RawText,
            PageCount = result.PageCount,
            DurationMs = result.DurationMs,
            ErrorMessage = result.ErrorMessage
        });
    }
}
