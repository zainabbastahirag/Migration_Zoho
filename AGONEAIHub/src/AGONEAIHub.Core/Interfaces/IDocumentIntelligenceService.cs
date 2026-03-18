using AGONEAIHub.Core.Enums;

namespace AGONEAIHub.Core.Interfaces;

public interface IDocumentIntelligenceService
{
    /// <summary>
    /// Analyze a document from a URL using Azure Document Intelligence.
    /// </summary>
    Task<DocumentResult> AnalyzeFromUrlAsync(
        ProjectName project, string documentUrl, string modelId = "prebuilt-document",
        CancellationToken ct = default);

    /// <summary>
    /// Analyze a document from a stream.
    /// </summary>
    Task<DocumentResult> AnalyzeFromStreamAsync(
        ProjectName project, Stream documentStream, string fileName,
        string modelId = "prebuilt-document",
        CancellationToken ct = default);
}

public class DocumentResult
{
    public bool Success { get; set; }
    public string? ExtractedDataJson { get; set; }
    public string? RawText { get; set; }
    public int PageCount { get; set; }
    public string? ErrorMessage { get; set; }
    public long DurationMs { get; set; }
}
