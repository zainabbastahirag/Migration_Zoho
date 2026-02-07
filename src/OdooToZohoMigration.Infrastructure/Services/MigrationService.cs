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
/// Config-driven migration service.
///
/// Every batch size and delay is configurable per entity type in appsettings.json:
///   JobsBatchSize / JobsDelayMs
///   CandidatesBatchSize / CandidatesDelayMs
///   CvsBatchSize / CvsDelayMs / MaxConcurrentCvUploads
///   ApplicationsBatchSize / ApplicationsDelayMs
///   CommentsBatchSize / CommentsDelayMs
///   SummariesBatchSize / SummariesDelayMs
///   HistoryBatchSize / HistoryDelayMs
///
/// Rate-limit 429 handling is inside ZohoRecruitService.SendZohoRequestAsync —
/// it auto-retries with exponential back-off so this service does not need to
/// worry about 429s at all.  The delays configured here are ADDITIONAL throttle
/// to stay well under the limit proactively.
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
    //  ENTRY POINT
    // ================================================================

    public async Task RunAsync(CancellationToken ct = default)
    {
        var run = new MigrationRun
        {
            RunType = _cfg.RetryFailed ? "Retry" : "Full",
            StartedAt = DateTime.UtcNow,
            Status = "Running"
        };

        if (_cfg.MigrateJobs)
            run.TotalJobs = await PendingJobs(ct);
        if (_cfg.MigrateCandidates)
            run.TotalCandidates = await PendingCandidates(ct);
        if (_cfg.MigrateApplications)
            run.TotalApplications = await PendingApplications(ct);

        _db.MigrationRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("╔══════════════════════════════════════════════════════════════╗");
        _log.LogInformation("║  Migration Run {Id} — {Type}                                 ║", run.Id, run.RunType);
        _log.LogInformation("║  Jobs={J} (batch {JB})  Candidates={C} (batch {CB})          ║",
            run.TotalJobs, _cfg.JobsBatchSize, run.TotalCandidates, _cfg.CandidatesBatchSize);
        _log.LogInformation("║  Apps={A} (batch {AB})  CVs (batch {CVB}, x{CC} parallel)    ║",
            run.TotalApplications, _cfg.ApplicationsBatchSize, _cfg.CvsBatchSize, _cfg.MaxConcurrentCvUploads);
        _log.LogInformation("╚══════════════════════════════════════════════════════════════╝");

        try
        {
            if (_cfg.MigrateJobs && run.TotalJobs > 0)
            {
                _log.LogInformation("▶ Phase 1/7: Jobs ({Count} pending, batch={B}, delay={D}ms)",
                    run.TotalJobs, _cfg.JobsBatchSize, _cfg.JobsDelayMs);
                await MigrateJobsAsync(run, ct);
            }

            if (_cfg.MigrateCandidates && run.TotalCandidates > 0)
            {
                _log.LogInformation("▶ Phase 2/7: Candidates ({Count} pending, batch={B}, delay={D}ms)",
                    run.TotalCandidates, _cfg.CandidatesBatchSize, _cfg.CandidatesDelayMs);
                await MigrateCandidatesAsync(run, ct);
            }

            if (_cfg.MigrateCvs)
            {
                var p = await _db.Candidates.CountAsync(c =>
                    c.IsMigrated && !c.IsCvMigrated
                    && !string.IsNullOrEmpty(c.ResumeUrl)
                    && !string.IsNullOrEmpty(c.ZohoCandidateId), ct);
                if (p > 0)
                {
                    _log.LogInformation("▶ Phase 3/7: CVs ({Count} pending, batch={B}, x{CC} parallel, delay={D}ms)",
                        p, _cfg.CvsBatchSize, _cfg.MaxConcurrentCvUploads, _cfg.CvsDelayMs);
                    await MigrateCvsAsync(run, ct);
                }
            }

            if (_cfg.MigrateApplications && run.TotalApplications > 0)
            {
                _log.LogInformation("▶ Phase 4/7: Applications ({Count} pending, batch={B}, delay={D}ms per call)",
                    run.TotalApplications, _cfg.ApplicationsBatchSize, _cfg.ApplicationsDelayMs);
                await MigrateApplicationsAsync(run, ct);
            }

            if (_cfg.MigrateComments)
            {
                var p = await _db.ApplicationComments.CountAsync(c =>
                    !c.IsMigrated && c.Application.IsMigrated
                    && !string.IsNullOrEmpty(c.Application.Candidate.ZohoCandidateId), ct);
                if (p > 0)
                {
                    _log.LogInformation("▶ Phase 5/7: Comments ({Count} pending, batch={B}, delay={D}ms)",
                        p, _cfg.CommentsBatchSize, _cfg.CommentsDelayMs);
                    await MigrateCommentsAsync(run, ct);
                }
            }

            if (_cfg.MigrateSummaries)
            {
                var p = await _db.ApplicationSummaries.CountAsync(s =>
                    !s.IsMigrated && s.Application != null && s.Application.IsMigrated
                    && !string.IsNullOrEmpty(s.Application.Candidate.ZohoCandidateId), ct);
                if (p > 0)
                {
                    _log.LogInformation("▶ Phase 6/7: Summaries ({Count} pending, batch={B}, delay={D}ms)",
                        p, _cfg.SummariesBatchSize, _cfg.SummariesDelayMs);
                    await MigrateSummariesAsync(run, ct);
                }
            }

            if (_cfg.MigrateHistory)
            {
                var p = await _db.ApplicationHistories.CountAsync(h =>
                    !h.IsMigrated && h.Application.IsMigrated
                    && !string.IsNullOrEmpty(h.Application.Candidate.ZohoCandidateId), ct);
                if (p > 0)
                {
                    _log.LogInformation("▶ Phase 7/7: History ({Count} pending, batch={B}, delay={D}ms)",
                        p, _cfg.HistoryBatchSize, _cfg.HistoryDelayMs);
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
        await _db.SaveChangesAsync(CancellationToken.None);
        LogFinalSummary(run);
    }

    // ================================================================
    //  PHASE 1 — JOBS
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
                .Take(_cfg.JobsBatchSize)
                .ToListAsync(ct);

            if (batch.Count == 0) break;

            var results = await _zoho.CreateJobOpeningsBatchAsync(batch, ct);

            for (int i = 0; i < batch.Count && i < results.Count; i++)
            {
                var j = batch[i]; var r = results[i];
                j.IsMigrated = r.Success;
                j.ZohoJobId = r.ZohoId;
                j.MigratedAt = r.Success ? DateTime.UtcNow : null;
                j.MigrationError = r.ErrorMessage;
                WriteLog("Job", j.Id, j.OdooId, r);
                if (r.Success) ok++; else fail++;
            }

            run.MigratedJobs = ok; run.FailedJobs = fail;
            await _db.SaveChangesAsync(ct);
            cursor = batch.Max(j => j.Id);
            _log.LogInformation("  Jobs: {Ok}/{Total} synced, {Fail} failed (cursor={C})",
                ok, run.TotalJobs, fail, cursor);

            await Task.Delay(_cfg.JobsDelayMs, ct);
        }
    }

    // ================================================================
    //  PHASE 2 — CANDIDATES
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
                .Take(_cfg.CandidatesBatchSize)
                .ToListAsync(ct);

            if (batch.Count == 0) break;

            var unique = batch
                .GroupBy(c => c.Email!.ToLowerInvariant())
                .Select(g => g.First())
                .ToList();

            var results = await _zoho.CreateCandidatesBatchAsync(unique, ct);

            for (int i = 0; i < unique.Count && i < results.Count; i++)
            {
                var c = unique[i]; var r = results[i];
                bool dup = r.ErrorMessage?.Contains("Duplicate", StringComparison.OrdinalIgnoreCase) == true;

                if (dup)
                {
                    var all = await _db.Candidates
                        .Where(x => x.Email != null && x.Email.ToLower() == c.Email!.ToLower())
                        .ToListAsync(ct);
                    foreach (var d in all)
                    {
                        d.IsMigrated = true; d.MigratedAt = DateTime.UtcNow;
                        d.ZohoCandidateId = r.ZohoId; d.MigrationError = null;
                    }
                    ok += all.Count;
                }
                else if (r.Success)
                {
                    c.IsMigrated = true; c.ZohoCandidateId = r.ZohoId;
                    c.MigratedAt = DateTime.UtcNow; c.MigrationError = null;
                    ok++;
                }
                else { c.MigrationError = r.ErrorMessage; fail++; }

                WriteLog("Candidate", c.Id, c.OdooId, r);
            }

            run.MigratedCandidates = ok; run.FailedCandidates = fail;
            await _db.SaveChangesAsync(ct);
            cursor = batch.Max(c => c.Id);
            _log.LogInformation("  Candidates: {Ok}/{Total} synced, {Fail} failed (cursor={C})",
                ok, run.TotalCandidates, fail, cursor);

            await Task.Delay(_cfg.CandidatesDelayMs, ct);
        }
    }

    // ================================================================
    //  PHASE 3 — CVs  (sequential — DbContext is NOT thread-safe)
    //  Downloads from Azure Blob → uploads to Zoho, one at a time.
    // ================================================================

    private async Task MigrateCvsAsync(MigrationRun run, CancellationToken ct)
    {
        int ok = 0, fail = 0;

        // Use the same query pattern as the old working code: re-query after each batch
        var candidates = await _db.Candidates
            .Where(c => c.IsMigrated && !c.IsCvMigrated
                && !string.IsNullOrEmpty(c.ResumeUrl)
                && !string.IsNullOrEmpty(c.ZohoCandidateId))
            .Take(_cfg.CvsBatchSize)
            .ToListAsync(ct);

        while (candidates.Count > 0)
        {
            foreach (var candidate in candidates)
            {
                try
                {
                    // 1. Download CV from Azure Blob Storage
                    var (stream, contentType, fileName) =
                        await _blob.GetCvWithMetadataAsync(candidate.ResumeUrl!, ct);

                    if (stream == null)
                    {
                        _log.LogWarning("CV not found in blob for candidate {Id}: {Url}",
                            candidate.Id, candidate.ResumeUrl);
                        fail++;
                        continue;
                    }

                    using (stream)
                    {
                        var cvFileName = fileName
                            ?? GetFileNameFromUrl(candidate.ResumeUrl!)
                            ?? $"resume_{candidate.OdooId}.pdf";
                        var cvContentType = contentType ?? "application/pdf";

                        // 2. Upload CV to Zoho Recruit
                        var result = await _zoho.UploadCandidateCvAsync(
                            candidate.ZohoCandidateId!,
                            stream,
                            cvFileName,
                            cvContentType,
                            ct);

                        if (result.Success)
                        {
                            candidate.IsCvMigrated = true;
                            candidate.CvMigratedAt = DateTime.UtcNow;
                            ok++;
                            _log.LogInformation("  CV uploaded for candidate {Id} ({File})",
                                candidate.Id, cvFileName);
                        }
                        else if (result.ErrorMessage != null &&
                                 result.ErrorMessage.Contains("not allowed to attach more than one file",
                                     StringComparison.OrdinalIgnoreCase))
                        {
                            // Zoho already has a resume for this candidate — treat as done
                            candidate.IsCvMigrated = true;
                            candidate.CvMigratedAt = DateTime.UtcNow;
                            ok++;
                            _log.LogInformation(
                                "  CV already exists in Zoho for candidate {Id} — marked as synced",
                                candidate.Id);
                        }
                        else
                        {
                            fail++;
                            _log.LogWarning("  CV upload FAILED for candidate {Id}: {Err}",
                                candidate.Id, result.ErrorMessage);
                        }
                    }
                }
                catch (Exception ex)
                {
                    fail++;
                    _log.LogError(ex, "  CV error for candidate {Id}", candidate.Id);
                }

                // Throttle between each individual upload
                await Task.Delay(_cfg.CvsDelayMs, ct);
            }

            // Save batch and update run counts
            run.TotalCvsUploaded = ok;
            run.FailedCvUploads = fail;
            await _db.SaveChangesAsync(ct);

            _log.LogInformation("  CVs: {Ok} uploaded, {Fail} failed so far", ok, fail);

            // Re-query for next batch (same pattern as old working code)
            candidates = await _db.Candidates
                .Where(c => c.IsMigrated && !c.IsCvMigrated
                    && !string.IsNullOrEmpty(c.ResumeUrl)
                    && !string.IsNullOrEmpty(c.ZohoCandidateId))
                .Take(_cfg.CvsBatchSize)
                .ToListAsync(ct);
        }
    }

    // ================================================================
    //  PHASE 4 — APPLICATIONS  (1-by-1 with configurable delay)
    // ================================================================

    private async Task MigrateApplicationsAsync(MigrationRun run, CancellationToken ct)
    {
        int ok = 0, fail = 0, cursor = 0;

        while (true)
        {
            var batch = await _db.Applications
                .Include(a => a.Candidate).Include(a => a.Job)
                .Where(a => !a.IsMigrated && a.Id > cursor
                    && a.Candidate.IsMigrated && !string.IsNullOrEmpty(a.Candidate.ZohoCandidateId)
                    && a.Job.IsMigrated && !string.IsNullOrEmpty(a.Job.ZohoJobId)
                    && (_cfg.RetryFailed || string.IsNullOrEmpty(a.MigrationError)))
                .OrderBy(a => a.Id)
                .Take(_cfg.ApplicationsBatchSize)
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

                a.IsMigrated = r.Success; a.ZohoApplicationId = r.ZohoId;
                a.MigratedAt = r.Success ? DateTime.UtcNow : null;
                a.MigrationError = r.ErrorMessage;
                WriteLog("Application", a.Id, a.OdooId, r);
                if (r.Success) ok++; else fail++;

                // configurable delay between each call
                await Task.Delay(_cfg.ApplicationsDelayMs, ct);
            }

            run.MigratedApplications = ok; run.FailedApplications = fail;
            await _db.SaveChangesAsync(ct);
            cursor = batch.Max(a => a.Id);
            _log.LogInformation("  Applications: {Ok}/{Total} synced, {Fail} failed (cursor={C})",
                ok, run.TotalApplications, fail, cursor);
        }
    }

    // ================================================================
    //  PHASE 5 — COMMENTS  (batch notes)
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
                .Take(_cfg.CommentsBatchSize)
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
            await Task.Delay(_cfg.CommentsDelayMs, ct);
        }
    }

    // ================================================================
    //  PHASE 6 — SUMMARIES  (batch notes)
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
                .Take(_cfg.SummariesBatchSize)
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
            await Task.Delay(_cfg.SummariesDelayMs, ct);
        }
    }

    // ================================================================
    //  PHASE 7 — HISTORY  (batch notes)
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
                .Take(_cfg.HistoryBatchSize)
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
            await Task.Delay(_cfg.HistoryDelayMs, ct);
        }
    }

    // ================================================================
    //  HELPERS
    // ================================================================

    private void WriteLog(string type, int id, int odooId, EntityMigrationResult r)
    {
        _db.MigrationLogs.Add(new MigrationLog
        {
            EntityType = type, EntityId = id, OdooId = odooId, ZohoId = r.ZohoId,
            Status = r.Success ? "Success" : "Failed",
            ErrorMessage = r.ErrorMessage, ErrorDetails = r.ErrorDetails,
            StartedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow
        });
    }

    private void LogFinalSummary(MigrationRun run)
    {
        var dur = run.CompletedAt.HasValue
            ? (run.CompletedAt.Value - run.StartedAt).ToString(@"hh\:mm\:ss") : "?";
        _log.LogInformation("╔══════════════════════════════════════════════════════════════╗");
        _log.LogInformation("║  Run {Id} {Status} in {Dur}                                  ║", run.Id, run.Status, dur);
        _log.LogInformation("║  Jobs        {M}/{T}  (failed {F})                           ║", run.MigratedJobs, run.TotalJobs, run.FailedJobs);
        _log.LogInformation("║  Candidates  {M}/{T}  (failed {F})                           ║", run.MigratedCandidates, run.TotalCandidates, run.FailedCandidates);
        _log.LogInformation("║  Apps        {M}/{T}  (failed {F})                           ║", run.MigratedApplications, run.TotalApplications, run.FailedApplications);
        _log.LogInformation("║  CVs         {U}  (failed {F})                               ║", run.TotalCvsUploaded, run.FailedCvUploads);
        _log.LogInformation("╚══════════════════════════════════════════════════════════════╝");
    }

    private async Task<int> PendingJobs(CancellationToken ct) =>
        await _db.Jobs.CountAsync(j => !j.IsMigrated
            && (_cfg.RetryFailed || string.IsNullOrEmpty(j.MigrationError)), ct);

    private async Task<int> PendingCandidates(CancellationToken ct) =>
        await _db.Candidates.CountAsync(c => !c.IsMigrated && !string.IsNullOrEmpty(c.Email)
            && (_cfg.RetryFailed || string.IsNullOrEmpty(c.MigrationError)), ct);

    private async Task<int> PendingApplications(CancellationToken ct) =>
        await _db.Applications.Include(a => a.Candidate).Include(a => a.Job)
            .CountAsync(a => !a.IsMigrated
                && a.Candidate.IsMigrated && !string.IsNullOrEmpty(a.Candidate.ZohoCandidateId)
                && a.Job.IsMigrated && !string.IsNullOrEmpty(a.Job.ZohoJobId)
                && (_cfg.RetryFailed || string.IsNullOrEmpty(a.MigrationError)), ct);

    private static string? GetFileNameFromUrl(string url)
    { try { return Path.GetFileName(new Uri(url).AbsolutePath); } catch { return null; } }
}
