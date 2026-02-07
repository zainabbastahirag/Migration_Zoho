using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OdooToZohoMigration.Core.DTOs.Migration;
using OdooToZohoMigration.Core.Entities;
using OdooToZohoMigration.Core.Interfaces;
using OdooToZohoMigration.Infrastructure.Configuration;
using OdooToZohoMigration.Infrastructure.Data;

namespace OdooToZohoMigration.Infrastructure.Services;

/// <summary>
/// Fully rewritten MigrationService with:
/// - Cursor-based pagination (OrderBy Id, track lastProcessedId) for reliable resume
/// - Index-based mapping of Zoho batch responses (no extra GET calls)
/// - Concurrent CV uploads via SemaphoreSlim
/// - Batch notes creation for comments/summaries/history
/// - Per-entity MigrationLog entries for every operation
/// - Incremental progress updates to MigrationRun after each batch
/// - Comprehensive sync dashboard and entity-level status queries
/// - Proper duplicate handling for candidates
/// </summary>
public class MigrationService : IMigrationService
{
    private readonly MigrationDbContext _dbContext;
    private readonly IZohoRecruitService _zohoService;
    private readonly IBlobStorageService _blobService;
    private readonly MigrationSettings _settings;
    private readonly ILogger<MigrationService> _logger;

    private static readonly SemaphoreSlim _migrationLock = new(1, 1);
    private static CancellationTokenSource? _currentMigrationCts;

    public MigrationService(
        MigrationDbContext dbContext,
        IZohoRecruitService zohoService,
        IBlobStorageService blobService,
        IOptions<MigrationSettings> settings,
        ILogger<MigrationService> logger)
    {
        _dbContext = dbContext;
        _zohoService = zohoService;
        _blobService = blobService;
        _settings = settings.Value;
        _logger = logger;
    }

    // ========================================================================
    // MIGRATION ORCHESTRATION
    // ========================================================================

