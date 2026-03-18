using System.ClientModel;
using System.Diagnostics;
using System.Text.Json;
using AGONEAIHub.Core.Entities;
using AGONEAIHub.Core.Enums;
using AGONEAIHub.Core.Interfaces;
using AGONEAIHub.Infrastructure.Configuration;
using AGONEAIHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace AGONEAIHub.Infrastructure.Services;

public class OpenAIChatService : IChatService
{
    private readonly ChatClient _chatClient;
    private readonly OpenAISettings _settings;
    private readonly IPromptService _promptService;
    private readonly AIHubDbContext _db;
    private readonly ILogger<OpenAIChatService> _log;

    public OpenAIChatService(
        IOptions<OpenAISettings> settings,
        IPromptService promptService,
        AIHubDbContext db,
        ILogger<OpenAIChatService> log)
    {
        _settings = settings.Value;
        _promptService = promptService;
        _db = db;
        _log = log;

        if (_settings.UseAzure)
        {
            var azureClient = new Azure.AI.OpenAI.AzureOpenAIClient(
                new Uri(_settings.Endpoint),
                new ApiKeyCredential(_settings.ApiKey));
            _chatClient = azureClient.GetChatClient(_settings.DefaultModel);
        }
        else
        {
            var openAiClient = new OpenAI.OpenAIClient(
                new ApiKeyCredential(_settings.ApiKey));
            _chatClient = openAiClient.GetChatClient(_settings.DefaultModel);
        }
    }

    /// <summary>
    /// Chat using a stored prompt template.
    /// Looks up by Project + PromptKey across all modules/sections.
    /// </summary>
    public async Task<ChatResult> ChatWithTemplateAsync(
        ProjectName project, string promptKey,
        Dictionary<string, string> variables,
        CancellationToken ct = default)
    {
        // Find template by promptKey (search across all modules/sections for this project)
        var template = await _db.PromptTemplates
            .Where(p => p.Project == project && p.PromptKey == promptKey && p.IsActive)
            .OrderByDescending(p => p.Version)
            .FirstOrDefaultAsync(ct);

        if (template == null)
            return new ChatResult { Success = false, ErrorMessage = $"Prompt template '{promptKey}' not found for project {project}" };

        var systemPrompt = _promptService.RenderTemplate(template.SystemPrompt, variables);
        var userMessage = _promptService.RenderTemplate(template.UserPromptTemplate, variables);

        return await ExecuteChatAsync(project, template, systemPrompt, userMessage, variables, ct);
    }

    public async Task<ChatResult> ChatAsync(
        ProjectName project, string systemPrompt, string userMessage,
        string model = "gpt-4o", int maxTokens = 4096, double temperature = 0.7,
        CancellationToken ct = default)
    {
        return await ExecuteChatAsync(project, null, systemPrompt, userMessage, null, ct,
            model, maxTokens, temperature, "direct");
    }

    private async Task<ChatResult> ExecuteChatAsync(
        ProjectName project, PromptTemplate? template,
        string systemPrompt, string userMessage,
        Dictionary<string, string>? variables, CancellationToken ct,
        string? modelOverride = null, int? maxTokensOverride = null,
        double? temperatureOverride = null, string? promptKeyOverride = null)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var model = modelOverride ?? template?.Model ?? _settings.DefaultModel;
        var maxTokens = maxTokensOverride ?? template?.MaxTokens ?? 4096;
        var temperature = temperatureOverride ?? template?.Temperature ?? 0.7;
        var promptKey = promptKeyOverride ?? template?.PromptKey ?? "direct";
        var module = template?.Module ?? "Direct";
        var section = template?.Section ?? "General";

        var sw = Stopwatch.StartNew();
        try
        {
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(userMessage)
            };

            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = maxTokens,
                Temperature = (float)temperature,
            };

            var completion = await _chatClient.CompleteChatAsync(messages, options, ct);
            sw.Stop();

            var result = new ChatResult
            {
                Success = true,
                Response = completion.Value.Content[0].Text,
                PromptTokens = completion.Value.Usage.InputTokenCount,
                CompletionTokens = completion.Value.Usage.OutputTokenCount,
                TotalTokens = completion.Value.Usage.TotalTokenCount,
                DurationMs = sw.ElapsedMilliseconds
            };

            await LogExecutionAsync(project, correlationId, template, module, section, promptKey,
                model, systemPrompt, userMessage, variables, result, ct);

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _log.LogError(ex, "OpenAI chat failed: {Project}/{Module}/{Section}/{Key}",
                project, module, section, promptKey);

            var result = new ChatResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                DurationMs = sw.ElapsedMilliseconds
            };

            await LogExecutionAsync(project, correlationId, template, module, section, promptKey,
                model, systemPrompt, userMessage, variables, result, ct);

            return result;
        }
    }

    private async Task LogExecutionAsync(
        ProjectName project, string correlationId, PromptTemplate? template,
        string module, string section, string promptKey, string model,
        string? systemPrompt, string? userPrompt,
        Dictionary<string, string>? variables, ChatResult result, CancellationToken ct)
    {
        _db.PromptExecutionLogs.Add(new PromptExecutionLog
        {
            Project = project,
            CorrelationId = correlationId,
            PromptTemplateId = template?.Id,
            Module = module,
            Section = section,
            PromptKey = promptKey,
            Model = model,
            RenderedSystemPrompt = Truncate(systemPrompt, 4000),
            RenderedUserPrompt = Truncate(userPrompt, 4000),
            OutputText = Truncate(result.Response, 8000),
            VariablesJson = variables != null ? Truncate(JsonSerializer.Serialize(variables), 4000) : null,
            PromptTokens = result.PromptTokens,
            CompletionTokens = result.CompletionTokens,
            TotalTokens = result.TotalTokens,
            DurationMs = result.DurationMs,
            Success = result.Success,
            ErrorMessage = Truncate(result.ErrorMessage, 2000),
            CreatedAt = DateTime.UtcNow
        });

        try { await _db.SaveChangesAsync(ct); }
        catch (Exception ex) { _log.LogError(ex, "Failed to save execution log"); }
    }

    private static string? Truncate(string? s, int max) =>
        s != null && s.Length > max ? s[..max] : s;
}
