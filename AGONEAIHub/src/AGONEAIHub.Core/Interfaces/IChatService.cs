using AGONEAIHub.Core.Enums;

namespace AGONEAIHub.Core.Interfaces;

public interface IChatService
{
    /// <summary>
    /// Send a chat completion request to OpenAI using a stored prompt template.
    /// </summary>
    Task<ChatResult> ChatWithTemplateAsync(
        ProjectName project, string promptKey,
        Dictionary<string, string> variables,
        CancellationToken ct = default);

    /// <summary>
    /// Send a raw chat completion request (no template, direct system + user prompt).
    /// </summary>
    Task<ChatResult> ChatAsync(
        ProjectName project, string systemPrompt, string userMessage,
        string model = "gpt-4o", int maxTokens = 4096, double temperature = 0.7,
        CancellationToken ct = default);
}

public class ChatResult
{
    public bool Success { get; set; }
    public string? Response { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }
    public long DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
}
