using System.Text.RegularExpressions;
using AGONEAIHub.Core.Entities;
using AGONEAIHub.Core.Enums;
using AGONEAIHub.Core.Interfaces;
using AGONEAIHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AGONEAIHub.Infrastructure.Services;

public class PromptService : IPromptService
{
    private readonly AIHubDbContext _db;

    public PromptService(AIHubDbContext db) => _db = db;

    public async Task<PromptTemplate?> GetTemplateAsync(
        ProjectName project, string module, string section, string promptKey, CancellationToken ct = default) =>
        await _db.PromptTemplates.FirstOrDefaultAsync(p =>
            p.Project == project && p.Module == module && p.Section == section
            && p.PromptKey == promptKey && p.IsActive, ct);

    public async Task<List<PromptTemplate>> GetByModuleAsync(
        ProjectName project, string module, CancellationToken ct = default) =>
        await _db.PromptTemplates
            .Where(p => p.Project == project && p.Module == module && p.IsActive)
            .OrderBy(p => p.Section).ThenBy(p => p.Name)
            .ToListAsync(ct);

    public async Task<List<PromptTemplate>> GetBySectionAsync(
        ProjectName project, string module, string section, CancellationToken ct = default) =>
        await _db.PromptTemplates
            .Where(p => p.Project == project && p.Module == module && p.Section == section && p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

    public async Task<List<PromptTemplate>> GetAllTemplatesAsync(
        ProjectName project, CancellationToken ct = default) =>
        await _db.PromptTemplates
            .Where(p => p.Project == project)
            .OrderBy(p => p.Module).ThenBy(p => p.Section).ThenBy(p => p.Name)
            .ToListAsync(ct);

    public async Task<PromptTemplate> CreateTemplateAsync(PromptTemplate template, CancellationToken ct = default)
    {
        template.CreatedAt = DateTime.UtcNow;
        _db.PromptTemplates.Add(template);
        await _db.SaveChangesAsync(ct);
        return template;
    }

    public async Task<PromptTemplate> UpdateTemplateAsync(PromptTemplate template, CancellationToken ct = default)
    {
        template.UpdatedAt = DateTime.UtcNow;
        _db.PromptTemplates.Update(template);
        await _db.SaveChangesAsync(ct);
        return template;
    }

    public async Task DeleteTemplateAsync(int id, CancellationToken ct = default)
    {
        var t = await _db.PromptTemplates.FindAsync(new object[] { id }, ct);
        if (t != null) { _db.PromptTemplates.Remove(t); await _db.SaveChangesAsync(ct); }
    }

    public string RenderTemplate(string template, Dictionary<string, string> variables)
    {
        if (string.IsNullOrWhiteSpace(template) || variables.Count == 0)
            return template;

        return Regex.Replace(template, @"\{\{(\w+)\}\}", match =>
        {
            var key = match.Groups[1].Value;
            return variables.TryGetValue(key, out var val) ? val : match.Value;
        });
    }
}
