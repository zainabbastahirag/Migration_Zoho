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

    public async Task<PromptTemplate?> GetTemplateAsync(ProjectName project, string promptKey, CancellationToken ct = default) =>
        await _db.PromptTemplates.FirstOrDefaultAsync(p =>
            p.Project == project && p.PromptKey == promptKey && p.IsActive, ct);

    public async Task<List<PromptTemplate>> GetAllTemplatesAsync(ProjectName project, CancellationToken ct = default) =>
        await _db.PromptTemplates.Where(p => p.Project == project).OrderBy(p => p.Category).ThenBy(p => p.Name).ToListAsync(ct);

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
        var template = await _db.PromptTemplates.FindAsync(new object[] { id }, ct);
        if (template != null)
        {
            _db.PromptTemplates.Remove(template);
            await _db.SaveChangesAsync(ct);
        }
    }

    public string RenderTemplate(string template, Dictionary<string, string> variables)
    {
        if (string.IsNullOrWhiteSpace(template) || variables.Count == 0)
            return template;

        return Regex.Replace(template, @"\{\{(\w+)\}\}", match =>
        {
            var key = match.Groups[1].Value;
            return variables.TryGetValue(key, out var value) ? value : match.Value;
        });
    }
}
