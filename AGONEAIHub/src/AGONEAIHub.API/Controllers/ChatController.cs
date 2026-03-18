using AGONEAIHub.Application.DTOs;
using AGONEAIHub.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AGONEAIHub.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Tags("Chat - OpenAI")]
public class ChatController : ControllerBase
{
    private readonly IChatService _chat;

    public ChatController(IChatService chat) => _chat = chat;

    /// <summary>
    /// Chat using a stored prompt template. Pass project + promptKey + variables.
    /// The template is loaded from DB, variables are injected, and sent to OpenAI.
    /// </summary>
    [HttpPost("template")]
    public async Task<ActionResult<ChatResponse>> ChatWithTemplate(
        [FromBody] ChatWithTemplateRequest request, CancellationToken ct)
    {
        var result = await _chat.ChatWithTemplateAsync(
            request.Project, request.PromptKey, request.Variables, ct);

        return Ok(new ChatResponse
        {
            Success = result.Success,
            Response = result.Response,
            TotalTokens = result.TotalTokens,
            DurationMs = result.DurationMs,
            ErrorMessage = result.ErrorMessage
        });
    }

    /// <summary>
    /// Direct chat — pass system prompt and user message directly (no template).
    /// </summary>
    [HttpPost("direct")]
    public async Task<ActionResult<ChatResponse>> ChatDirect(
        [FromBody] ChatDirectRequest request, CancellationToken ct)
    {
        var result = await _chat.ChatAsync(
            request.Project, request.SystemPrompt, request.UserMessage,
            request.Model, request.MaxTokens, request.Temperature, ct);

        return Ok(new ChatResponse
        {
            Success = result.Success,
            Response = result.Response,
            TotalTokens = result.TotalTokens,
            DurationMs = result.DurationMs,
            ErrorMessage = result.ErrorMessage
        });
    }
}
