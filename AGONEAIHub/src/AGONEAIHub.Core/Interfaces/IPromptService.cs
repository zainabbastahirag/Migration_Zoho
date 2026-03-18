using AGONEAIHub.Core.Entities;
using AGONEAIHub.Core.Enums;

namespace AGONEAIHub.Core.Interfaces;

public interface IPromptService
{
    Task<PromptTemplate?> GetTemplateAsync(ProjectName project, string promptKey, CancellationToken ct = default);
    Task<List<PromptTemplate>> GetAllTemplatesAsync(ProjectName project, CancellationToken ct = default);
    Task<PromptTemplate> CreateTemplateAsync(PromptTemplate template, CancellationToken ct = default);
    Task<PromptTemplate> UpdateTemplateAsync(PromptTemplate template, CancellationToken ct = default);
    Task DeleteTemplateAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Replaces {{placeholders}} in the template with actual values.
    /// </summary>
    string RenderTemplate(string template, Dictionary<string, string> variables);
}
