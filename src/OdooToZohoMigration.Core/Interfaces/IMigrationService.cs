namespace OdooToZohoMigration.Core.Interfaces;

public interface IMigrationService
{
    /// <summary>
    /// Run the full migration driven by MigrationSettings toggles.
    /// Skips already-synced records automatically. Logs everything to MigrationRuns + MigrationLogs.
    /// </summary>
    Task RunAsync(CancellationToken ct = default);
}
