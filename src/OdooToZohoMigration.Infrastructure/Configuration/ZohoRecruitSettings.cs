namespace OdooToZohoMigration.Infrastructure.Configuration;

public class ZohoRecruitSettings
{
    // ── OAuth ─────────────────────────────────────────────────────────
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string AccountsDomain { get; set; } = "accounts.zoho.com";
    public string ApiDomain { get; set; } = "recruit.zoho.com";
    public int TokenCacheMinutes { get; set; } = 50;

    // ── Batch ─────────────────────────────────────────────────────────
    /// <summary>
    /// Max records per single Zoho API batch insert call.
    /// Zoho hard limit is 100. The MigrationSettings per-entity batch sizes
    /// control how many records are loaded from DB; this controls how they
    /// are chunked for the actual API call if they exceed this number.
    /// </summary>
    public int MaxBatchSize { get; set; } = 100;

    /// <summary>Delay (ms) between sub-batch API calls inside one batch method.</summary>
    public int ApiDelayMs { get; set; } = 1000;

    // ── Rate-limit / 429 handling ─────────────────────────────────────
    /// <summary>How many times to retry when Zoho returns HTTP 429.</summary>
    public int RateLimitMaxRetries { get; set; } = 5;

    /// <summary>
    /// Initial wait (ms) when a 429 is received.
    /// Doubles on each retry (exponential back-off).
    /// Zoho documentation says "wait at least 1 minute" on 429.
    /// </summary>
    public int RateLimitRetryBaseDelayMs { get; set; } = 60_000;
}
