namespace AGONEAIHub.Infrastructure.Configuration;

public class SpotSettings
{
    public string BlobConnectionString { get; set; } = string.Empty;
    public string BlobContainer { get; set; } = "agonespot";
    public string DocIntEndpoint { get; set; } = string.Empty;
    public string DocIntKey { get; set; } = string.Empty;
    public string ClassifierId { get; set; } = string.Empty;
    public string CtosLogoModelId { get; set; } = "";
    public string CompanyProfileModelId { get; set; } = "";
    public double FileVerificationThreshold { get; set; } = 0.7;
    public long MaxFileSizeBytes { get; set; } = 52_428_800;

    // ── Azure Service Bus ─────────────────────────────────────────
    /// <summary>Azure Service Bus connection string. Leave empty to skip queue (dev mode).</summary>
    public string ServiceBusConnectionString { get; set; } = "";

    /// <summary>Queue name for classify jobs.</summary>
    public string ClassifyQueueName { get; set; } = "spot-classify-queue";

    /// <summary>Queue name for report generation jobs.</summary>
    public string ReportQueueName { get; set; } = "spot-report-queue";

    /// <summary>Set to false to skip Service Bus and call workers directly (dev/local).</summary>
    public bool UseServiceBus { get; set; } = false;
}
