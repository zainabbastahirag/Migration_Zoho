using AGONEAIHub.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AGONEAIHub.API.Controllers;

/// <summary>
/// ALL AGONESPot APIs in one controller.
/// Classification, dataroom, tags, company registration, report generation, statistics.
/// </summary>
[ApiController]
[Route("api/spot")]
[Tags("AGONESPot")]
public class SpotController : ControllerBase
{
    private readonly ISpotDataService _spot;

    public SpotController(ISpotDataService spot) => _spot = spot;

    // ── Classification ───────────────────────────────────────────────

    [HttpPost("classify")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Classify([FromForm] string companyId, [FromForm] string userId, IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0) return BadRequest(new SpotResult { StatusCode = 400, Message = "No file." });
        using var s = file.OpenReadStream();
        return Result(await _spot.ClassifyAsync(companyId, userId, file.FileName, s, file.Length, ct));
    }

    [HttpGet("classify/status")]
    public async Task<IActionResult> ClassifyStatus([FromQuery] string fileId, CancellationToken ct) =>
        Result(await _spot.GetClassifyStatusAsync(fileId, ct));

    [HttpPost("classify-worker")]
    public async Task<IActionResult> ClassifyWorker([FromForm] string jobId, [FromForm] int jobType, CancellationToken ct) =>
        Result(await _spot.ClassifyWorkerAsync(jobId, jobType, ct));

    // ── Dataroom ─────────────────────────────────────────────────────

    [HttpGet("dataroom/summary/{companyId}")]
    public async Task<IActionResult> GetSummary(string companyId, CancellationToken ct) =>
        Result(await _spot.GetSummaryAsync(companyId, ct));

    [HttpGet("company-files/{companyId}")]
    public async Task<IActionResult> GetCompanyFiles(string companyId, CancellationToken ct) =>
        Result(await _spot.GetCompanyFilesAsync(companyId, ct));

    [HttpDelete("delete-file/company/{companyId}/user/{userId}/file/{fileId}")]
    public async Task<IActionResult> DeleteFile(string companyId, string userId, string fileId, CancellationToken ct) =>
        Result(await _spot.DeleteFileAsync(companyId, userId, fileId, ct));

    [HttpPost("dataroom/update-document-status/{companyId}")]
    public async Task<IActionResult> UpdateStatus(string companyId, [FromBody] UpdateStatusReq body, CancellationToken ct) =>
        Result(await _spot.UpdateDocumentStatusAsync(companyId, body.Id, body.Status, ct));

    [HttpGet("extract/company-profile")]
    public async Task<IActionResult> ExtractProfile([FromQuery] string companyId, CancellationToken ct) =>
        Result(await _spot.ExtractCompanyProfileAsync(companyId, ct));

    [HttpPost("dataroom/companies-status")]
    public async Task<IActionResult> CompaniesStatus([FromBody] CompaniesStatusReq body, CancellationToken ct) =>
        Result(await _spot.GetCompaniesStatusAsync(body.CompanyIds, ct));

    [HttpGet("validate-company-documents")]
    public async Task<IActionResult> ValidateDocs([FromQuery] string companyId, CancellationToken ct) =>
        Result(await _spot.ValidateCompanyDocumentsAsync(companyId, ct));

    // ── Tags ─────────────────────────────────────────────────────────

    [HttpPost("document-tags")]
    public async Task<IActionResult> UpdateTags([FromBody] DocumentTagReq body, CancellationToken ct) =>
        Result(await _spot.UpdateDocumentTagsAsync(body.CompanyId, body.FileUID, body.Tags, ct));

    // ── Company Registration ─────────────────────────────────────────

    [HttpPost("company/register-company/{companyId}")]
    public async Task<IActionResult> RegisterCompany(string companyId, [FromBody] RegisterCompanyReq body, CancellationToken ct) =>
        Result(await _spot.RegisterCompanyAsync(companyId, body.CompanyName, body.RepresentativeName, body.PhoneNumber, body.CompanyAddress, body.Email, ct));

    // ── Report Generation ────────────────────────────────────────────

    [HttpPost("generate-report/start")]
    public async Task<IActionResult> StartReport([FromQuery] string companyId, [FromQuery] string type = "md", CancellationToken ct = default) =>
        Result(await _spot.StartReportGenerationAsync(companyId, type, ct));

    [HttpGet("generate-report/status")]
    public async Task<IActionResult> ReportStatus([FromQuery] string companyId, [FromQuery] string type = "md", CancellationToken ct = default) =>
        Result(await _spot.GetReportStatusAsync(companyId, type, ct));

    [HttpPost("generate-report-worker")]
    public async Task<IActionResult> ReportWorker([FromQuery] string jobId, CancellationToken ct) =>
        Result(await _spot.ReportWorkerAsync(jobId, ct));

    [HttpGet("spot-statistics")]
    public async Task<IActionResult> Statistics(CancellationToken ct) =>
        Result(await _spot.GetSpotStatisticsAsync(ct));

    [HttpGet("spot-history")]
    public async Task<IActionResult> History([FromQuery] string companyId, CancellationToken ct) =>
        Result(await _spot.GetSpotHistoryAsync(companyId, ct));

    [HttpDelete("delete-report/company/{companyId}/user/{userId}/report/{reportId}")]
    public async Task<IActionResult> DeleteReport(string companyId, string userId, string reportId, CancellationToken ct) =>
        Result(await _spot.DeleteReportAsync(companyId, userId, reportId, ct));

    [HttpGet("recent-generated-reports")]
    public async Task<IActionResult> RecentReports(CancellationToken ct) =>
        Result(await _spot.GetRecentReportsAsync(ct));

    // ── Helper ───────────────────────────────────────────────────────

    private IActionResult Result(SpotResult r) => StatusCode(r.StatusCode, r);
}

// ── Request models ───────────────────────────────────────────────────

public class UpdateStatusReq { public string Id { get; set; } = ""; public string Status { get; set; } = ""; }
public class CompaniesStatusReq { public List<string> CompanyIds { get; set; } = new(); }
public class DocumentTagReq { public string CompanyId { get; set; } = ""; public string FileUID { get; set; } = ""; public List<string> Tags { get; set; } = new(); }
public class RegisterCompanyReq { public string CompanyName { get; set; } = ""; public string? RepresentativeName { get; set; } public string? PhoneNumber { get; set; } public string? CompanyAddress { get; set; } public string? Email { get; set; } }
