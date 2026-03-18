using AGONEAIHub.Core.Enums;
using AGONEAIHub.Core.Interfaces;

namespace AGONEAIHub.Core.Interfaces;

public interface ISpotService
{
    /// <summary>
    /// Classify a file using AI — determines file type, category, risk level, etc.
    /// Uses prompt: Module=FileClassification, Section=General, PromptKey=classify-file
    /// </summary>
    Task<SpotClassificationResult> ClassifyFileAsync(
        string fileName, string fileContent, string? additionalContext = null,
        CancellationToken ct = default);

    /// <summary>
    /// Generate a Spot audit report with multiple sections.
    /// Each section (A, B, C, D) has its own prompt template in the DB.
    /// Uses prompts: Module=ReportGeneration, Section=SectionA/B/C/D
    /// </summary>
    Task<SpotReportResult> GenerateSpotReportAsync(
        string reportTitle, Dictionary<string, string> reportData,
        List<string>? sections = null,
        CancellationToken ct = default);
}

public class SpotClassificationResult
{
    public bool Success { get; set; }
    public string? FileType { get; set; }
    public string? Category { get; set; }
    public string? RiskLevel { get; set; }
    public string? Summary { get; set; }
    public string? RawResponse { get; set; }
    public string? CorrelationId { get; set; }
    public long DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
}

public class SpotReportResult
{
    public bool Success { get; set; }
    public string? ReportTitle { get; set; }
    public Dictionary<string, string> SectionResults { get; set; } = new();
    public string? CorrelationId { get; set; }
    public long TotalDurationMs { get; set; }
    public string? ErrorMessage { get; set; }
}
