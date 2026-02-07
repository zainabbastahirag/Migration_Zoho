namespace OdooToZohoMigration.Infrastructure.Configuration;

public class MigrationSettings
{
    public int DelayBetweenBatchesMs { get; set; } = 2000;
    public int DefaultBatchSize { get; set; } = 50;
    public int MaxConcurrentCvUploads { get; set; } = 3;
    public int MaxRetryCount { get; set; } = 3;
}
