using AGONEAIHub.Application.DTOs;
using AGONEAIHub.Core.Entities;
using AGONEAIHub.Core.Enums;
using AGONEAIHub.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AGONEAIHub.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Tags("Prompt Management")]
public class PromptController : ControllerBase
{
    private readonly IPromptService _prompts;

    public PromptController(IPromptService prompts) => _prompts = prompts;

    /// <summary>
    /// Get all prompt templates for a project.
    /// </summary>
    [HttpGet("{project}")]
    public async Task<ActionResult<List<PromptTemplateDto>>> GetAll(
        ProjectName project, CancellationToken ct)
    {
        var templates = await _prompts.GetAllTemplatesAsync(project, ct);
        return Ok(templates.Select(MapToDto));
    }

    /// <summary>
    /// Get a specific prompt template by project + key.
    /// </summary>
    [HttpGet("{project}/{promptKey}")]
    public async Task<ActionResult<PromptTemplateDto>> Get(
        ProjectName project, string promptKey, CancellationToken ct)
    {
        var template = await _prompts.GetTemplateAsync(project, promptKey, ct);
        if (template == null) return NotFound();
        return Ok(MapToDto(template));
    }

    /// <summary>
    /// Create a new prompt template.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<PromptTemplateDto>> Create(
        [FromBody] CreatePromptRequest request, CancellationToken ct)
    {
        var template = new PromptTemplate
        {
            Project = request.Project,
            PromptKey = request.PromptKey,
            Name = request.Name,
            Description = request.Description,
            SystemPrompt = request.SystemPrompt,
            UserPromptTemplate = request.UserPromptTemplate,
            Model = request.Model,
            MaxTokens = request.MaxTokens,
            Temperature = request.Temperature,
            Category = request.Category
        };

        var created = await _prompts.CreateTemplateAsync(template, ct);
        return CreatedAtAction(nameof(Get),
            new { project = created.Project, promptKey = created.PromptKey },
            MapToDto(created));
    }

    /// <summary>
    /// Update an existing prompt template.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<PromptTemplateDto>> Update(
        int id, [FromBody] UpdatePromptRequest request, CancellationToken ct)
    {
        var existing = await _prompts.GetTemplateAsync(
            default, string.Empty, ct);

        // Re-fetch by ID for updates
        var template = await _prompts.GetAllTemplatesAsync(default, ct);
        var target = template.FirstOrDefault(t => t.Id == id);
        if (target == null) return NotFound();

        if (request.Name != null) target.Name = request.Name;
        if (request.Description != null) target.Description = request.Description;
        if (request.SystemPrompt != null) target.SystemPrompt = request.SystemPrompt;
        if (request.UserPromptTemplate != null) target.UserPromptTemplate = request.UserPromptTemplate;
        if (request.Model != null) target.Model = request.Model;
        if (request.MaxTokens.HasValue) target.MaxTokens = request.MaxTokens.Value;
        if (request.Temperature.HasValue) target.Temperature = request.Temperature.Value;
        if (request.IsActive.HasValue) target.IsActive = request.IsActive.Value;
        if (request.Category != null) target.Category = request.Category;

        var updated = await _prompts.UpdateTemplateAsync(target, ct);
        return Ok(MapToDto(updated));
    }

    /// <summary>
    /// Delete a prompt template.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _prompts.DeleteTemplateAsync(id, ct);
        return NoContent();
    }

    private static PromptTemplateDto MapToDto(PromptTemplate t) => new()
    {
        Id = t.Id,
        Project = t.Project,
        PromptKey = t.PromptKey,
        Name = t.Name,
        Description = t.Description,
        SystemPrompt = t.SystemPrompt,
        UserPromptTemplate = t.UserPromptTemplate,
        Model = t.Model,
        MaxTokens = t.MaxTokens,
        Temperature = t.Temperature,
        IsActive = t.IsActive,
        Category = t.Category
    };
}
