namespace AGONEAIHub.Core.Entities;

/// <summary>
/// Logs every AI call for auditing, debugging, and cost tracking per project.
/// </summary>
public class PromptExecutionLog : BaseEntity
{
    public string PromptKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string? InputText { get; set; }
    public string? OutputText { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }
    public long DurationMs { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
