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
}
