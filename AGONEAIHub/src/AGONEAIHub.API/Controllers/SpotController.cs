using AGONEAIHub.Application.DTOs;
using AGONEAIHub.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AGONEAIHub.API.Controllers;

/// <summary>
/// AGONESPot-specific AI functions.
/// Prompts are loaded from DB (Project=AGONESPot, Module=FileClassification / ReportGeneration).
/// </summary>
[ApiController]
[Route("api/spot")]
[Tags("AGONESPot")]
public class SpotController : ControllerBase
{
    private readonly ISpotService _spot;

    public SpotController(ISpotService spot) => _spot = spot;

    /// <summary>
    /// Classify a file — determines type (Invoice, Contract, etc.), category, and risk level.
    /// Uses prompt: Module=FileClassification, Section=General, PromptKey=classify-file
    /// </summary>
    [HttpPost("classify-file")]
    public async Task<ActionResult<ClassifyFileResponse>> ClassifyFile(
        [FromBody] ClassifyFileRequest request, CancellationToken ct)
    {
        var result = await _spot.ClassifyFileAsync(
            request.FileName, request.FileContent, request.AdditionalContext, ct);

        return Ok(new ClassifyFileResponse
        {
            Success = result.Success,
            FileType = result.FileType,
            Category = result.Category,
            RiskLevel = result.RiskLevel,
            Summary = result.Summary,
            CorrelationId = result.CorrelationId,
            DurationMs = result.DurationMs,
            ErrorMessage = result.ErrorMessage
        });
    }

    /// <summary>
    /// Generate a Spot audit report with multiple sections (A, B, C, D).
    /// Each section uses its own prompt from DB.
    /// Pass ReportData with your audit findings, and optionally specify which sections to generate.
    /// </summary>
    [HttpPost("generate-report")]
    public async Task<ActionResult<GenerateReportResponse>> GenerateReport(
        [FromBody] GenerateReportRequest request, CancellationToken ct)
    {
        var result = await _spot.GenerateSpotReportAsync(
            request.ReportTitle, request.ReportData, request.Sections, ct);

        return Ok(new GenerateReportResponse
        {
            Success = result.Success,
            ReportTitle = result.ReportTitle,
            SectionResults = result.SectionResults,
            CorrelationId = result.CorrelationId,
            TotalDurationMs = result.TotalDurationMs,
            ErrorMessage = result.ErrorMessage
        });
    }
}
