using AGONEAIHub.Core.Enums;

namespace AGONEAIHub.Application.DTOs;

public class ChatWithTemplateRequest
{
    public ProjectName Project { get; set; }
    public string PromptKey { get; set; } = string.Empty;
    public Dictionary<string, string> Variables { get; set; } = new();
}

public class ChatDirectRequest
{
    public ProjectName Project { get; set; }
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserMessage { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o";
    public int MaxTokens { get; set; } = 4096;
    public double Temperature { get; set; } = 0.7;
}

public class ChatResponse
{
    public bool Success { get; set; }
    public string? Response { get; set; }
    public int? TotalTokens { get; set; }
    public long DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
}
