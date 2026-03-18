namespace OdooToZohoMigration.Infrastructure.Configuration;

public class AzureBlobStorageSettings
{
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Container that holds the Odoo resume files.</summary>
    public string CvContainerName { get; set; } = "odooresumes";

    /// <summary>Max allowed CV file size in bytes (default 20 MB).</summary>
    public long MaxCvSizeBytes { get; set; } = 20_971_520;
}
