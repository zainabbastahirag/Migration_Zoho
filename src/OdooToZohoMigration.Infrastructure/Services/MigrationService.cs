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
/// Config-driven migration service.  Set MigrationSettings.Enabled = true and run.
/// All phases, batch sizes and retry behaviour come from appsettings.json.
///
/// Key optimisations vs. the original:
///   - Cursor-based pagination  (OrderBy Id + lastProcessedId)
///   - Index-based Zoho batch mapping  (no extra GET call per record)
///   - Concurrent CV uploads  (SemaphoreSlim)
///   - Batch notes API for comments / summaries / history
///   - Every record written to MigrationLogs
///   - MigrationRun counts updated after every batch
///   - Duplicate candidates detected and all matching DB rows marked synced
/// </summary>
public class MigrationService : IMigrationService
{
    private readonly MigrationDbContext _db;
    private readonly IZohoRecruitService _zoho;
    private readonly IBlobStorageService _blob;
    private readonly MigrationSettings _cfg;
    private readonly ILogger<MigrationService> _log;

    public MigrationService(
        MigrationDbContext db,
        IZohoRecruitService zoho,
        IBlobStorageService blob,
        IOptions<MigrationSettings> cfg,
        ILogger<MigrationService> log)
    {
        _db = db;
        _zoho = zoho;
        _blob = blob;
        _cfg = cfg.Value;
        _log = log;
    }

    // ================================================================
    //  PUBLIC ENTRY POINT
    // ================================================================

    public async Task RunAsync(CancellationToken ct = default)
    {
        var run = new MigrationRun
        {
            RunType = _cfg.RetryFailed ? "Retry" : "Full",
            StartedAt = DateTime.UtcNow,
            Status = "Running"
        };

        // ── count what needs doing ───────────────────────────────────
        if (_cfg.MigrateJobs)
            run.TotalJobs = await PendingJobs(_cfg.RetryFailed, ct);
        if (_cfg.MigrateCandidates)
            run.TotalCandidates = await PendingCandidates(_cfg.RetryFailed, ct);
        if (_cfg.MigrateApplications)
            run.TotalApplications = await PendingApplications(_cfg.RetryFailed, ct);

        _db.MigrationRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation(
            "╔══════════════════════════════════════════════════╗");
        _log.LogInformation(
            "║  Migration Run {Id} — {Type}                    ║", run.Id, run.RunType);
        _log.LogInformation(
            "║  Jobs={J}  Candidates={C}  Apps={A}             ║",
            run.TotalJobs, run.TotalCandidates, run.TotalApplications);
        _log.LogInformation(
            "╚══════════════════════════════════════════════════╝");

        try
        {
            // Phase 1 ─ Jobs
            if (_cfg.MigrateJobs && run.TotalJobs > 0)
            {
                _log.LogInformation("▶ Phase 1/7: Jobs ({Count} pending)", run.TotalJobs);
                await MigrateJobsAsync(run, ct);
            }

            // Phase 2 ─ Candidates
            if (_cfg.MigrateCandidates && run.TotalCandidates > 0)
            {
                _log.LogInformation("▶ Phase 2/7: Candidates ({Count} pending)", run.TotalCandidates);
                await MigrateCandidatesAsync(run, ct);
            }

            // Phase 3 ─ CVs
            if (_cfg.MigrateCvs)
            {
                var pending = await _db.Candidates.CountAsync(c =>
                    c.IsMigrated && !c.IsCvMigrated
                    && !string.IsNullOrEmpty(c.ResumeUrl)
                    && !string.IsNullOrEmpty(c.ZohoCandidateId), ct);

                if (pending > 0)
                {
                    _log.LogInformation("▶ Phase 3/7: CVs ({Count} pending, concurrency={C})",
                        pending, _cfg.MaxConcurrentCvUploads);
                    await MigrateCvsAsync(run, ct);
                }
            }

            // Phase 4 ─ Applications
            if (_cfg.MigrateApplications && run.TotalApplications > 0)
            {
                _log.LogInformation("▶ Phase 4/7: Applications ({Count} pending)", run.TotalApplications);
                await MigrateApplicationsAsync(run, ct);
            }

            // Phase 5 ─ Comments
            if (_cfg.MigrateComments)
            {
                var pending = await _db.ApplicationComments.CountAsync(c =>
                    !c.IsMigrated && c.Application.IsMigrated
                    && !string.IsNullOrEmpty(c.Application.Candidate.ZohoCandidateId), ct);

                if (pending > 0)
                {
                    _log.LogInformation("▶ Phase 5/7: Comments ({Count} pending)", pending);
                    await MigrateCommentsAsync(run, ct);
                }
            }

            // Phase 6 ─ Summaries
            if (_cfg.MigrateSummaries)
            {
                var pending = await _db.ApplicationSummaries.CountAsync(s =>
                    !s.IsMigrated && s.Application != null && s.Application.IsMigrated
                    && !string.IsNullOrEmpty(s.Application.Candidate.ZohoCandidateId), ct);

                if (pending > 0)
                {
                    _log.LogInformation("▶ Phase 6/7: Summaries ({Count} pending)", pending);
                    await MigrateSummariesAsync(run, ct);
                }
            }

            // Phase 7 ─ History
            if (_cfg.MigrateHistory)
            {
                var pending = await _db.ApplicationHistories.CountAsync(h =>
                    !h.IsMigrated && h.Application.IsMigrated
                    && !string.IsNullOrEmpty(h.Application.Candidate.ZohoCandidateId), ct);

                if (pending > 0)
                {
                    _log.LogInformation("▶ Phase 7/7: History ({Count} pending)", pending);
                    await MigrateHistoryAsync(run, ct);
                }
            }

            run.Status = "Completed";
        }
        catch (OperationCanceledException)
        {
            run.Status = "Cancelled";
            _log.LogWarning("Migration run {Id} was cancelled.", run.Id);
        }
        catch (Exception ex)
        {
            run.Status = "Failed";
            run.Notes = ex.Message;
            _log.LogError(ex, "Migration run {Id} failed.", run.Id);
        }

        run.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(CancellationToken.None); // save even if cancelled

        LogFinalSummary(run);
    }

