using AGONEAIHub.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AGONEAIHub.API.Controllers;

/// <summary>
/// AGONESPot file classification — upload PDF, classify with Azure Document Intelligence,
/// validate against SSM, enforce company matching, CTOS logo check.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Tags("AGONESPot - File Classification")]
public class ClassifyController : ControllerBase
{
    private readonly IClassificationService _classify;

    public ClassifyController(IClassificationService classify) => _classify = classify;

    /// <summary>
    /// Upload a PDF for classification.
    /// Stores in Azure Blob, creates a job, queues for Azure Document Intelligence classification.
    /// Returns immediately with file metadata + job ID.
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ClassifyResult>> Classify(
        [FromForm] string companyId,
        [FromForm] string userId,
        IFormFile file,
        CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new ClassifyResult { StatusCode = 400, Message = "No file provided." });

        using var stream = file.OpenReadStream();
        var result = await _classify.ClassifyAsync(
            companyId, userId, file.FileName, stream, file.Length, ct);

        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Check classification status for a file.
    /// Returns current state: pending, classified, or failed.
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<ClassifyStatusResult>> GetStatus(
        [FromQuery] string fileId, CancellationToken ct)
    {
        var result = await _classify.GetStatusAsync(fileId, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Worker endpoint: runs the actual Azure Document Intelligence classification.
    /// In production, called by Service Bus consumer.
    /// In development, called directly.
    /// </summary>
    [HttpPost("worker")]
    public async Task<ActionResult<ClassifyResult>> ClassifyWorker(
        [FromForm] string jobId,
        [FromForm] int jobType,
        CancellationToken ct)
    {
        var result = await _classify.ClassifyWorkerAsync(jobId, jobType, ct);
        return StatusCode(result.StatusCode, result);
    }
}
