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
/// The Candidate.ResumeUrl in DB can be:
///   1. A full blob URL:  https://aginternalaistorage.blob.core.windows.net/odooresumes/some/path/resume.pdf
///   2. A relative path:  some/path/resume.pdf
///   3. Just a filename:  resume.pdf
///
/// All three are handled — the service extracts the blob name and downloads from
/// the container configured in AzureBlobStorage:CvContainerName.
/// </summary>
public class AzureBlobStorageService : IBlobStorageService
{
    private readonly BlobContainerClient _container;
    private readonly AzureBlobStorageSettings _settings;
    private readonly ILogger<AzureBlobStorageService> _log;

    public AzureBlobStorageService(
        IOptions<AzureBlobStorageSettings> settings,
        ILogger<AzureBlobStorageService> log)
    {
        _settings = settings.Value;
        _log = log;

        var serviceClient = new BlobServiceClient(_settings.ConnectionString);
        _container = serviceClient.GetBlobContainerClient(_settings.CvContainerName);
    }

    public async Task<(Stream? Stream, string? ContentType, string? FileName)> GetCvWithMetadataAsync(
        string blobUrl, CancellationToken ct = default)
    {
        try
        {
            var blobName = ExtractBlobName(blobUrl);
            if (string.IsNullOrWhiteSpace(blobName))
            {
                _log.LogWarning("Could not extract blob name from URL: {Url}", blobUrl);
                return (null, null, null);
            }

            var blobClient = _container.GetBlobClient(blobName);

            // Check if blob exists
            var exists = await blobClient.ExistsAsync(ct);
            if (!exists.Value)
            {
                _log.LogWarning("Blob not found in container '{Container}': {Blob}", _settings.CvContainerName, blobName);
                return (null, null, null);
            }

            // Get properties to check size and content type
            BlobProperties props = await blobClient.GetPropertiesAsync(cancellationToken: ct);

            if (props.ContentLength > _settings.MaxCvSizeBytes)
            {
                _log.LogWarning("Blob {Blob} is {Size} bytes — exceeds max {Max} bytes. Skipping.",
                    blobName, props.ContentLength, _settings.MaxCvSizeBytes);
                return (null, null, null);
            }

            // Download to a MemoryStream so the caller owns the stream
            var memStream = new MemoryStream();
            await blobClient.DownloadToAsync(memStream, ct);
            memStream.Position = 0;

            var contentType = props.ContentType ?? GuessContentType(blobName);
            var fileName = Path.GetFileName(blobName);

            _log.LogDebug("Downloaded blob {Blob} ({Size} bytes, {Type})",
                blobName, props.ContentLength, contentType);

            return (memStream, contentType, fileName);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            _log.LogWarning("Blob 404 for URL: {Url}", blobUrl);
            return (null, null, null);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error downloading blob: {Url}", blobUrl);
            return (null, null, null);
        }
    }

    /// <summary>
    /// Extracts the blob name from various URL formats:
    ///   - Full URL: https://account.blob.core.windows.net/container/path/file.pdf  →  path/file.pdf
    ///   - Container-relative: /odooresumes/path/file.pdf  →  path/file.pdf
    ///   - Just blob name: path/file.pdf  →  path/file.pdf
    /// </summary>
    private string? ExtractBlobName(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        url = url.Trim();

        // Case 1: Full HTTPS URL
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri(url);
                var path = uri.AbsolutePath.TrimStart('/');

                // path = "container/blob/name.pdf"  →  remove container prefix
                var containerPrefix = _settings.CvContainerName + "/";
                if (path.StartsWith(containerPrefix, StringComparison.OrdinalIgnoreCase))
                    return Uri.UnescapeDataString(path[containerPrefix.Length..]);

                // No container prefix — maybe it's just the blob path
                return Uri.UnescapeDataString(path);
            }
            catch
            {
                return url; // fallback: treat the whole thing as blob name
            }
        }

        // Case 2: Starts with /container/
        var leadingSlash = "/" + _settings.CvContainerName + "/";
        if (url.StartsWith(leadingSlash, StringComparison.OrdinalIgnoreCase))
            return url[leadingSlash.Length..];

        // Case 3: Starts with container/
        var containerSlash = _settings.CvContainerName + "/";
        if (url.StartsWith(containerSlash, StringComparison.OrdinalIgnoreCase))
            return url[containerSlash.Length..];

        // Case 4: Just the blob name / relative path
        return url.TrimStart('/');
    }

    private static string GuessContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName)?.ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".rtf" => "application/rtf",
            ".txt" => "text/plain",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            _ => "application/octet-stream"
        };
    }
}