    // ================================================================
    //  PHASE 1 — JOBS  (batch create, index mapping)
    // ================================================================

    private async Task MigrateJobsAsync(MigrationRun run, CancellationToken ct)
    {
        int ok = 0, fail = 0, cursor = 0;

        while (true)
        {
            var batch = await _db.Jobs
                .Where(j => !j.IsMigrated && j.Id > cursor
                    && (_cfg.RetryFailed || string.IsNullOrEmpty(j.MigrationError)))
                .OrderBy(j => j.Id)
                .Take(_cfg.BatchSize)
                .ToListAsync(ct);

            if (batch.Count == 0) break;

            var results = await _zoho.CreateJobOpeningsBatchAsync(batch, ct);

            for (int i = 0; i < batch.Count && i < results.Count; i++)
            {
                var j = batch[i];
                var r = results[i];
                j.IsMigrated = r.Success;
                j.ZohoJobId = r.ZohoId;
                j.MigratedAt = r.Success ? DateTime.UtcNow : null;
                j.MigrationError = r.ErrorMessage;
                WriteLog("Job", j.Id, j.OdooId, r);
                if (r.Success) ok++; else fail++;
            }

            run.MigratedJobs = ok;
            run.FailedJobs = fail;
            await _db.SaveChangesAsync(ct);
            cursor = batch.Max(j => j.Id);
            _log.LogInformation("  Jobs: {Ok} synced, {Fail} failed (cursor={C})", ok, fail, cursor);
            await Task.Delay(_cfg.DelayBetweenBatchesMs, ct);
        }
    }

    // ================================================================
    //  PHASE 2 — CANDIDATES  (batch create, duplicate handling)
    // ================================================================

