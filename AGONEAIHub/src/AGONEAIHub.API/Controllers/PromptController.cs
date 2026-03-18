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

    /// <summary>Get all prompts for a project, grouped by Module → Section.</summary>
    [HttpGet("{project}")]
    public async Task<ActionResult<List<PromptTemplateDto>>> GetAll(
        ProjectName project, CancellationToken ct)
    {
        var templates = await _prompts.GetAllTemplatesAsync(project, ct);
        return Ok(templates.Select(MapToDto));
    }

    /// <summary>Get all prompts for a project + module (e.g. FileClassification).</summary>
    [HttpGet("{project}/{module}")]
    public async Task<ActionResult<List<PromptTemplateDto>>> GetByModule(
        ProjectName project, string module, CancellationToken ct)
    {
        var templates = await _prompts.GetByModuleAsync(project, module, ct);
        return Ok(templates.Select(MapToDto));
    }

    /// <summary>Get all prompts for a project + module + section (e.g. ReportGeneration/SectionA).</summary>
    [HttpGet("{project}/{module}/{section}")]
    public async Task<ActionResult<List<PromptTemplateDto>>> GetBySection(
        ProjectName project, string module, string section, CancellationToken ct)
    {
        var templates = await _prompts.GetBySectionAsync(project, module, section, ct);
        return Ok(templates.Select(MapToDto));
    }

    /// <summary>Get one specific prompt by project + module + section + key.</summary>
    [HttpGet("{project}/{module}/{section}/{promptKey}")]
    public async Task<ActionResult<PromptTemplateDto>> GetOne(
        ProjectName project, string module, string section, string promptKey, CancellationToken ct)
    {
        var template = await _prompts.GetTemplateAsync(project, module, section, promptKey, ct);
        if (template == null) return NotFound();
        return Ok(MapToDto(template));
    }

    /// <summary>Create a new prompt template.</summary>
    [HttpPost]
    public async Task<ActionResult<PromptTemplateDto>> Create(
        [FromBody] CreatePromptRequest req, CancellationToken ct)
    {
        var template = new PromptTemplate
        {
            Project = req.Project,
            Module = req.Module,
            Section = req.Section,
            PromptKey = req.PromptKey,
            Name = req.Name,
            Description = req.Description,
            SystemPrompt = req.SystemPrompt,
            UserPromptTemplate = req.UserPromptTemplate,
            Model = req.Model,
            MaxTokens = req.MaxTokens,
            Temperature = req.Temperature,
            Tags = req.Tags
        };

        var created = await _prompts.CreateTemplateAsync(template, ct);
        return CreatedAtAction(nameof(GetOne),
            new { project = created.Project, module = created.Module, section = created.Section, promptKey = created.PromptKey },
            MapToDto(created));
    }

    /// <summary>Update an existing prompt template by ID.</summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<PromptTemplateDto>> Update(
        int id, [FromBody] UpdatePromptRequest req, CancellationToken ct)
    {
        var all = await _prompts.GetAllTemplatesAsync(default, ct);
        var target = all.FirstOrDefault(t => t.Id == id);
        if (target == null) return NotFound();

        if (req.Name != null) target.Name = req.Name;
        if (req.Description != null) target.Description = req.Description;
        if (req.SystemPrompt != null) { target.SystemPrompt = req.SystemPrompt; target.Version++; }
        if (req.UserPromptTemplate != null) { target.UserPromptTemplate = req.UserPromptTemplate; target.Version++; }
        if (req.Model != null) target.Model = req.Model;
        if (req.MaxTokens.HasValue) target.MaxTokens = req.MaxTokens.Value;
        if (req.Temperature.HasValue) target.Temperature = req.Temperature.Value;
        if (req.IsActive.HasValue) target.IsActive = req.IsActive.Value;
        if (req.Tags != null) target.Tags = req.Tags;

        var updated = await _prompts.UpdateTemplateAsync(target, ct);
        return Ok(MapToDto(updated));
    }

    /// <summary>Delete a prompt template.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _prompts.DeleteTemplateAsync(id, ct);
        return NoContent();
    }

    private static PromptTemplateDto MapToDto(PromptTemplate t) => new()
    {
        Id = t.Id, Project = t.Project, Module = t.Module, Section = t.Section,
        PromptKey = t.PromptKey, Name = t.Name, Description = t.Description,
        SystemPrompt = t.SystemPrompt, UserPromptTemplate = t.UserPromptTemplate,
        Model = t.Model, MaxTokens = t.MaxTokens, Temperature = t.Temperature,
        IsActive = t.IsActive, Version = t.Version, Tags = t.Tags
    };
}
