using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OdooToZohoMigration.Core.Interfaces;
using OdooToZohoMigration.Infrastructure.Configuration;

namespace OdooToZohoMigration.Infrastructure.Services;

/// <summary>
/// Hosted background service that runs the migration automatically when the app starts.
///
///   "MigrationSettings": {
///       "Enabled": true       ← flip this to start / stop
///   }
///
/// If RepeatIntervalSeconds > 0 it will loop forever (good for ongoing sync).
/// If RepeatIntervalSeconds == 0 it runs once and then idles.
/// </summary>
public class MigrationBackgroundService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly MigrationSettings _cfg;
    private readonly ILogger<MigrationBackgroundService> _log;

    public MigrationBackgroundService(
        IServiceProvider sp,
        IOptions<MigrationSettings> cfg,
        ILogger<MigrationBackgroundService> log)
    {
        _sp = sp;
        _cfg = cfg.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_cfg.Enabled)
        {
            _log.LogInformation("Migration is DISABLED in config (MigrationSettings.Enabled = false). Doing nothing.");
            return;
        }

        _log.LogInformation("Migration background service started.  Waiting 5 s for app warm-up...");
        await Task.Delay(5000, stoppingToken);

        do
        {
            try
            {
                // Create a fresh DI scope for each run (EF DbContext is scoped)
                using var scope = _sp.CreateScope();
                var migrationService = scope.ServiceProvider.GetRequiredService<IMigrationService>();

                _log.LogInformation("Starting migration run...");
                await migrationService.RunAsync(stoppingToken);
                _log.LogInformation("Migration run finished.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _log.LogInformation("Migration background service shutting down (app stopping).");
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Migration run crashed. Will retry after interval if configured.");
            }

            // Repeat or stop
            if (_cfg.RepeatIntervalSeconds > 0)
            {
                _log.LogInformation("Next migration run in {Sec} seconds...", _cfg.RepeatIntervalSeconds);
                await Task.Delay(TimeSpan.FromSeconds(_cfg.RepeatIntervalSeconds), stoppingToken);
            }
            else
            {
                _log.LogInformation("RepeatIntervalSeconds = 0 → single run complete.  Service idling.");
                break;
            }

        } while (!stoppingToken.IsCancellationRequested);
    }
}