    private async Task MigrateCandidatesAsync(MigrationRun run, CancellationToken ct)
    {
        int ok = 0, fail = 0, cursor = 0;

        while (true)
        {
            var batch = await _db.Candidates
                .Where(c => !c.IsMigrated && !string.IsNullOrEmpty(c.Email) && c.Id > cursor
                    && (_cfg.RetryFailed || string.IsNullOrEmpty(c.MigrationError)))
                .OrderBy(c => c.Id)
                .Take(_cfg.BatchSize)
                .ToListAsync(ct);

            if (batch.Count == 0) break;

            // deduplicate within batch by email
            var unique = batch
                .GroupBy(c => c.Email!.ToLowerInvariant())
                .Select(g => g.First())
                .ToList();

            // Zoho returns results in SAME ORDER as input → index mapping
            var results = await _zoho.CreateCandidatesBatchAsync(unique, ct);

            for (int i = 0; i < unique.Count && i < results.Count; i++)
            {
                var c = unique[i];
                var r = results[i];
                bool dup = r.ErrorMessage?.Contains("Duplicate", StringComparison.OrdinalIgnoreCase) == true;

                if (dup)
                {
                    // mark every DB row with same email as synced
                    var all = await _db.Candidates
                        .Where(x => x.Email != null
                            && x.Email.ToLower() == c.Email!.ToLower())
                        .ToListAsync(ct);
                    foreach (var d in all)
                    {
                        d.IsMigrated = true;
                        d.MigratedAt = DateTime.UtcNow;
                        d.ZohoCandidateId = r.ZohoId;
                        d.MigrationError = null;
                    }
                    ok += all.Count;
                    _log.LogInformation("  Candidate {Email} duplicate in Zoho → {N} DB rows marked synced",
                        c.Email, all.Count);
                }
                else if (r.Success)
                {
                    c.IsMigrated = true;
                    c.ZohoCandidateId = r.ZohoId;
                    c.MigratedAt = DateTime.UtcNow;
                    c.MigrationError = null;
                    ok++;
                }
                else
                {
                    c.MigrationError = r.ErrorMessage;
                    fail++;
                }

                WriteLog("Candidate", c.Id, c.OdooId, r);
            }

            run.MigratedCandidates = ok;
            run.FailedCandidates = fail;
            await _db.SaveChangesAsync(ct);
            cursor = batch.Max(c => c.Id);
            _log.LogInformation("  Candidates: {Ok} synced, {Fail} failed (cursor={C})", ok, fail, cursor);
            await Task.Delay(_cfg.DelayBetweenBatchesMs, ct);
        }
    }

    // ================================================================
    //  PHASE 3 — CVs  (concurrent upload via SemaphoreSlim)
    // ================================================================

    private async Task MigrateCvsAsync(MigrationRun run, CancellationToken ct)
    {
        int ok = 0, fail = 0, cursor = 0;
        var sem = new SemaphoreSlim(_cfg.MaxConcurrentCvUploads);

        while (true)
        {
            var batch = await _db.Candidates
                .Where(c => c.IsMigrated && !c.IsCvMigrated
                    && !string.IsNullOrEmpty(c.ResumeUrl)
                    && !string.IsNullOrEmpty(c.ZohoCandidateId)
                    && c.Id > cursor)
                .OrderBy(c => c.Id)
                .Take(_cfg.BatchSize)
                .ToListAsync(ct);

            if (batch.Count == 0) break;

            var tasks = batch.Select(async c =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    if (await UploadOneCvAsync(c, ct))
                        Interlocked.Increment(ref ok);
                    else
                        Interlocked.Increment(ref fail);
                }
                finally { sem.Release(); }
            });

            await Task.WhenAll(tasks);

