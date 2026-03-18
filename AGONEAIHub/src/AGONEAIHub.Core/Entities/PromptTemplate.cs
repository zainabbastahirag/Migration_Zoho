namespace AGONEAIHub.Core.Entities;

/// <summary>
/// Hierarchical prompt template structure:
///   Project  → which team (AGONESPot, AGONELearn, ...)
///   Module   → feature area (FileClassification, ReportGeneration, ...)
///   Section  → sub-area within module (SectionA, SectionB, ...)
///   PromptKey → unique name within project+module+section
///
/// Example for AGONESPot:
///   Project=AGONESPot, Module=FileClassification, Section=General, PromptKey=classify-file
///   Project=AGONESPot, Module=ReportGeneration, Section=SectionA, PromptKey=generate-section-a
///   Project=AGONESPot, Module=ReportGeneration, Section=SectionB, PromptKey=generate-section-b
///   Project=AGONESPot, Module=ReportGeneration, Section=SectionC, PromptKey=generate-section-c
/// </summary>
public class PromptTemplate : BaseEntity
{
    /// <summary>Feature area: FileClassification, ReportGeneration, DataExtraction, etc.</summary>
    public string Module { get; set; } = string.Empty;

    /// <summary>Sub-area: SectionA, SectionB, General, etc.</summary>
    public string Section { get; set; } = "General";

    /// <summary>Unique key within Project+Module+Section.</summary>
    public string PromptKey { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserPromptTemplate { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o";
    public int MaxTokens { get; set; } = 4096;
    public double Temperature { get; set; } = 0.7;
    public bool IsActive { get; set; } = true;

    /// <summary>Version tracking — bump when prompt text changes.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Optional tags for filtering: "classification,spot,file"</summary>
    public string? Tags { get; set; }
}
