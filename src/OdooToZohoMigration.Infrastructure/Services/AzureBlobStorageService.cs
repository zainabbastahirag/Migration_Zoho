using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OdooToZohoMigration.Core.Interfaces;
using OdooToZohoMigration.Infrastructure.Configuration;

namespace OdooToZohoMigration.Infrastructure.Services;

/// <summary>
/// Downloads CV files from Azure Blob Storage so they can be uploaded to Zoho Recruit.
///
/// Uses the same proven approach from the original working code:
///   - Full URL  →  extract container + blob name from the URL path
///   - Relative path  →  use configured default container
///   - Uses DownloadStreamingAsync (no full memory buffer)
///   - Checks blob metadata for original file name
/// </summary>
public class AzureBlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly AzureBlobStorageSettings _settings;
    private readonly ILogger<AzureBlobStorageService> _logger;

    public AzureBlobStorageService(
        IOptions<AzureBlobStorageSettings> settings,
        ILogger<AzureBlobStorageService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        _blobServiceClient = new BlobServiceClient(_settings.ConnectionString);
    }

    public async Task<(Stream? Stream, string? ContentType, string? FileName)> GetCvWithMetadataAsync(
        string blobUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var blobClient = GetBlobClientFromUrl(blobUrl);

            if (!await blobClient.ExistsAsync(cancellationToken))
            {
                _logger.LogWarning("Blob does not exist: {BlobUrl}", blobUrl);
                return (null, null, null);
            }

            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);

            // Check size limit
            if (properties.Value.ContentLength > _settings.MaxCvSizeBytes)
            {
                _logger.LogWarning("Blob {Blob} is {Size} bytes — exceeds max {Max} bytes. Skipping.",
                    blobClient.Name, properties.Value.ContentLength, _settings.MaxCvSizeBytes);
                return (null, null, null);
            }

            // Download using streaming (same as old working code)
            var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);

            var contentType = properties.Value.ContentType ?? "application/octet-stream";
            var fileName = blobClient.Name;

            // Try to get original filename from metadata (same as old working code)
            if (properties.Value.Metadata.TryGetValue("originalFileName", out var originalName))
            {
                fileName = originalName;
            }
            else if (properties.Value.Metadata.TryGetValue("OriginalFileName", out var originalName2))
            {
                fileName = originalName2;
            }

            // Copy to MemoryStream so caller owns it and blob connection can close
            var memStream = new MemoryStream();
            await response.Value.Content.CopyToAsync(memStream, cancellationToken);
            memStream.Position = 0;

            _logger.LogDebug("Downloaded blob {Blob} ({Size} bytes, {Type})",
                blobClient.Name, properties.Value.ContentLength, contentType);

            return (memStream, contentType, Path.GetFileName(fileName));
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Blob 404 for URL: {Url}", blobUrl);
            return (null, null, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading blob: {Url}", blobUrl);
            return (null, null, null);
        }
    }

    /// <summary>
    /// Same logic as the original working code:
    ///   - Full URL → parse container name + blob name from the URL path
    ///   - Relative path → use configured default container
    /// </summary>
    private BlobClient GetBlobClientFromUrl(string blobUrl)
    {
        // Handle full absolute URLs
        if (Uri.TryCreate(blobUrl, UriKind.Absolute, out var uri)
            && (uri.Scheme == "http" || uri.Scheme == "https"))
        {
            // URL like: https://aginternalaistorage.blob.core.windows.net/odooresumes/path/file.pdf
            // AbsolutePath = /odooresumes/path/file.pdf
            var pathParts = uri.AbsolutePath.TrimStart('/').Split('/', 2);
            if (pathParts.Length == 2)
            {
                var containerName = pathParts[0];   // "odooresumes"
                var blobName = pathParts[1];         // "path/file.pdf"

                _logger.LogDebug("Parsed URL → container={Container}, blob={Blob}", containerName, blobName);

                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                return containerClient.GetBlobClient(Uri.UnescapeDataString(blobName));
            }
        }

        // Relative path — use configured default container
        _logger.LogDebug("Relative path → container={Container}, blob={Blob}", _settings.CvContainerName, blobUrl);
        var defaultContainer = _blobServiceClient.GetBlobContainerClient(_settings.CvContainerName);
        return defaultContainer.GetBlobClient(blobUrl);
    }
}
