using System.Text.Json;
using AGONEAIHub.Infrastructure.Configuration;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AGONEAIHub.Infrastructure.Services.Spot;

/// <summary>
/// Sends job messages to Azure Service Bus queues.
/// When UseServiceBus=false (dev/local), does nothing — workers are called directly.
/// </summary>
public class SpotServiceBus : IAsyncDisposable
{
    private readonly SpotSettings _cfg;
    private readonly ILogger<SpotServiceBus> _log;
    private readonly ServiceBusClient? _client;

    public SpotServiceBus(IOptions<SpotSettings> cfg, ILogger<SpotServiceBus> log)
    {
        _cfg = cfg.Value;
        _log = log;

        if (_cfg.UseServiceBus && !string.IsNullOrEmpty(_cfg.ServiceBusConnectionString))
        {
            _client = new ServiceBusClient(_cfg.ServiceBusConnectionString);
            _log.LogInformation("[ServiceBus] Connected. Classify queue: {CQ}, Report queue: {RQ}",
                _cfg.ClassifyQueueName, _cfg.ReportQueueName);
        }
        else
        {
            _log.LogInformation("[ServiceBus] DISABLED (UseServiceBus=false). Workers must be called directly.");
        }
    }

    /// <summary>
    /// Send a classify job to the Service Bus classify queue.
    /// Message: { "jobId": "...", "jobType": 1 }
    /// </summary>
    public async Task SendClassifyJobAsync(string jobId, int jobType, CancellationToken ct = default)
    {
        if (_client == null)
        {
            _log.LogDebug("[ServiceBus] Skipping classify queue (disabled). JobId={JobId}", jobId);
            return;
        }

        await SendMessageAsync(_cfg.ClassifyQueueName, new { jobId, jobType }, ct);
        _log.LogInformation("[ServiceBus] Sent classify job to queue. JobId={JobId}", jobId);
    }

    /// <summary>
    /// Send a report generation job to the Service Bus report queue.
    /// Message: { "jobId": "...", "jobType": 2 }
    /// </summary>
    public async Task SendReportJobAsync(string jobId, int jobType, CancellationToken ct = default)
    {
        if (_client == null)
        {
            _log.LogDebug("[ServiceBus] Skipping report queue (disabled). JobId={JobId}", jobId);
            return;
        }

        await SendMessageAsync(_cfg.ReportQueueName, new { jobId, jobType }, ct);
        _log.LogInformation("[ServiceBus] Sent report job to queue. JobId={JobId}", jobId);
    }

    private async Task SendMessageAsync(string queueName, object body, CancellationToken ct)
    {
        var sender = _client!.CreateSender(queueName);
        try
        {
            var json = JsonSerializer.Serialize(body);
            var message = new ServiceBusMessage(json)
            {
                ContentType = "application/json"
            };

            await sender.SendMessageAsync(message, ct);
        }
        finally
        {
            await sender.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client != null)
            await _client.DisposeAsync();
    }
}
