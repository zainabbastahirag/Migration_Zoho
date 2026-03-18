using AGONEAIHub.Core.Enums;

namespace AGONEAIHub.Application.DTOs;

public class PromptTemplateDto
{
    public int Id { get; set; }
    public ProjectName Project { get; set; }
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

public class CreatePromptRequest
{
    public ProjectName Project { get; set; }
    public string PromptKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserPromptTemplate { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o";
    public int MaxTokens { get; set; } = 4096;
    public double Temperature { get; set; } = 0.7;
    public string? Category { get; set; }
}

public class UpdatePromptRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? SystemPrompt { get; set; }
    public string? UserPromptTemplate { get; set; }
    public string? Model { get; set; }
    public int? MaxTokens { get; set; }
    public double? Temperature { get; set; }
    public bool? IsActive { get; set; }
    public string? Category { get; set; }
}