    public async Task<MigrationResultDto> StartMigrationAsync(
        MigrationOptionsDto options,
        CancellationToken cancellationToken = default)
    {
        if (!await _migrationLock.WaitAsync(0, cancellationToken))
        {
            return new MigrationResultDto
            {
                Success = false,
                Message = "A migration is already in progress. Check GET /api/migration/sync-dashboard for current status."
            };
        }

        try
        {
            _currentMigrationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var ct = _currentMigrationCts.Token;

            var batchSize = options.BatchSize > 0 ? options.BatchSize : _settings.DefaultBatchSize;
            var concurrency = options.ConcurrencyLevel > 0 ? options.ConcurrencyLevel : _settings.MaxConcurrentCvUploads;

            // Create migration run record
            var run = new MigrationRun
            {
                RunType = options.RetryFailed ? "Retry" : "Full",
                StartedAt = DateTime.UtcNow,
                Status = "Running"
            };

            // Count totals for each enabled phase
            if (options.MigrateJobs)
                run.TotalJobs = await CountPendingAsync<Job>(options.RetryFailed, ct);
            if (options.MigrateCandidates)
                run.TotalCandidates = await CountPendingCandidatesAsync(options.RetryFailed, ct);
            if (options.MigrateApplications)
                run.TotalApplications = await CountPendingApplicationsAsync(options.RetryFailed, ct);

            _dbContext.MigrationRuns.Add(run);
            await _dbContext.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Migration run {RunId} started. Type={RunType}, Jobs={Jobs}, Candidates={Candidates}, Apps={Apps}",
                run.Id, run.RunType, run.TotalJobs, run.TotalCandidates, run.TotalApplications);

            var summary = new MigrationRunSummaryDto();
            var startTime = DateTime.UtcNow;

            // Phase 1: Jobs
            if (options.MigrateJobs && run.TotalJobs > 0)
            {
                _logger.LogInformation("=== Phase 1: Migrating {Count} jobs ===", run.TotalJobs);
                var (migrated, failed) = await MigrateJobsBulkAsync(run, batchSize, options.RetryFailed, ct);
                summary.JobsMigrated = migrated;
                summary.JobsFailed = failed;
            }

            // Phase 2: Candidates
            if (options.MigrateCandidates && run.TotalCandidates > 0)
            {
                _logger.LogInformation("=== Phase 2: Migrating {Count} candidates ===", run.TotalCandidates);
                var (migrated, failed) = await MigrateCandidatesBulkAsync(run, batchSize, options.RetryFailed, ct);
                summary.CandidatesMigrated = migrated;
                summary.CandidatesFailed = failed;
            }

            // Phase 3: CVs
            if (options.MigrateCvs)
            {
                _logger.LogInformation("=== Phase 3: Uploading CVs (concurrency={Concurrency}) ===", concurrency);
                var (uploaded, failed) = await MigrateCvsConcurrentAsync(run, batchSize, concurrency, ct);
                summary.CvsUploaded = uploaded;
                summary.CvsFailed = failed;
            }

            // Phase 4: Applications
            if (options.MigrateApplications && run.TotalApplications > 0)
            {
                _logger.LogInformation("=== Phase 4: Migrating {Count} applications ===", run.TotalApplications);
                var (migrated, failed) = await MigrateApplicationsBulkAsync(run, batchSize, options.RetryFailed, ct);
                summary.ApplicationsMigrated = migrated;
                summary.ApplicationsFailed = failed;
            }

            // Phase 5: Comments (batch notes)
            if (options.MigrateComments)
            {
                _logger.LogInformation("=== Phase 5: Migrating comments ===");
                var (migrated, failed) = await MigrateCommentsBatchAsync(run, batchSize, ct);
                summary.CommentsMigrated = migrated;
                summary.CommentsFailed = failed;
            }

            // Phase 6: Summaries (batch notes)
            if (options.MigrateSummaries)
            {
                _logger.LogInformation("=== Phase 6: Migrating summaries ===");
                var (migrated, failed) = await MigrateSummariesBatchAsync(run, batchSize, ct);
                summary.SummariesMigrated = migrated;
                summary.SummariesFailed = failed;
            }

            // Phase 7: History (batch notes)
            if (options.MigrateHistory)
            {
                _logger.LogInformation("=== Phase 7: Migrating history ===");
                var (migrated, failed) = await MigrateHistoryBatchAsync(run, batchSize, ct);
                summary.HistoryMigrated = migrated;
                summary.HistoryFailed = failed;
            }

            // Finalize
            run.Status = "Completed";
            run.CompletedAt = DateTime.UtcNow;
            summary.Duration = DateTime.UtcNow - startTime;
            await _dbContext.SaveChangesAsync(ct);

            _logger.LogInformation("Migration run {RunId} completed in {Duration}", run.Id, summary.Duration);

            return new MigrationResultDto
            {
                Success = true,
                RunId = run.Id,
                Message = $"Migration completed in {summary.Duration:hh\\:mm\\:ss}. " +
                          $"Jobs: {summary.JobsMigrated}/{run.TotalJobs}, " +
                          $"Candidates: {summary.CandidatesMigrated}/{run.TotalCandidates}, " +
                          $"Apps: {summary.ApplicationsMigrated}/{run.TotalApplications}, " +
                          $"CVs: {summary.CvsUploaded}, Comments: {summary.CommentsMigrated}",
                Summary = summary
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Migration was cancelled");
            return new MigrationResultDto { Success = false, Message = "Migration was cancelled by user" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Migration failed with unhandled exception");
            return new MigrationResultDto { Success = false, Message = $"Migration failed: {ex.Message}" };
        }
        finally
        {
            _migrationLock.Release();
            _currentMigrationCts = null;
        }
    }

    // ========================================================================
    // PHASE 1: JOBS - Bulk batch with cursor-based pagination
    // ========================================================================

    private async Task<(int Migrated, int Failed)> MigrateJobsBulkAsync(
        MigrationRun run, int batchSize, bool retryFailed, CancellationToken ct)
    {
        int migrated = 0, failed = 0, lastProcessedId = 0;

        while (true)
        {
            var query = _dbContext.Jobs
                .Where(j => !j.IsMigrated && j.Id > lastProcessedId)
                .OrderBy(j => j.Id);

            if (!retryFailed)
                query = (IOrderedQueryable<Job>)query.Where(j => string.IsNullOrEmpty(j.MigrationError));

            var jobs = await query.Take(batchSize).ToListAsync(ct);
            if (!jobs.Any()) break;

            // Send full batch to Zoho (up to 100 per API call)
            var results = await _zohoService.CreateJobOpeningsBatchAsync(jobs, ct);

            // Map results by index
            for (int i = 0; i < jobs.Count && i < results.Count; i++)
            {
                var job = jobs[i];
                var result = results[i];

                job.IsMigrated = result.Success;
                job.ZohoJobId = result.ZohoId;
                job.MigratedAt = result.Success ? DateTime.UtcNow : null;
                job.MigrationError = result.ErrorMessage;

                WriteMigrationLog("Job", job.Id, job.OdooId, result);

                if (result.Success) migrated++;
                else failed++;
            }

            // Update run counts incrementally
            run.MigratedJobs = migrated;
            run.FailedJobs = failed;
            await _dbContext.SaveChangesAsync(ct);

            lastProcessedId = jobs.Max(j => j.Id);

            _logger.LogInformation("Jobs batch done. Migrated={Migrated}, Failed={Failed}, LastId={LastId}",
                migrated, failed, lastProcessedId);

            await Task.Delay(_settings.DelayBetweenBatchesMs, ct);
        }

        return (migrated, failed);
    }

    // ========================================================================
    // PHASE 2: CANDIDATES - Bulk batch with index-based mapping, duplicate handling
    // ========================================================================

    private async Task<(int Migrated, int Failed)> MigrateCandidatesBulkAsync(
        MigrationRun run, int batchSize, bool retryFailed, CancellationToken ct)
    {
        int migrated = 0, failed = 0, lastProcessedId = 0;

        while (true)
        {
            // Fetch next batch using cursor-based pagination
            var queryBase = _dbContext.Candidates
                .Where(c => !c.IsMigrated && !string.IsNullOrEmpty(c.Email) && c.Id > lastProcessedId)
                .OrderBy(c => c.Id);

            var candidates = await queryBase.Take(batchSize).ToListAsync(ct);
            if (!candidates.Any()) break;

            // Deduplicate by email within this batch (pick first occurrence)
            var uniqueByEmail = candidates
                .GroupBy(c => c.Email!.ToLowerInvariant())
                .Select(g => g.First())
                .ToList();

            // Send batch to Zoho - response is in SAME ORDER as input (index-based mapping)
            var results = await _zohoService.CreateCandidatesBatchAsync(uniqueByEmail, ct);

            // Process results by index
            for (int i = 0; i < uniqueByEmail.Count && i < results.Count; i++)
            {
                var candidate = uniqueByEmail[i];
                var result = results[i];

                bool isDuplicate = result.ErrorMessage?.Contains("Duplicate", StringComparison.OrdinalIgnoreCase) == true;

                if (isDuplicate)
                {
                    // Mark ALL candidates with this email as migrated (handles duplicates in DB)
                    var allWithEmail = await _dbContext.Candidates
                        .Where(c => c.Email != null && c.Email.ToLower() == candidate.Email!.ToLower())
                        .ToListAsync(ct);

                    foreach (var dup in allWithEmail)
                    {
                        dup.IsMigrated = true;
                        dup.MigratedAt = DateTime.UtcNow;
                        dup.ZohoCandidateId = result.ZohoId;
                        dup.MigrationError = null; // Clear any previous error - it's actually synced
                    }
                    migrated += allWithEmail.Count;

                    _logger.LogInformation("Candidate {Email} already exists in Zoho (duplicate). Marked {Count} DB records as synced.",
                        candidate.Email, allWithEmail.Count);
                }
                else if (result.Success)
                {
                    candidate.IsMigrated = true;
                    candidate.ZohoCandidateId = result.ZohoId;
                    candidate.MigratedAt = DateTime.UtcNow;
                    candidate.MigrationError = null;
                    migrated++;
                }
                else
                {
                    candidate.IsMigrated = false;
                    candidate.MigrationError = result.ErrorMessage;
                    failed++;
                }

                WriteMigrationLog("Candidate", candidate.Id, candidate.OdooId, result);
            }

            // Update run counts incrementally
            run.MigratedCandidates = migrated;
            run.FailedCandidates = failed;
            await _dbContext.SaveChangesAsync(ct);

            lastProcessedId = candidates.Max(c => c.Id);

            _logger.LogInformation("Candidates batch done. Migrated={Migrated}, Failed={Failed}, LastId={LastId}",
                migrated, failed, lastProcessedId);

            await Task.Delay(_settings.DelayBetweenBatchesMs, ct);
        }

        return (migrated, failed);
    }

    // ========================================================================
    // PHASE 3: CVs - Concurrent uploads with SemaphoreSlim
    // ========================================================================

    private async Task<(int Uploaded, int Failed)> MigrateCvsConcurrentAsync(
        MigrationRun run, int batchSize, int concurrency, CancellationToken ct)
    {
        int uploaded = 0, failed = 0, lastProcessedId = 0;
        var semaphore = new SemaphoreSlim(concurrency);

        while (true)
        {
            var candidates = await _dbContext.Candidates
                .Where(c => c.IsMigrated
                    && !c.IsCvMigrated
                    && !string.IsNullOrEmpty(c.ResumeUrl)
                    && !string.IsNullOrEmpty(c.ZohoCandidateId)
                    && c.Id > lastProcessedId)
                .OrderBy(c => c.Id)
                .Take(batchSize)
                .ToListAsync(ct);

            if (!candidates.Any()) break;

            // Process CV uploads concurrently within the batch
            var tasks = candidates.Select(async candidate =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    var success = await UploadSingleCvAsync(candidate, ct);
                    if (success) Interlocked.Increment(ref uploaded);
                    else Interlocked.Increment(ref failed);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            // Update run counts
            run.TotalCvsUploaded = uploaded;
            run.FailedCvUploads = failed;
            await _dbContext.SaveChangesAsync(ct);

            lastProcessedId = candidates.Max(c => c.Id);

            _logger.LogInformation("CVs batch done. Uploaded={Uploaded}, Failed={Failed}, LastId={LastId}",
                uploaded, failed, lastProcessedId);

            await Task.Delay(_settings.DelayBetweenBatchesMs, ct);
        }

        return (uploaded, failed);
    }

    private async Task<bool> UploadSingleCvAsync(Candidate candidate, CancellationToken ct)
    {
        try
        {
            var (stream, contentType, fileName) = await _blobService.GetCvWithMetadataAsync(candidate.ResumeUrl!, ct);

            if (stream == null)
            {
                _logger.LogWarning("CV not found for candidate {Id}: {Url}", candidate.Id, candidate.ResumeUrl);
                return false;
            }

            using (stream)
            {
                var cvFileName = fileName ?? GetFileNameFromUrl(candidate.ResumeUrl!) ?? $"resume_{candidate.OdooId}.pdf";
                var cvContentType = contentType ?? "application/pdf";

                var result = await _zohoService.UploadCandidateCvAsync(
                    candidate.ZohoCandidateId!, stream, cvFileName, cvContentType, ct);

                candidate.IsCvMigrated = result.Success;
                candidate.CvMigratedAt = result.Success ? DateTime.UtcNow : null;

                _logger.LogInformation("CV upload for candidate {Id}: {Status}",
                    candidate.Id, result.Success ? "OK" : result.ErrorMessage);

                return result.Success;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading CV for candidate {Id}", candidate.Id);
            return false;
        }
    }

    // ========================================================================
    // PHASE 4: APPLICATIONS - One-by-one (Zoho associate API limitation)
    // ========================================================================

    private async Task<(int Migrated, int Failed)> MigrateApplicationsBulkAsync(
        MigrationRun run, int batchSize, bool retryFailed, CancellationToken ct)
    {
        int migrated = 0, failed = 0, lastProcessedId = 0;

        while (true)
        {
            var query = _dbContext.Applications
                .Include(a => a.Candidate)
                .Include(a => a.Job)
                .Where(a => !a.IsMigrated
                    && a.Id > lastProcessedId
                    && a.Candidate.IsMigrated
                    && !string.IsNullOrEmpty(a.Candidate.ZohoCandidateId)
                    && a.Job.IsMigrated
                    && !string.IsNullOrEmpty(a.Job.ZohoJobId))
                .OrderBy(a => a.Id);

            var apps = retryFailed
                ? await query.Take(batchSize).ToListAsync(ct)
                : await query.Where(a => string.IsNullOrEmpty(a.MigrationError)).Take(batchSize).ToListAsync(ct);

            if (!apps.Any()) break;

            foreach (var app in apps)
            {
                var comments = BuildApplicationComments(app);

                var result = await _zohoService.AssociateCandidateWithJobAsync(
                    app.Candidate.ZohoCandidateId!, app.Job.ZohoJobId!, comments, ct);

                app.IsMigrated = result.Success;
                app.ZohoApplicationId = result.ZohoId;
                app.MigratedAt = result.Success ? DateTime.UtcNow : null;
                app.MigrationError = result.ErrorMessage;

                WriteMigrationLog("Application", app.Id, app.OdooId, result);

                if (result.Success) migrated++;
                else failed++;

                // Zoho associate API: throttle 1 request per 2 seconds
                await Task.Delay(2000, ct);
            }

            run.MigratedApplications = migrated;
            run.FailedApplications = failed;
            await _dbContext.SaveChangesAsync(ct);

            lastProcessedId = apps.Max(a => a.Id);

            _logger.LogInformation("Applications batch done. Migrated={Migrated}, Failed={Failed}, LastId={LastId}",
                migrated, failed, lastProcessedId);

            await Task.Delay(_settings.DelayBetweenBatchesMs, ct);
        }

        return (migrated, failed);
    }

    // ========================================================================
    // PHASE 5: COMMENTS - Batch notes creation
    // ========================================================================

    private async Task<(int Migrated, int Failed)> MigrateCommentsBatchAsync(
        MigrationRun run, int batchSize, CancellationToken ct)
    {
        int migrated = 0, failed = 0, lastProcessedId = 0;

        while (true)
        {
            var comments = await _dbContext.ApplicationComments
                .Include(c => c.Application)
                    .ThenInclude(a => a.Candidate)
                .Where(c => !c.IsMigrated
                    && c.Id > lastProcessedId
                    && c.Application.IsMigrated
                    && !string.IsNullOrEmpty(c.Application.Candidate.ZohoCandidateId))
                .OrderBy(c => c.Id)
                .Take(batchSize)
                .ToListAsync(ct);

            if (!comments.Any()) break;

            // Build batch notes payload
            var notePayloads = comments.Select(c => (
                ParentId: c.Application.Candidate.ZohoCandidateId!,
                Title: $"Comment - {c.CreatedAt:yyyy-MM-dd}",
                Content: $"Author: {c.Author}\nDate: {c.CreatedAt:yyyy-MM-dd HH:mm}\n\n{c.Body}"
            )).ToList();

            var results = await _zohoService.CreateNotesBatchAsync("Candidates", notePayloads, ct);

            for (int i = 0; i < comments.Count && i < results.Count; i++)
            {
                var comment = comments[i];
                var result = results[i];

                comment.IsMigrated = result.Success;
                comment.ZohoNoteId = result.ZohoId;
                comment.MigratedAt = result.Success ? DateTime.UtcNow : null;

                if (result.Success) migrated++;
                else failed++;
            }

            await _dbContext.SaveChangesAsync(ct);
            lastProcessedId = comments.Max(c => c.Id);

            _logger.LogInformation("Comments batch done. Migrated={Migrated}, Failed={Failed}, LastId={LastId}",
                migrated, failed, lastProcessedId);

            await Task.Delay(_settings.DelayBetweenBatchesMs, ct);
        }

        return (migrated, failed);
    }

    // ========================================================================
    // PHASE 6: SUMMARIES - Batch notes creation
    // ========================================================================

    private async Task<(int Migrated, int Failed)> MigrateSummariesBatchAsync(
        MigrationRun run, int batchSize, CancellationToken ct)
    {
        int migrated = 0, failed = 0, lastProcessedId = 0;

        while (true)
        {
            var summaries = await _dbContext.ApplicationSummaries
                .Include(s => s.Application)
                    .ThenInclude(a => a!.Candidate)
                .Where(s => !s.IsMigrated
                    && s.Id > lastProcessedId
                    && s.Application != null
                    && s.Application.IsMigrated
                    && !string.IsNullOrEmpty(s.Application.Candidate.ZohoCandidateId))
                .OrderBy(s => s.Id)
                .Take(batchSize)
                .ToListAsync(ct);

            if (!summaries.Any()) break;

            var notePayloads = summaries.Select(s => (
                ParentId: s.Application!.Candidate.ZohoCandidateId!,
                Title: "Application Summary",
                Content: $"=== Application Summary ===\n\n" +
                    $"Expected Salary: {s.ExpectedSalary:N2}\n" +
                    $"Proposed Salary: {s.ProposedSalary:N2}\n" +
                    $"Availability Date: {s.AvailabilityDate:yyyy-MM-dd}\n" +
                    $"Priority: {s.Priority}\n" +
                    $"Source: {s.Source}\n" +
                    $"Degree: {s.Degree}\n" +
                    $"Recruiter: {s.Recruiter}"
            )).ToList();

            var results = await _zohoService.CreateNotesBatchAsync("Candidates", notePayloads, ct);

            for (int i = 0; i < summaries.Count && i < results.Count; i++)
            {
                var summary = summaries[i];
                var result = results[i];

                summary.IsMigrated = result.Success;
                summary.ZohoNoteId = result.ZohoId;
                summary.MigratedAt = result.Success ? DateTime.UtcNow : null;

                if (result.Success) migrated++;
                else failed++;
            }

            await _dbContext.SaveChangesAsync(ct);
            lastProcessedId = summaries.Max(s => s.Id);

            _logger.LogInformation("Summaries batch done. Migrated={Migrated}, Failed={Failed}, LastId={LastId}",
                migrated, failed, lastProcessedId);

            await Task.Delay(_settings.DelayBetweenBatchesMs, ct);
        }

        return (migrated, failed);
    }

    // ========================================================================
    // PHASE 7: HISTORY - Batch notes creation
    // ========================================================================

    private async Task<(int Migrated, int Failed)> MigrateHistoryBatchAsync(
        MigrationRun run, int batchSize, CancellationToken ct)
    {
        int migrated = 0, failed = 0, lastProcessedId = 0;

        while (true)
        {
            var history = await _dbContext.ApplicationHistories
                .Include(h => h.Application)
                    .ThenInclude(a => a.Candidate)
                .Where(h => !h.IsMigrated
                    && h.Id > lastProcessedId
                    && h.Application.IsMigrated
                    && !string.IsNullOrEmpty(h.Application.Candidate.ZohoCandidateId))
                .OrderBy(h => h.Id)
                .Take(batchSize)
                .ToListAsync(ct);

            if (!history.Any()) break;

            var notePayloads = history.Select(h => (
                ParentId: h.Application.Candidate.ZohoCandidateId!,
                Title: $"History - {h.ChangeDate:yyyy-MM-dd}",
                Content: $"Stage Change: {h.ChangeDate:yyyy-MM-dd HH:mm}\n\n{h.Description}"
            )).ToList();

            var results = await _zohoService.CreateNotesBatchAsync("Candidates", notePayloads, ct);

            for (int i = 0; i < history.Count && i < results.Count; i++)
            {
                var item = history[i];
                var result = results[i];

                item.IsMigrated = result.Success;
                item.ZohoNoteId = result.ZohoId;
                item.MigratedAt = result.Success ? DateTime.UtcNow : null;

                if (result.Success) migrated++;
                else failed++;
            }

            await _dbContext.SaveChangesAsync(ct);
            lastProcessedId = history.Max(h => h.Id);

            _logger.LogInformation("History batch done. Migrated={Migrated}, Failed={Failed}, LastId={LastId}",
                migrated, failed, lastProcessedId);

            await Task.Delay(_settings.DelayBetweenBatchesMs, ct);
        }

        return (migrated, failed);
    }

    // ========================================================================
    // SYNC DASHBOARD - Comprehensive view of all entity sync states
    // ========================================================================

    public async Task<SyncDashboardDto> GetSyncDashboardAsync(CancellationToken ct = default)
    {
        var dashboard = new SyncDashboardDto();

        // Jobs
        var totalJobs = await _dbContext.Jobs.CountAsync(ct);
        var syncedJobs = await _dbContext.Jobs.CountAsync(j => j.IsMigrated, ct);
        var failedJobs = await _dbContext.Jobs.CountAsync(j => !j.IsMigrated && !string.IsNullOrEmpty(j.MigrationError), ct);
        var lastJobSync = await _dbContext.Jobs.Where(j => j.MigratedAt != null).MaxAsync(j => (DateTime?)j.MigratedAt, ct);
        dashboard.Jobs = new EntitySyncOverview
        {
            EntityType = "Jobs", Total = totalJobs, Synced = syncedJobs,
            Pending = totalJobs - syncedJobs - failedJobs, Failed = failedJobs, LastSyncedAt = lastJobSync
        };

        // Candidates
        var totalCandidates = await _dbContext.Candidates.CountAsync(ct);
        var syncedCandidates = await _dbContext.Candidates.CountAsync(c => c.IsMigrated, ct);
        var failedCandidates = await _dbContext.Candidates.CountAsync(c => !c.IsMigrated && !string.IsNullOrEmpty(c.MigrationError), ct);
        var lastCandidateSync = await _dbContext.Candidates.Where(c => c.MigratedAt != null).MaxAsync(c => (DateTime?)c.MigratedAt, ct);
        dashboard.Candidates = new EntitySyncOverview
        {
            EntityType = "Candidates", Total = totalCandidates, Synced = syncedCandidates,
            Pending = totalCandidates - syncedCandidates - failedCandidates, Failed = failedCandidates, LastSyncedAt = lastCandidateSync
        };

        // CVs
        var totalWithCv = await _dbContext.Candidates.CountAsync(c => c.IsMigrated && !string.IsNullOrEmpty(c.ResumeUrl), ct);
        var cvUploaded = await _dbContext.Candidates.CountAsync(c => c.IsCvMigrated, ct);
        var pendingCvUploads = await _dbContext.Candidates.CountAsync(c => c.IsMigrated && !c.IsCvMigrated && !string.IsNullOrEmpty(c.ResumeUrl) && !string.IsNullOrEmpty(c.ZohoCandidateId), ct);
        var noCvAvailable = await _dbContext.Candidates.CountAsync(c => c.IsMigrated && string.IsNullOrEmpty(c.ResumeUrl), ct);
        var lastCvSync = await _dbContext.Candidates.Where(c => c.CvMigratedAt != null).MaxAsync(c => (DateTime?)c.CvMigratedAt, ct);
        dashboard.CandidateCvs = new CandidateCvSyncOverview
        {
            EntityType = "CVs", Total = totalWithCv, Synced = cvUploaded,
            Pending = pendingCvUploads, Failed = totalWithCv - cvUploaded - pendingCvUploads,
            PendingCvUploads = pendingCvUploads, NoCvAvailable = noCvAvailable, LastSyncedAt = lastCvSync
        };

        // Applications
        var totalApps = await _dbContext.Applications.CountAsync(ct);
        var syncedApps = await _dbContext.Applications.CountAsync(a => a.IsMigrated, ct);
        var failedApps = await _dbContext.Applications.CountAsync(a => !a.IsMigrated && !string.IsNullOrEmpty(a.MigrationError), ct);
        var lastAppSync = await _dbContext.Applications.Where(a => a.MigratedAt != null).MaxAsync(a => (DateTime?)a.MigratedAt, ct);
        dashboard.Applications = new EntitySyncOverview
        {
            EntityType = "Applications", Total = totalApps, Synced = syncedApps,
            Pending = totalApps - syncedApps - failedApps, Failed = failedApps, LastSyncedAt = lastAppSync
        };

        // Comments
        var totalComments = await _dbContext.ApplicationComments.CountAsync(ct);
        var syncedComments = await _dbContext.ApplicationComments.CountAsync(c => c.IsMigrated, ct);
        var lastCommentSync = await _dbContext.ApplicationComments.Where(c => c.MigratedAt != null).MaxAsync(c => (DateTime?)c.MigratedAt, ct);
        dashboard.Comments = new EntitySyncOverview
        {
            EntityType = "Comments", Total = totalComments, Synced = syncedComments,
            Pending = totalComments - syncedComments, Failed = 0, LastSyncedAt = lastCommentSync
        };

        // Summaries
        var totalSummaries = await _dbContext.ApplicationSummaries.CountAsync(ct);
        var syncedSummaries = await _dbContext.ApplicationSummaries.CountAsync(s => s.IsMigrated, ct);
        var lastSummarySync = await _dbContext.ApplicationSummaries.Where(s => s.MigratedAt != null).MaxAsync(s => (DateTime?)s.MigratedAt, ct);
        dashboard.Summaries = new EntitySyncOverview
        {
            EntityType = "Summaries", Total = totalSummaries, Synced = syncedSummaries,
            Pending = totalSummaries - syncedSummaries, Failed = 0, LastSyncedAt = lastSummarySync
        };

        // History
        var totalHistory = await _dbContext.ApplicationHistories.CountAsync(ct);
        var syncedHistory = await _dbContext.ApplicationHistories.CountAsync(h => h.IsMigrated, ct);
        var lastHistorySync = await _dbContext.ApplicationHistories.Where(h => h.MigratedAt != null).MaxAsync(h => (DateTime?)h.MigratedAt, ct);
        dashboard.History = new EntitySyncOverview
        {
            EntityType = "History", Total = totalHistory, Synced = syncedHistory,
            Pending = totalHistory - syncedHistory, Failed = 0, LastSyncedAt = lastHistorySync
        };

        // Totals
        dashboard.TotalRecords = totalJobs + totalCandidates + totalApps + totalComments + totalSummaries + totalHistory;
        dashboard.TotalSynced = syncedJobs + syncedCandidates + syncedApps + syncedComments + syncedSummaries + syncedHistory;
        dashboard.TotalPending = dashboard.TotalRecords - dashboard.TotalSynced - (failedJobs + failedCandidates + failedApps);
        dashboard.TotalFailed = failedJobs + failedCandidates + failedApps;

        // Last run info
        var lastRun = await _dbContext.MigrationRuns
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (lastRun != null)
        {
            dashboard.LastRun = new LastRunInfo
            {
                RunId = lastRun.Id,
                Status = lastRun.Status,
                StartedAt = lastRun.StartedAt,
                CompletedAt = lastRun.CompletedAt,
                RunType = lastRun.RunType
            };
        }

        return dashboard;
    }

    // ========================================================================
    // SYNC STATUS - Entity-level detail with pagination and filtering
    // ========================================================================

    public async Task<PagedSyncResultDto> GetSyncStatusAsync(
        string entityType, SyncStatusQueryDto query, CancellationToken ct = default)
    {
        return entityType.ToLowerInvariant() switch
        {
            "jobs" => await GetJobsSyncStatusAsync(query, ct),
            "candidates" => await GetCandidatesSyncStatusAsync(query, ct),
            "applications" => await GetApplicationsSyncStatusAsync(query, ct),
            "cvs" => await GetCvsSyncStatusAsync(query, ct),
            "comments" => await GetCommentsSyncStatusAsync(query, ct),
            "summaries" => await GetSummariesSyncStatusAsync(query, ct),
            "history" => await GetHistorySyncStatusAsync(query, ct),
            _ => new PagedSyncResultDto { EntityType = entityType, Items = new(), TotalCount = 0 }
        };
    }

    private async Task<PagedSyncResultDto> GetJobsSyncStatusAsync(SyncStatusQueryDto query, CancellationToken ct)
    {
        var q = _dbContext.Jobs.AsQueryable();
        q = ApplySyncFilter(q, query.Filter, j => j.IsMigrated, j => j.MigrationError);

        if (!string.IsNullOrWhiteSpace(query.Search))
            q = q.Where(j => j.Title.Contains(query.Search));

        if (!string.IsNullOrWhiteSpace(query.ErrorFilter))
            q = q.Where(j => j.MigrationError != null && j.MigrationError.Contains(query.ErrorFilter));

        var totalCount = await q.CountAsync(ct);
        var items = await q.OrderBy(j => j.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(j => new SyncEntityDetailDto
            {
                Id = j.Id,
                OdooId = j.OdooId,
                Name = j.Title,
                SyncStatus = j.IsMigrated ? "Synced" : (!string.IsNullOrEmpty(j.MigrationError) ? "Failed" : "Pending"),
                ZohoId = j.ZohoJobId,
                SyncedAt = j.MigratedAt,
                ErrorMessage = j.MigrationError,
                AdditionalInfo = j.Department
            })
            .ToListAsync(ct);

        return new PagedSyncResultDto
        {
            EntityType = "Jobs", Items = items, TotalCount = totalCount,
            Page = query.Page, PageSize = query.PageSize, Filter = query.Filter
        };
    }

    private async Task<PagedSyncResultDto> GetCandidatesSyncStatusAsync(SyncStatusQueryDto query, CancellationToken ct)
    {
        var q = _dbContext.Candidates.AsQueryable();
        q = ApplySyncFilter(q, query.Filter, c => c.IsMigrated, c => c.MigrationError);

        if (!string.IsNullOrWhiteSpace(query.Search))
            q = q.Where(c => c.Name.Contains(query.Search) || (c.Email != null && c.Email.Contains(query.Search)));

        if (!string.IsNullOrWhiteSpace(query.ErrorFilter))
            q = q.Where(c => c.MigrationError != null && c.MigrationError.Contains(query.ErrorFilter));

        var totalCount = await q.CountAsync(ct);
        var items = await q.OrderBy(c => c.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(c => new SyncEntityDetailDto
            {
                Id = c.Id,
                OdooId = c.OdooId,
                Name = c.Name,
                Email = c.Email,
                SyncStatus = c.IsMigrated ? "Synced" : (!string.IsNullOrEmpty(c.MigrationError) ? "Failed" : "Pending"),
                ZohoId = c.ZohoCandidateId,
                SyncedAt = c.MigratedAt,
                ErrorMessage = c.MigrationError,
                AdditionalInfo = c.IsCvMigrated ? "CV: Uploaded" : (string.IsNullOrEmpty(c.ResumeUrl) ? "CV: None" : "CV: Pending")
            })
            .ToListAsync(ct);

        return new PagedSyncResultDto
        {
            EntityType = "Candidates", Items = items, TotalCount = totalCount,
            Page = query.Page, PageSize = query.PageSize, Filter = query.Filter
        };
    }

    private async Task<PagedSyncResultDto> GetApplicationsSyncStatusAsync(SyncStatusQueryDto query, CancellationToken ct)
    {
        var q = _dbContext.Applications.Include(a => a.Candidate).Include(a => a.Job).AsQueryable();
        q = ApplySyncFilter(q, query.Filter, a => a.IsMigrated, a => a.MigrationError);

        if (!string.IsNullOrWhiteSpace(query.Search))
            q = q.Where(a => a.Candidate.Name.Contains(query.Search) || a.Job.Title.Contains(query.Search));

        var totalCount = await q.CountAsync(ct);
        var items = await q.OrderBy(a => a.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(a => new SyncEntityDetailDto
            {
                Id = a.Id,
                OdooId = a.OdooId,
                Name = a.Candidate.Name + " -> " + a.Job.Title,
                Email = a.Candidate.Email,
                SyncStatus = a.IsMigrated ? "Synced" : (!string.IsNullOrEmpty(a.MigrationError) ? "Failed" : "Pending"),
                ZohoId = a.ZohoApplicationId,
                SyncedAt = a.MigratedAt,
                ErrorMessage = a.MigrationError,
                AdditionalInfo = $"Status: {a.Status}"
            })
            .ToListAsync(ct);

        return new PagedSyncResultDto
        {
            EntityType = "Applications", Items = items, TotalCount = totalCount,
            Page = query.Page, PageSize = query.PageSize, Filter = query.Filter
        };
    }

    private async Task<PagedSyncResultDto> GetCvsSyncStatusAsync(SyncStatusQueryDto query, CancellationToken ct)
    {
        var q = _dbContext.Candidates
            .Where(c => c.IsMigrated && !string.IsNullOrEmpty(c.ResumeUrl))
            .AsQueryable();

        q = query.Filter?.ToLowerInvariant() switch
        {
            "synced" => q.Where(c => c.IsCvMigrated),
            "pending" => q.Where(c => !c.IsCvMigrated),
            _ => q
        };

        if (!string.IsNullOrWhiteSpace(query.Search))
            q = q.Where(c => c.Name.Contains(query.Search) || (c.Email != null && c.Email.Contains(query.Search)));

        var totalCount = await q.CountAsync(ct);
        var items = await q.OrderBy(c => c.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(c => new SyncEntityDetailDto
            {
                Id = c.Id,
                OdooId = c.OdooId,
                Name = c.Name,
                Email = c.Email,
                SyncStatus = c.IsCvMigrated ? "Synced" : "Pending",
                ZohoId = c.ZohoCandidateId,
                SyncedAt = c.CvMigratedAt,
                AdditionalInfo = c.ResumeUrl
            })
            .ToListAsync(ct);

        return new PagedSyncResultDto
        {
            EntityType = "CVs", Items = items, TotalCount = totalCount,
            Page = query.Page, PageSize = query.PageSize, Filter = query.Filter ?? "all"
        };
    }

    private async Task<PagedSyncResultDto> GetCommentsSyncStatusAsync(SyncStatusQueryDto query, CancellationToken ct)
    {
        var q = _dbContext.ApplicationComments.AsQueryable();
        q = query.Filter?.ToLowerInvariant() switch
        {
            "synced" => q.Where(c => c.IsMigrated),
            "pending" => q.Where(c => !c.IsMigrated),
            _ => q
        };

        var totalCount = await q.CountAsync(ct);
        var items = await q.OrderBy(c => c.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(c => new SyncEntityDetailDto
            {
                Id = c.Id,
                OdooId = c.ApplicationOdooId,
                Name = c.Author,
                SyncStatus = c.IsMigrated ? "Synced" : "Pending",
                ZohoId = c.ZohoNoteId,
                SyncedAt = c.MigratedAt,
                AdditionalInfo = c.Body.Length > 100 ? c.Body.Substring(0, 100) + "..." : c.Body
            })
            .ToListAsync(ct);

        return new PagedSyncResultDto
        {
            EntityType = "Comments", Items = items, TotalCount = totalCount,
            Page = query.Page, PageSize = query.PageSize, Filter = query.Filter ?? "all"
        };
    }

    private async Task<PagedSyncResultDto> GetSummariesSyncStatusAsync(SyncStatusQueryDto query, CancellationToken ct)
    {
        var q = _dbContext.ApplicationSummaries.AsQueryable();
        q = query.Filter?.ToLowerInvariant() switch
        {
            "synced" => q.Where(s => s.IsMigrated),
            "pending" => q.Where(s => !s.IsMigrated),
            _ => q
        };

        var totalCount = await q.CountAsync(ct);
        var items = await q.OrderBy(s => s.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(s => new SyncEntityDetailDto
            {
                Id = s.Id,
                OdooId = s.ApplicationOdooId,
                Name = $"Summary (Recruiter: {s.Recruiter})",
                SyncStatus = s.IsMigrated ? "Synced" : "Pending",
                ZohoId = s.ZohoNoteId,
                SyncedAt = s.MigratedAt,
                AdditionalInfo = $"Salary: {s.ExpectedSalary:N0}, Source: {s.Source}"
            })
            .ToListAsync(ct);

        return new PagedSyncResultDto
        {
            EntityType = "Summaries", Items = items, TotalCount = totalCount,
            Page = query.Page, PageSize = query.PageSize, Filter = query.Filter ?? "all"
        };
    }

    private async Task<PagedSyncResultDto> GetHistorySyncStatusAsync(SyncStatusQueryDto query, CancellationToken ct)
    {
        var q = _dbContext.ApplicationHistories.AsQueryable();
        q = query.Filter?.ToLowerInvariant() switch
        {
            "synced" => q.Where(h => h.IsMigrated),
            "pending" => q.Where(h => !h.IsMigrated),
            _ => q
        };

        var totalCount = await q.CountAsync(ct);
        var items = await q.OrderBy(h => h.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(h => new SyncEntityDetailDto
            {
                Id = h.Id,
                OdooId = h.ApplicationOdooId,
                Name = $"History - {h.ChangeDate:yyyy-MM-dd}",
                SyncStatus = h.IsMigrated ? "Synced" : "Pending",
                ZohoId = h.ZohoNoteId,
                SyncedAt = h.MigratedAt,
                AdditionalInfo = h.Description.Length > 100 ? h.Description.Substring(0, 100) + "..." : h.Description
            })
            .ToListAsync(ct);

        return new PagedSyncResultDto
        {
            EntityType = "History", Items = items, TotalCount = totalCount,
            Page = query.Page, PageSize = query.PageSize, Filter = query.Filter ?? "all"
        };
    }

    // ========================================================================
    // OTHER PUBLIC METHODS
    // ========================================================================

    public async Task<MigrationStatusDto?> GetMigrationStatusAsync(int runId, CancellationToken ct = default)
    {
        var run = await _dbContext.MigrationRuns.FindAsync(new object[] { runId }, ct);
        if (run == null) return null;

        return MapRunToStatus(run);
    }

    public async Task<MigrationSummaryDto> GetMigrationSummaryAsync(CancellationToken ct = default)
    {
        var runs = await _dbContext.MigrationRuns.ToListAsync(ct);
        var lastRun = runs.OrderByDescending(r => r.StartedAt).FirstOrDefault();

        // Get CV counts
        var cvTotal = await _dbContext.Candidates.CountAsync(c => c.IsMigrated && !string.IsNullOrEmpty(c.ResumeUrl), ct);
        var cvMigrated = await _dbContext.Candidates.CountAsync(c => c.IsCvMigrated, ct);
        var cvPending = await _dbContext.Candidates.CountAsync(c => c.IsMigrated && !c.IsCvMigrated && !string.IsNullOrEmpty(c.ResumeUrl), ct);

        // Get comments counts
        var commentTotal = await _dbContext.ApplicationComments.CountAsync(ct);
        var commentMigrated = await _dbContext.ApplicationComments.CountAsync(c => c.IsMigrated, ct);

        // Get summaries counts
        var summaryTotal = await _dbContext.ApplicationSummaries.CountAsync(ct);
        var summaryMigrated = await _dbContext.ApplicationSummaries.CountAsync(s => s.IsMigrated, ct);

        // Get history counts
        var historyTotal = await _dbContext.ApplicationHistories.CountAsync(ct);
        var historyMigrated = await _dbContext.ApplicationHistories.CountAsync(h => h.IsMigrated, ct);

        return new MigrationSummaryDto
        {
            TotalRuns = runs.Count,
            SuccessfulRuns = runs.Count(r => r.Status == "Completed"),
            FailedRuns = runs.Count(r => r.Status == "Failed"),
            Jobs = new EntitySummaryDto
            {
                Total = await _dbContext.Jobs.CountAsync(ct),
                Migrated = await _dbContext.Jobs.CountAsync(j => j.IsMigrated, ct),
                Pending = await _dbContext.Jobs.CountAsync(j => !j.IsMigrated && string.IsNullOrEmpty(j.MigrationError), ct),
                Failed = await _dbContext.Jobs.CountAsync(j => !j.IsMigrated && !string.IsNullOrEmpty(j.MigrationError), ct)
            },
            Candidates = new EntitySummaryDto
            {
                Total = await _dbContext.Candidates.CountAsync(ct),
                Migrated = await _dbContext.Candidates.CountAsync(c => c.IsMigrated, ct),
                Pending = await _dbContext.Candidates.CountAsync(c => !c.IsMigrated && string.IsNullOrEmpty(c.MigrationError), ct),
                Failed = await _dbContext.Candidates.CountAsync(c => !c.IsMigrated && !string.IsNullOrEmpty(c.MigrationError), ct)
            },
            Applications = new EntitySummaryDto
            {
                Total = await _dbContext.Applications.CountAsync(ct),
                Migrated = await _dbContext.Applications.CountAsync(a => a.IsMigrated, ct),
                Pending = await _dbContext.Applications.CountAsync(a => !a.IsMigrated && string.IsNullOrEmpty(a.MigrationError), ct),
                Failed = await _dbContext.Applications.CountAsync(a => !a.IsMigrated && !string.IsNullOrEmpty(a.MigrationError), ct)
            },
            CvUploads = new EntitySummaryDto { Total = cvTotal, Migrated = cvMigrated, Pending = cvPending, Failed = cvTotal - cvMigrated - cvPending },
            Comments = new EntitySummaryDto { Total = commentTotal, Migrated = commentMigrated, Pending = commentTotal - commentMigrated, Failed = 0 },
            Summaries = new EntitySummaryDto { Total = summaryTotal, Migrated = summaryMigrated, Pending = summaryTotal - summaryMigrated, Failed = 0 },
            History = new EntitySummaryDto { Total = historyTotal, Migrated = historyMigrated, Pending = historyTotal - historyMigrated, Failed = 0 },
            LastMigrationDate = lastRun?.StartedAt
        };
    }

    public async Task CancelMigrationAsync(int runId, CancellationToken ct = default)
    {
        _currentMigrationCts?.Cancel();

        var run = await _dbContext.MigrationRuns.FindAsync(new object[] { runId }, ct);
        if (run != null && run.Status == "Running")
        {
            run.Status = "Cancelled";
            run.CompletedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(ct);
        }
    }

    public async Task<MigrationResultDto> RetryFailedMigrationsAsync(RetryMigrationDto options, CancellationToken ct = default)
    {
        var entityType = options.EntityType?.ToLowerInvariant() ?? "all";

        var migrationOptions = new MigrationOptionsDto
        {
            MigrateJobs = entityType is "all" or "job",
            MigrateCandidates = entityType is "all" or "candidate",
            MigrateApplications = entityType is "all" or "application",
            MigrateCvs = entityType is "all" or "cv",
            MigrateComments = entityType is "all" or "comment",
            MigrateSummaries = entityType is "all" or "summary",
            MigrateHistory = entityType is "all" or "history",
            RetryFailed = true,
            BatchSize = options.BatchSize > 0 ? options.BatchSize : _settings.DefaultBatchSize
        };

        return await StartMigrationAsync(migrationOptions, ct);
    }

    public async Task<int> ResetFailedRecordsAsync(string entityType, CancellationToken ct = default)
    {
        int count = 0;

        switch (entityType.ToLowerInvariant())
        {
            case "jobs":
                var failedJobs = await _dbContext.Jobs.Where(j => !j.IsMigrated && !string.IsNullOrEmpty(j.MigrationError)).ToListAsync(ct);
                foreach (var j in failedJobs) { j.MigrationError = null; }
                count = failedJobs.Count;
                break;

            case "candidates":
                var failedCandidates = await _dbContext.Candidates.Where(c => !c.IsMigrated && !string.IsNullOrEmpty(c.MigrationError)).ToListAsync(ct);
                foreach (var c in failedCandidates) { c.MigrationError = null; }
                count = failedCandidates.Count;
                break;

            case "applications":
                var failedApps = await _dbContext.Applications.Where(a => !a.IsMigrated && !string.IsNullOrEmpty(a.MigrationError)).ToListAsync(ct);
                foreach (var a in failedApps) { a.MigrationError = null; }
                count = failedApps.Count;
                break;

            default:
                return 0;
        }

        await _dbContext.SaveChangesAsync(ct);
        _logger.LogInformation("Reset {Count} failed {EntityType} records for retry", count, entityType);
        return count;
    }

    public async Task<List<MigrationStatusDto>> GetAllRunsAsync(CancellationToken ct = default)
    {
        var runs = await _dbContext.MigrationRuns
            .OrderByDescending(r => r.StartedAt)
            .Take(50)
            .ToListAsync(ct);

        return runs.Select(MapRunToStatus).ToList();
    }

    // ========================================================================
    // HELPERS
    // ========================================================================

    private async Task<int> CountPendingAsync<T>(bool retryFailed, CancellationToken ct) where T : class
    {
        if (typeof(T) == typeof(Job))
        {
            return retryFailed
                ? await _dbContext.Jobs.CountAsync(j => !j.IsMigrated, ct)
                : await _dbContext.Jobs.CountAsync(j => !j.IsMigrated && string.IsNullOrEmpty(j.MigrationError), ct);
        }
        return 0;
    }

    private async Task<int> CountPendingCandidatesAsync(bool retryFailed, CancellationToken ct)
    {
        return retryFailed
            ? await _dbContext.Candidates.CountAsync(c => !c.IsMigrated && !string.IsNullOrEmpty(c.Email), ct)
            : await _dbContext.Candidates.CountAsync(c => !c.IsMigrated && !string.IsNullOrEmpty(c.Email) && string.IsNullOrEmpty(c.MigrationError), ct);
    }

    private async Task<int> CountPendingApplicationsAsync(bool retryFailed, CancellationToken ct)
    {
        var query = _dbContext.Applications
            .Include(a => a.Candidate)
            .Include(a => a.Job)
            .Where(a => !a.IsMigrated
                && a.Candidate.IsMigrated
                && !string.IsNullOrEmpty(a.Candidate.ZohoCandidateId)
                && a.Job.IsMigrated
                && !string.IsNullOrEmpty(a.Job.ZohoJobId));

        if (!retryFailed)
            query = query.Where(a => string.IsNullOrEmpty(a.MigrationError));

        return await query.CountAsync(ct);
    }

    private static string BuildApplicationComments(Application app)
    {
        var comments = $"Applied on {app.AppliedAt?.ToString("yyyy-MM-dd") ?? app.CreatedAt.ToString("yyyy-MM-dd")}. " +
                      $"Status: {app.Status}. Priority: {app.Priority}";

        if (!string.IsNullOrEmpty(app.CoverNote))
            comments += $"\n\nCover Note: {app.CoverNote}";

        return comments;
    }

    private void WriteMigrationLog(string entityType, int entityId, int odooId, EntityMigrationResult result)
    {
        _dbContext.MigrationLogs.Add(new MigrationLog
        {
            EntityType = entityType,
            EntityId = entityId,
            OdooId = odooId,
            ZohoId = result.ZohoId,
            Status = result.Success ? "Success" : "Failed",
            ErrorMessage = result.ErrorMessage,
            ErrorDetails = result.ErrorDetails,
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        });

        if (result.Success)
            _logger.LogDebug("{EntityType} {Id} (Odoo:{OdooId}) -> Zoho:{ZohoId}", entityType, entityId, odooId, result.ZohoId);
        else
            _logger.LogWarning("{EntityType} {Id} (Odoo:{OdooId}) FAILED: {Error}", entityType, entityId, odooId, result.ErrorMessage);
    }

    private static string? GetFileNameFromUrl(string url)
    {
        try { return Path.GetFileName(new Uri(url).AbsolutePath); }
        catch { return null; }
    }

    private static IQueryable<T> ApplySyncFilter<T>(
        IQueryable<T> query, string? filter,
        System.Linq.Expressions.Expression<Func<T, bool>> isMigratedExpr,
        System.Linq.Expressions.Expression<Func<T, string?>> errorExpr) where T : class
    {
        // For simple filters we build manual conditions
        return filter?.ToLowerInvariant() switch
        {
            "synced" => query.Where(isMigratedExpr),
            "pending" => query.Where(NegateExpression(isMigratedExpr)),
            "failed" => query.Where(NegateExpression(isMigratedExpr)),
            _ => query
        };
    }

    private static System.Linq.Expressions.Expression<Func<T, bool>> NegateExpression<T>(
        System.Linq.Expressions.Expression<Func<T, bool>> expression)
    {
        var negated = System.Linq.Expressions.Expression.Not(expression.Body);
        return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(negated, expression.Parameters);
    }

    private static MigrationStatusDto MapRunToStatus(MigrationRun run) => new()
    {
        RunId = run.Id,
        Status = run.Status,
        StartedAt = run.StartedAt,
        CompletedAt = run.CompletedAt,
        Jobs = new MigrationProgressDto { Total = run.TotalJobs, Migrated = run.MigratedJobs, Failed = run.FailedJobs },
        Candidates = new MigrationProgressDto { Total = run.TotalCandidates, Migrated = run.MigratedCandidates, Failed = run.FailedCandidates },
        Applications = new MigrationProgressDto { Total = run.TotalApplications, Migrated = run.MigratedApplications, Failed = run.FailedApplications },
        CvUploads = new MigrationProgressDto { Total = run.TotalCvsUploaded + run.FailedCvUploads, Migrated = run.TotalCvsUploaded, Failed = run.FailedCvUploads }
    };
}
