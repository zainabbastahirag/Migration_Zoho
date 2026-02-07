using OdooToZohoMigration.Core.DTOs.Migration;

namespace OdooToZohoMigration.Core.Interfaces;

public interface IMigrationService
{
    // ===== Migration Operations =====

    /// <summary>
    /// Start a migration with specified options. Processes entities in bulk batches.
    /// </summary>
    Task<MigrationResultDto> StartMigrationAsync(MigrationOptionsDto options, CancellationToken ct = default);

    /// <summary>
    /// Get status of a specific migration run.
    /// </summary>
    Task<MigrationStatusDto?> GetMigrationStatusAsync(int runId, CancellationToken ct = default);

    /// <summary>
    /// Get overall migration summary across all runs.
    /// </summary>
    Task<MigrationSummaryDto> GetMigrationSummaryAsync(CancellationToken ct = default);

    /// <summary>
    /// Cancel a running migration.
    /// </summary>
    Task CancelMigrationAsync(int runId, CancellationToken ct = default);

    /// <summary>
    /// Retry failed migrations for specified entity type.
    /// </summary>
    Task<MigrationResultDto> RetryFailedMigrationsAsync(RetryMigrationDto options, CancellationToken ct = default);

    // ===== Sync Tracking =====

    /// <summary>
    /// Get a comprehensive sync dashboard showing status of every entity type.
    /// This is the main view to understand what's synced and what's not.
    /// </summary>
    Task<SyncDashboardDto> GetSyncDashboardAsync(CancellationToken ct = default);

    /// <summary>
    /// Get detailed sync status for a specific entity type with pagination and filtering.
    /// entityType: "jobs", "candidates", "applications", "cvs", "comments", "summaries", "history"
    /// </summary>
    Task<PagedSyncResultDto> GetSyncStatusAsync(string entityType, SyncStatusQueryDto query, CancellationToken ct = default);

    /// <summary>
    /// Reset migration status for failed records so they can be retried.
    /// </summary>
    Task<int> ResetFailedRecordsAsync(string entityType, CancellationToken ct = default);

    /// <summary>
    /// Get all migration runs with their status.
    /// </summary>
    Task<List<MigrationStatusDto>> GetAllRunsAsync(CancellationToken ct = default);
}
