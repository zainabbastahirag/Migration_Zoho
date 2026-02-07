using Microsoft.AspNetCore.Mvc;
using OdooToZohoMigration.Core.DTOs.Migration;
using OdooToZohoMigration.Core.Interfaces;

namespace OdooToZohoMigration.API.Controllers;

/// <summary>
/// Single API controller for the entire Odoo-to-Zoho migration.
/// 
/// KEY ENDPOINTS:
/// - GET  /api/migration/sync-dashboard     -> See exactly what's synced across all entity types
/// - GET  /api/migration/sync-status/{type}  -> Drill into specific entities (paginated, filterable)
/// - POST /api/migration/start              -> Start migration (runs as background task, returns immediately)
/// - POST /api/migration/start/full         -> Start full migration of everything
/// - GET  /api/migration/status/{runId}     -> Check progress of a running migration
/// - POST /api/migration/cancel/{runId}     -> Cancel a running migration
/// - POST /api/migration/retry              -> Retry failed migrations
/// - POST /api/migration/reset-failed/{type} -> Reset error status so records can be retried
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class MigrationController : ControllerBase
{
    private readonly IMigrationService _migrationService;
    private readonly ILogger<MigrationController> _logger;

    public MigrationController(
        IMigrationService migrationService,
        ILogger<MigrationController> logger)
    {
        _migrationService = migrationService;
        _logger = logger;
    }

    // ====================================================================
    // SYNC TRACKING - Know exactly what's synced and what's not
    // ====================================================================

    /// <summary>
    /// Get comprehensive sync dashboard showing status of every entity type.
    /// This is the PRIMARY endpoint to understand your migration state.
    /// Shows: Jobs, Candidates, CVs, Applications, Comments, Summaries, History
    /// with counts for Total/Synced/Pending/Failed and percentage complete.
    /// </summary>
    [HttpGet("sync-dashboard")]
    [ProducesResponseType(typeof(SyncDashboardDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SyncDashboardDto>> GetSyncDashboard(CancellationToken ct)
    {
        var dashboard = await _migrationService.GetSyncDashboardAsync(ct);
        return Ok(dashboard);
    }

    /// <summary>
    /// Get detailed sync status for a specific entity type with pagination and filtering.
    /// 
    /// entityType: "jobs", "candidates", "applications", "cvs", "comments", "summaries", "history"
    /// filter: "all", "synced", "pending", "failed"
    /// search: optional text search by name/email
    /// errorFilter: optional filter by error message pattern
    /// 
    /// Example: GET /api/migration/sync-status/candidates?filter=failed&amp;page=1&amp;pageSize=50
    /// Example: GET /api/migration/sync-status/candidates?filter=pending&amp;search=john
    /// Example: GET /api/migration/sync-status/jobs?filter=failed&amp;errorFilter=Duplicate
    /// </summary>
    [HttpGet("sync-status/{entityType}")]
    [ProducesResponseType(typeof(PagedSyncResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedSyncResultDto>> GetSyncStatus(
        string entityType,
        [FromQuery] string filter = "all",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null,
        [FromQuery] string? errorFilter = null,
        CancellationToken ct = default)
    {
        var query = new SyncStatusQueryDto
        {
            Filter = filter,
            Page = Math.Max(1, page),
            PageSize = Math.Clamp(pageSize, 1, 200),
            Search = search,
            ErrorFilter = errorFilter
        };

        var result = await _migrationService.GetSyncStatusAsync(entityType, query, ct);
        return Ok(result);
    }

    /// <summary>
    /// Get overall migration summary showing total, migrated, pending, and failed counts
    /// for all entity types including CVs, comments, summaries, and history.
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(MigrationSummaryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<MigrationSummaryDto>> GetMigrationSummary(CancellationToken ct)
    {
        var summary = await _migrationService.GetMigrationSummaryAsync(ct);
        return Ok(summary);
    }

    // ====================================================================
    // MIGRATION OPERATIONS - Start, monitor, cancel migrations
    // ====================================================================

    /// <summary>
    /// Start a new migration with specified options.
    /// The migration runs as a background task - this endpoint returns immediately with a RunId.
    /// Use GET /api/migration/status/{runId} or GET /api/migration/sync-dashboard to monitor progress.
    /// </summary>
    [HttpPost("start")]
    [ProducesResponseType(typeof(MigrationResultDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(MigrationResultDto), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MigrationResultDto>> StartMigration(
        [FromBody] MigrationOptionsDto options,
        CancellationToken ct)
    {
        _logger.LogInformation("Migration requested: {@Options}", options);

        var result = await _migrationService.StartMigrationAsync(options, ct);

        if (!result.Success && result.Message.Contains("already in progress"))
            return Conflict(result);

        return result.Success ? Accepted(result) : Ok(result);
    }

    /// <summary>
    /// Start a FULL migration with default options (all entities, batch size 50).
    /// This is the "one button" endpoint to sync everything.
    /// </summary>
    [HttpPost("start/full")]
    [ProducesResponseType(typeof(MigrationResultDto), StatusCodes.Status202Accepted)]
    public async Task<ActionResult<MigrationResultDto>> StartFullMigration(
        [FromQuery] int batchSize = 50,
        CancellationToken ct = default)
    {
        var options = new MigrationOptionsDto
        {
            MigrateJobs = true,
            MigrateCandidates = true,
            MigrateApplications = true,
            MigrateCvs = true,
            MigrateComments = true,
            MigrateSummaries = true,
            MigrateHistory = true,
            RetryFailed = false,
            BatchSize = batchSize,
            ConcurrencyLevel = 3
        };

        return await StartMigration(options, ct);
    }

    /// <summary>
    /// Start migration for jobs only.
    /// </summary>
    [HttpPost("start/jobs")]
    [ProducesResponseType(typeof(MigrationResultDto), StatusCodes.Status202Accepted)]
    public async Task<ActionResult<MigrationResultDto>> StartJobsMigration(
        [FromQuery] int batchSize = 50,
        CancellationToken ct = default)
    {
        return await StartMigration(new MigrationOptionsDto
        {
            MigrateJobs = true, BatchSize = batchSize
        }, ct);
    }

    /// <summary>
    /// Start migration for candidates only (optionally including CV uploads).
    /// </summary>
    [HttpPost("start/candidates")]
    [ProducesResponseType(typeof(MigrationResultDto), StatusCodes.Status202Accepted)]
    public async Task<ActionResult<MigrationResultDto>> StartCandidatesMigration(
        [FromQuery] int batchSize = 50,
        [FromQuery] bool includeCvs = true,
        CancellationToken ct = default)
    {
        return await StartMigration(new MigrationOptionsDto
        {
            MigrateCandidates = true, MigrateCvs = includeCvs, BatchSize = batchSize
        }, ct);
    }

    /// <summary>
    /// Start CV upload only (candidates must already be migrated).
    /// </summary>
    [HttpPost("start/cvs")]
    [ProducesResponseType(typeof(MigrationResultDto), StatusCodes.Status202Accepted)]
    public async Task<ActionResult<MigrationResultDto>> StartCvsMigration(
        [FromQuery] int batchSize = 50,
        [FromQuery] int concurrency = 3,
        CancellationToken ct = default)
    {
        return await StartMigration(new MigrationOptionsDto
        {
            MigrateCvs = true, BatchSize = batchSize, ConcurrencyLevel = concurrency
        }, ct);
    }

    /// <summary>
    /// Start migration for applications only (requires jobs and candidates to be migrated first).
    /// </summary>
    [HttpPost("start/applications")]
    [ProducesResponseType(typeof(MigrationResultDto), StatusCodes.Status202Accepted)]
    public async Task<ActionResult<MigrationResultDto>> StartApplicationsMigration(
        [FromQuery] int batchSize = 50,
        [FromQuery] bool includeComments = true,
        [FromQuery] bool includeSummaries = true,
        [FromQuery] bool includeHistory = true,
        CancellationToken ct = default)
    {
        return await StartMigration(new MigrationOptionsDto
        {
            MigrateApplications = true,
            MigrateComments = includeComments,
            MigrateSummaries = includeSummaries,
            MigrateHistory = includeHistory,
            BatchSize = batchSize
        }, ct);
    }

    /// <summary>
    /// Start migration for comments only (requires applications to be migrated).
    /// </summary>
    [HttpPost("start/comments")]
    [ProducesResponseType(typeof(MigrationResultDto), StatusCodes.Status202Accepted)]
    public async Task<ActionResult<MigrationResultDto>> StartCommentsMigration(
        [FromQuery] int batchSize = 50,
        CancellationToken ct = default)
    {
        return await StartMigration(new MigrationOptionsDto
        {
            MigrateComments = true, BatchSize = batchSize
        }, ct);
    }

    /// <summary>
    /// Start migration for summaries only.
    /// </summary>
    [HttpPost("start/summaries")]
    [ProducesResponseType(typeof(MigrationResultDto), StatusCodes.Status202Accepted)]
    public async Task<ActionResult<MigrationResultDto>> StartSummariesMigration(
        [FromQuery] int batchSize = 50,
        CancellationToken ct = default)
    {
        return await StartMigration(new MigrationOptionsDto
        {
            MigrateSummaries = true, BatchSize = batchSize
        }, ct);
    }

    /// <summary>
    /// Start migration for history only.
    /// </summary>
    [HttpPost("start/history")]
    [ProducesResponseType(typeof(MigrationResultDto), StatusCodes.Status202Accepted)]
    public async Task<ActionResult<MigrationResultDto>> StartHistoryMigration(
        [FromQuery] int batchSize = 50,
        CancellationToken ct = default)
    {
        return await StartMigration(new MigrationOptionsDto
        {
            MigrateHistory = true, BatchSize = batchSize
        }, ct);
    }

    // ====================================================================
    // MONITORING
    // ====================================================================

    /// <summary>
    /// Get status of a specific migration run by ID.
    /// </summary>
    [HttpGet("status/{runId}")]
    [ProducesResponseType(typeof(MigrationStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MigrationStatusDto>> GetMigrationStatus(int runId, CancellationToken ct)
    {
        var status = await _migrationService.GetMigrationStatusAsync(runId, ct);
        if (status == null)
            return NotFound($"Migration run {runId} not found");
        return Ok(status);
    }

    /// <summary>
    /// Get all migration runs (last 50).
    /// </summary>
    [HttpGet("runs")]
    [ProducesResponseType(typeof(List<MigrationStatusDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<MigrationStatusDto>>> GetAllRuns(CancellationToken ct)
    {
        var runs = await _migrationService.GetAllRunsAsync(ct);
        return Ok(runs);
    }

    /// <summary>
    /// Cancel a running migration.
    /// </summary>
    [HttpPost("cancel/{runId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelMigration(int runId, CancellationToken ct)
    {
        _logger.LogWarning("Cancellation requested for migration run {RunId}", runId);
        await _migrationService.CancelMigrationAsync(runId, ct);
        return Ok(new { message = $"Cancellation requested for migration run {runId}" });
    }

    // ====================================================================
    // RETRY / RESET
    // ====================================================================

    /// <summary>
    /// Retry failed migrations for a specific entity type.
    /// EntityType: "All", "Job", "Candidate", "Application", "Cv", "Comment", "Summary", "History"
    /// </summary>
    [HttpPost("retry")]
    [ProducesResponseType(typeof(MigrationResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<MigrationResultDto>> RetryFailedMigrations(
        [FromBody] RetryMigrationDto retryOptions,
        CancellationToken ct)
    {
        _logger.LogInformation("Retrying failed migrations for {EntityType}", retryOptions.EntityType);
        var result = await _migrationService.RetryFailedMigrationsAsync(retryOptions, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retry ALL failed migrations across all entity types.
    /// </summary>
    [HttpPost("retry/all")]
    [ProducesResponseType(typeof(MigrationResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<MigrationResultDto>> RetryAllFailedMigrations(CancellationToken ct)
    {
        return await RetryFailedMigrations(new RetryMigrationDto { EntityType = "All" }, ct);
    }

    /// <summary>
    /// Reset migration error status for failed records so they appear as "Pending" again.
    /// This is useful when you want to retry records that failed with a specific error
    /// (e.g., after fixing a Zoho configuration issue).
    /// 
    /// entityType: "jobs", "candidates", "applications"
    /// </summary>
    [HttpPost("reset-failed/{entityType}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ResetFailedRecords(string entityType, CancellationToken ct)
    {
        var count = await _migrationService.ResetFailedRecordsAsync(entityType, ct);
        return Ok(new { message = $"Reset {count} failed {entityType} records. They will now appear as 'Pending'." });
    }
}
