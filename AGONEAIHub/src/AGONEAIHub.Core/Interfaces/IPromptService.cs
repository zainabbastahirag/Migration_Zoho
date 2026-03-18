using AGONEAIHub.Core.Entities;
using AGONEAIHub.Core.Enums;

namespace AGONEAIHub.Core.Interfaces;

public interface IPromptService
{
    Task<PromptTemplate?> GetTemplateAsync(ProjectName project, string module, string section, string promptKey, CancellationToken ct = default);
    Task<List<PromptTemplate>> GetByModuleAsync(ProjectName project, string module, CancellationToken ct = default);
    Task<List<PromptTemplate>> GetBySectionAsync(ProjectName project, string module, string section, CancellationToken ct = default);
    Task<List<PromptTemplate>> GetAllTemplatesAsync(ProjectName project, CancellationToken ct = default);
    Task<PromptTemplate> CreateTemplateAsync(PromptTemplate template, CancellationToken ct = default);
    Task<PromptTemplate> UpdateTemplateAsync(PromptTemplate template, CancellationToken ct = default);
    Task DeleteTemplateAsync(int id, CancellationToken ct = default);
    string RenderTemplate(string template, Dictionary<string, string> variables);
}
