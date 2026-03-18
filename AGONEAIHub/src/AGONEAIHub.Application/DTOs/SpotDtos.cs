namespace AGONEAIHub.Application.DTOs;

public class ClassifyFileRequest
{
    public string FileName { get; set; } = string.Empty;
    public string FileContent { get; set; } = string.Empty;
    public string? AdditionalContext { get; set; }
}

public class ClassifyFileResponse
{
    public bool Success { get; set; }
    public string? FileType { get; set; }
    public string? Category { get; set; }
    public string? RiskLevel { get; set; }
    public string? Summary { get; set; }
    public string? CorrelationId { get; set; }
    public long DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
}

public class GenerateReportRequest
{
    public string ReportTitle { get; set; } = string.Empty;
    public Dictionary<string, string> ReportData { get; set; } = new();

    /// <summary>Which sections to generate. Default: all (SectionA, SectionB, SectionC, SectionD).</summary>
    public List<string>? Sections { get; set; }
}

public class GenerateReportResponse
{
    public bool Success { get; set; }
    public string? ReportTitle { get; set; }
    public Dictionary<string, string> SectionResults { get; set; } = new();
    public string? CorrelationId { get; set; }
    public long TotalDurationMs { get; set; }
    public string? ErrorMessage { get; set; }
}
