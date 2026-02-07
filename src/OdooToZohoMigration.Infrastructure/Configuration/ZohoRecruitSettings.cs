namespace OdooToZohoMigration.Infrastructure.Configuration;

public class ZohoRecruitSettings
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string AccountsDomain { get; set; } = "accounts.zoho.com";
    public string ApiDomain { get; set; } = "recruit.zoho.com";
    public int TokenCacheMinutes { get; set; } = 50;
    public int MaxBatchSize { get; set; } = 100;
    public int ApiDelayMs { get; set; } = 1000;
}
