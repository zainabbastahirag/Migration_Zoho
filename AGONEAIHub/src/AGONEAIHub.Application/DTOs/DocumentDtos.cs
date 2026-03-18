using AGONEAIHub.Core.Enums;

namespace AGONEAIHub.Application.DTOs;

public class DocumentAnalyzeUrlRequest
{
    public ProjectName Project { get; set; }
    public string DocumentUrl { get; set; } = string.Empty;
    public string ModelId { get; set; } = "prebuilt-document";
}

public class DocumentAnalyzeResponse
{
    public bool Success { get; set; }
    public int JobId { get; set; }
    public string? ExtractedDataJson { get; set; }
    public string? RawText { get; set; }
    public int PageCount { get; set; }
    public long DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
}
