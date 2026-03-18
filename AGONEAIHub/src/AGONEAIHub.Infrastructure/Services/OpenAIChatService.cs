using System.ClientModel;
using System.Diagnostics;
using AGONEAIHub.Core.Entities;
using AGONEAIHub.Core.Enums;
using AGONEAIHub.Core.Interfaces;
using AGONEAIHub.Infrastructure.Configuration;
using AGONEAIHub.Infrastructure.Data;
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

    public async Task<ChatResult> ChatWithTemplateAsync(
        ProjectName project, string promptKey,
        Dictionary<string, string> variables,
        CancellationToken ct = default)
    {
        var template = await _promptService.GetTemplateAsync(project, promptKey, ct);
        if (template == null)
            return new ChatResult { Success = false, ErrorMessage = $"Prompt template '{promptKey}' not found for project {project}" };

        var systemPrompt = _promptService.RenderTemplate(template.SystemPrompt, variables);
        var userMessage = _promptService.RenderTemplate(template.UserPromptTemplate, variables);

        return await ChatAsync(project, systemPrompt, userMessage,
            template.Model, template.MaxTokens, template.Temperature, ct, promptKey);
    }

    public async Task<ChatResult> ChatAsync(
        ProjectName project, string systemPrompt, string userMessage,
        string model = "gpt-4o", int maxTokens = 4096, double temperature = 0.7,
        CancellationToken ct = default)
    {
        return await ChatAsync(project, systemPrompt, userMessage, model, maxTokens, temperature, ct, "direct");
    }

    private async Task<ChatResult> ChatAsync(
        ProjectName project, string systemPrompt, string userMessage,
        string model, int maxTokens, double temperature,
        CancellationToken ct, string promptKey)
    {
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

            await LogExecutionAsync(project, promptKey, model, userMessage, result, ct);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _log.LogError(ex, "OpenAI chat failed for {Project}/{PromptKey}", project, promptKey);
            var result = new ChatResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                DurationMs = sw.ElapsedMilliseconds
            };
            await LogExecutionAsync(project, promptKey, model, userMessage, result, ct);
            return result;
        }
    }

    private async Task LogExecutionAsync(
        ProjectName project, string promptKey, string model,
        string? input, ChatResult result, CancellationToken ct)
    {
        _db.PromptExecutionLogs.Add(new PromptExecutionLog
        {
            Project = project,
            PromptKey = promptKey,
            Model = model,
            InputText = input?.Length > 4000 ? input[..4000] : input,
            OutputText = result.Response?.Length > 4000 ? result.Response[..4000] : result.Response,
            PromptTokens = result.PromptTokens,
            CompletionTokens = result.CompletionTokens,
            TotalTokens = result.TotalTokens,
            DurationMs = result.DurationMs,
            Success = result.Success,
            ErrorMessage = result.ErrorMessage,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
    }
}
