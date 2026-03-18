using AGONEAIHub.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AGONEAIHub.API.Controllers;

/// <summary>
/// AGONESPot Dataroom — company files, summaries, status, company profile extraction,
/// document validation. Converted from Python FastAPI.
/// </summary>
[ApiController]
[Route("api")]
[Tags("AGONESPot - Dataroom")]
public class DataroomController : ControllerBase
{
    private readonly IDataroomService _dr;

    public DataroomController(IDataroomService dr) => _dr = dr;

    /// <summary>Company document status summary: total, pending, processed, approved, rejected.</summary>
    [HttpGet("dataroom/summary/{companyId}")]
    public async Task<IActionResult> GetSummary(string companyId, CancellationToken ct)
    {
        var r = await _dr.GetSummaryAsync(companyId, ct);
        return StatusCode(r.StatusCode, r);
    }

    /// <summary>Get all non-deleted files for a company.</summary>
    [HttpGet("company-files/{companyId}")]
    public async Task<IActionResult> GetCompanyFiles(string companyId, CancellationToken ct)
    {
        var r = await _dr.GetCompanyFilesAsync(companyId, ct);
        return StatusCode(r.StatusCode, r);
    }

    /// <summary>Soft-delete a file (marks IsDeleted=true).</summary>
    [HttpDelete("delete-file/company/{companyId}/user/{userId}/file/{fileId}")]
    public async Task<IActionResult> DeleteFile(
        string companyId, string userId, string fileId, CancellationToken ct)
    {
        var r = await _dr.DeleteFileAsync(companyId, userId, fileId, ct);
        return StatusCode(r.StatusCode, r);
    }

    /// <summary>Update document status (PENDING, PROCESSED, APPROVED, REJECTED).</summary>
    [HttpPost("dataroom/update-document-status/{companyId}")]
    public async Task<IActionResult> UpdateStatus(
        string companyId, [FromBody] UpdateStatusBody body, CancellationToken ct)
    {
        var r = await _dr.UpdateDocumentStatusAsync(companyId, body.Id, body.Status, ct);
        return StatusCode(r.StatusCode, r);
    }

    /// <summary>Extract company profile from SSM document using Azure Document Intelligence.</summary>
    [HttpGet("extract/company-profile")]
    public async Task<IActionResult> ExtractProfile(
        [FromQuery] string companyId, CancellationToken ct)
    {
        var r = await _dr.ExtractCompanyProfileAsync(companyId, ct);
        return StatusCode(r.StatusCode, r);
    }

    /// <summary>Get document counts for multiple companies.</summary>
    [HttpPost("dataroom/companies-status")]
    public async Task<IActionResult> GetCompaniesStatus(
        [FromBody] CompaniesStatusBody body, CancellationToken ct)
    {
        var r = await _dr.GetCompaniesStatusAsync(body.CompanyIds, ct);
        return StatusCode(r.StatusCode, r);
    }

    /// <summary>Validate that all core documents (SSM, CTOS, Audit) belong to the same company.</summary>
    [HttpGet("validate-company-documents")]
    public async Task<IActionResult> ValidateDocuments(
        [FromQuery] string companyId, CancellationToken ct)
    {
        var r = await _dr.ValidateCompanyDocumentsAsync(companyId, ct);
        return StatusCode(r.StatusCode, r);
    }
}

public class UpdateStatusBody
{
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class CompaniesStatusBody
{
    public List<string> CompanyIds { get; set; } = new();
}
