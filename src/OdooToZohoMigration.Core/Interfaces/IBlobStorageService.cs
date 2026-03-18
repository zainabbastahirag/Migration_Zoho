namespace OdooToZohoMigration.Core.Interfaces;

public interface IBlobStorageService
{
    Task<(Stream? Stream, string? ContentType, string? FileName)> GetCvWithMetadataAsync(
        string blobUrl, CancellationToken ct = default);
}
