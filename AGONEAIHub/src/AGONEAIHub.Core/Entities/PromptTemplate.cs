namespace AGONEAIHub.Core.Entities;

/// <summary>
/// Stores reusable prompt templates per project.
/// Each team manages their own prompts identified by Project + PromptKey.
/// </summary>
public class PromptTemplate : BaseEntity
{
    public string PromptKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserPromptTemplate { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o";
    public int MaxTokens { get; set; } = 4096;
    public double Temperature { get; set; } = 0.7;
    public bool IsActive { get; set; } = true;
    public string? Category { get; set; }
}
