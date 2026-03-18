namespace AGONEAIHub.Core.Entities;

/// <summary>
/// Logs every AI call. Linked to the prompt template that was used.
/// Tracks tokens, duration, success/failure, and full request/response.
/// </summary>
public class PromptExecutionLog : BaseEntity
{
    /// <summary>Correlation ID to link related operations together.</summary>
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>FK to PromptTemplate if a template was used.</summary>
    public int? PromptTemplateId { get; set; }

    public string Module { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty;
    public string PromptKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;

    public string? InputText { get; set; }
    public string? RenderedSystemPrompt { get; set; }
    public string? RenderedUserPrompt { get; set; }
    public string? OutputText { get; set; }
    public string? VariablesJson { get; set; }

    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }
    public long DurationMs { get; set; }

    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorStackTrace { get; set; }

    // Navigation
    public PromptTemplate? PromptTemplate { get; set; }
}