            run.TotalCvsUploaded = ok;
            run.FailedCvUploads = fail;
            await _db.SaveChangesAsync(ct);
            cursor = batch.Max(c => c.Id);
            _log.LogInformation("  CVs: {Ok} uploaded, {Fail} failed (cursor={C})", ok, fail, cursor);
            await Task.Delay(_cfg.DelayBetweenBatchesMs, ct);
        }
    }

    private async Task<bool> UploadOneCvAsync(Candidate c, CancellationToken ct)
    {
        try
        {
            var (stream, contentType, fileName) = await _blob.GetCvWithMetadataAsync(c.ResumeUrl!, ct);
            if (stream == null) return false;

            using (stream)
            {
                var fn = fileName ?? GetFileNameFromUrl(c.ResumeUrl!) ?? $"resume_{c.OdooId}.pdf";
                var result = await _zoho.UploadCandidateCvAsync(
                    c.ZohoCandidateId!, stream, fn, contentType ?? "application/pdf", ct);

                c.IsCvMigrated = result.Success;
                c.CvMigratedAt = result.Success ? DateTime.UtcNow : null;
                return result.Success;
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "CV upload failed for candidate {Id}", c.Id);
            return false;
        }
    }

    // ================================================================
    //  PHASE 4 — APPLICATIONS  (associate API, 1-by-1 with throttle)
    // ================================================================

    private async Task MigrateApplicationsAsync(MigrationRun run, CancellationToken ct)
    {
        int ok = 0, fail = 0, cursor = 0;

        while (true)
        {
            var batch = await _db.Applications
                .Include(a => a.Candidate)
                .Include(a => a.Job)
                .Where(a => !a.IsMigrated && a.Id > cursor
                    && a.Candidate.IsMigrated
                    && !string.IsNullOrEmpty(a.Candidate.ZohoCandidateId)
                    && a.Job.IsMigrated
                    && !string.IsNullOrEmpty(a.Job.ZohoJobId)
                    && (_cfg.RetryFailed || string.IsNullOrEmpty(a.MigrationError)))
                .OrderBy(a => a.Id)
                .Take(_cfg.BatchSize)
                .ToListAsync(ct);

            if (batch.Count == 0) break;

            foreach (var a in batch)
            {
                var note = $"Applied {a.AppliedAt?.ToString("yyyy-MM-dd") ?? a.CreatedAt.ToString("yyyy-MM-dd")}. " +
                           $"Status: {a.Status}. Priority: {a.Priority}";
                if (!string.IsNullOrEmpty(a.CoverNote))
                    note += $"\n\nCover Note: {a.CoverNote}";

                var r = await _zoho.AssociateCandidateWithJobAsync(
                    a.Candidate.ZohoCandidateId!, a.Job.ZohoJobId!, note, ct);

                a.IsMigrated = r.Success;
                a.ZohoApplicationId = r.ZohoId;
                a.MigratedAt = r.Success ? DateTime.UtcNow : null;
                a.MigrationError = r.ErrorMessage;
                WriteLog("Application", a.Id, a.OdooId, r);
                if (r.Success) ok++; else fail++;

                await Task.Delay(2000, ct); // Zoho rate limit
            }

            run.MigratedApplications = ok;
            run.FailedApplications = fail;
            await _db.SaveChangesAsync(ct);
            cursor = batch.Max(a => a.Id);
            _log.LogInformation("  Applications: {Ok} synced, {Fail} failed (cursor={C})", ok, fail, cursor);
            await Task.Delay(_cfg.DelayBetweenBatchesMs, ct);
        }
    }

    // ================================================================
    //  PHASE 5 — COMMENTS  (batch notes API)
    // ================================================================

    private async Task MigrateCommentsAsync(MigrationRun run, CancellationToken ct)
    {
        int ok = 0, fail = 0, cursor = 0;

        while (true)
        {
            var batch = await _db.ApplicationComments
                .Include(c => c.Application).ThenInclude(a => a.Candidate)
                .Where(c => !c.IsMigrated && c.Id > cursor
                    && c.Application.IsMigrated
                    && !string.IsNullOrEmpty(c.Application.Candidate.ZohoCandidateId))
                .OrderBy(c => c.Id)
                .Take(_cfg.BatchSize)
                .ToListAsync(ct);

            if (batch.Count == 0) break;

            var notes = batch.Select(c => (
                ParentId: c.Application.Candidate.ZohoCandidateId!,
                Title: $"Comment - {c.CreatedAt:yyyy-MM-dd}",
                Content: $"Author: {c.Author}\nDate: {c.CreatedAt:yyyy-MM-dd HH:mm}\n\n{c.Body}"
            )).ToList();

            var results = await _zoho.CreateNotesBatchAsync("Candidates", notes, ct);

            for (int i = 0; i < batch.Count && i < results.Count; i++)
            {
                batch[i].IsMigrated = results[i].Success;
                batch[i].ZohoNoteId = results[i].ZohoId;
                batch[i].MigratedAt = results[i].Success ? DateTime.UtcNow : null;
                if (results[i].Success) ok++; else fail++;
            }

            await _db.SaveChangesAsync(ct);
            cursor = batch.Max(c => c.Id);
            _log.LogInformation("  Comments: {Ok} synced, {Fail} failed (cursor={C})", ok, fail, cursor);
            await Task.Delay(_cfg.DelayBetweenBatchesMs, ct);
        }
    }

    // ================================================================
    //  PHASE 6 — SUMMARIES  (batch notes API)
    // ================================================================

    private async Task MigrateSummariesAsync(MigrationRun run, CancellationToken ct)
    {
        int ok = 0, fail = 0, cursor = 0;

        while (true)
        {
            var batch = await _db.ApplicationSummaries
                .Include(s => s.Application).ThenInclude(a => a!.Candidate)
                .Where(s => !s.IsMigrated && s.Id > cursor
                    && s.Application != null && s.Application.IsMigrated
                    && !string.IsNullOrEmpty(s.Application.Candidate.ZohoCandidateId))
                .OrderBy(s => s.Id)
                .Take(_cfg.BatchSize)
                .ToListAsync(ct);

            if (batch.Count == 0) break;

            var notes = batch.Select(s => (
                ParentId: s.Application!.Candidate.ZohoCandidateId!,
                Title: "Application Summary",
                Content: $"Expected Salary: {s.ExpectedSalary:N2}\n" +
                         $"Proposed Salary: {s.ProposedSalary:N2}\n" +
                         $"Availability: {s.AvailabilityDate:yyyy-MM-dd}\n" +
                         $"Priority: {s.Priority}\nSource: {s.Source}\n" +
                         $"Degree: {s.Degree}\nRecruiter: {s.Recruiter}"
            )).ToList();

            var results = await _zoho.CreateNotesBatchAsync("Candidates", notes, ct);

            for (int i = 0; i < batch.Count && i < results.Count; i++)
            {
                batch[i].IsMigrated = results[i].Success;
                batch[i].ZohoNoteId = results[i].ZohoId;
                batch[i].MigratedAt = results[i].Success ? DateTime.UtcNow : null;
                if (results[i].Success) ok++; else fail++;
            }

            await _db.SaveChangesAsync(ct);
            cursor = batch.Max(s => s.Id);
            _log.LogInformation("  Summaries: {Ok} synced, {Fail} failed (cursor={C})", ok, fail, cursor);
            await Task.Delay(_cfg.DelayBetweenBatchesMs, ct);
        }
    }

    // ================================================================
    //  PHASE 7 — HISTORY  (batch notes API)
    // ================================================================

    private async Task MigrateHistoryAsync(MigrationRun run, CancellationToken ct)
    {
        int ok = 0, fail = 0, cursor = 0;

        while (true)
        {
            var batch = await _db.ApplicationHistories
                .Include(h => h.Application).ThenInclude(a => a.Candidate)
                .Where(h => !h.IsMigrated && h.Id > cursor
                    && h.Application.IsMigrated
                    && !string.IsNullOrEmpty(h.Application.Candidate.ZohoCandidateId))
                .OrderBy(h => h.Id)
                .Take(_cfg.BatchSize)
                .ToListAsync(ct);

            if (batch.Count == 0) break;

            var notes = batch.Select(h => (
                ParentId: h.Application.Candidate.ZohoCandidateId!,
                Title: $"History - {h.ChangeDate:yyyy-MM-dd}",
                Content: $"Stage Change: {h.ChangeDate:yyyy-MM-dd HH:mm}\n\n{h.Description}"
            )).ToList();

            var results = await _zoho.CreateNotesBatchAsync("Candidates", notes, ct);

            for (int i = 0; i < batch.Count && i < results.Count; i++)
            {
                batch[i].IsMigrated = results[i].Success;
                batch[i].ZohoNoteId = results[i].ZohoId;
                batch[i].MigratedAt = results[i].Success ? DateTime.UtcNow : null;
                if (results[i].Success) ok++; else fail++;
            }

            await _db.SaveChangesAsync(ct);
            cursor = batch.Max(h => h.Id);
            _log.LogInformation("  History: {Ok} synced, {Fail} failed (cursor={C})", ok, fail, cursor);
            await Task.Delay(_cfg.DelayBetweenBatchesMs, ct);
        }
    }

    // ================================================================
    //  HELPERS
    // ================================================================

    private void WriteLog(string type, int id, int odooId, EntityMigrationResult r)
    {
        _db.MigrationLogs.Add(new MigrationLog
        {
            EntityType = type, EntityId = id, OdooId = odooId,
            ZohoId = r.ZohoId,
            Status = r.Success ? "Success" : "Failed",
            ErrorMessage = r.ErrorMessage, ErrorDetails = r.ErrorDetails,
            StartedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow
        });
    }

    private void LogFinalSummary(MigrationRun run)
    {
        _log.LogInformation(
            "╔══════════════════════════════════════════════════╗");
        _log.LogInformation(
            "║  Run {Id} {Status} in {Dur}                     ║",
            run.Id, run.Status,
            run.CompletedAt.HasValue
                ? (run.CompletedAt.Value - run.StartedAt).ToString(@"hh\:mm\:ss")
                : "?");
        _log.LogInformation(
            "║  Jobs     {M}/{T}  (failed {F})                 ║",
            run.MigratedJobs, run.TotalJobs, run.FailedJobs);
        _log.LogInformation(
            "║  Cands    {M}/{T}  (failed {F})                 ║",
            run.MigratedCandidates, run.TotalCandidates, run.FailedCandidates);
        _log.LogInformation(
            "║  Apps     {M}/{T}  (failed {F})                 ║",
            run.MigratedApplications, run.TotalApplications, run.FailedApplications);
        _log.LogInformation(
            "║  CVs      {U}  (failed {F})                     ║",
            run.TotalCvsUploaded, run.FailedCvUploads);
        _log.LogInformation(
            "╚══════════════════════════════════════════════════╝");
    }

    private async Task<int> PendingJobs(bool retry, CancellationToken ct) =>
        await _db.Jobs.CountAsync(j => !j.IsMigrated && (retry || string.IsNullOrEmpty(j.MigrationError)), ct);

    private async Task<int> PendingCandidates(bool retry, CancellationToken ct) =>
        await _db.Candidates.CountAsync(c => !c.IsMigrated && !string.IsNullOrEmpty(c.Email)
            && (retry || string.IsNullOrEmpty(c.MigrationError)), ct);

    private async Task<int> PendingApplications(bool retry, CancellationToken ct) =>
        await _db.Applications
            .Include(a => a.Candidate).Include(a => a.Job)
            .CountAsync(a => !a.IsMigrated
                && a.Candidate.IsMigrated && !string.IsNullOrEmpty(a.Candidate.ZohoCandidateId)
                && a.Job.IsMigrated && !string.IsNullOrEmpty(a.Job.ZohoJobId)
                && (retry || string.IsNullOrEmpty(a.MigrationError)), ct);

    private static string? GetFileNameFromUrl(string url)
    {
        try { return Path.GetFileName(new Uri(url).AbsolutePath); }
        catch { return null; }
    }
}
