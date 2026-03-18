using AGONEAIHub.Core.Entities;
using AGONEAIHub.Core.Enums;

namespace AGONEAIHub.Core.Interfaces;

public interface INotificationService
{
    Task CreateErrorNotificationAsync(
        ProjectName project, string service, string operation,
        string errorMessage, string? stackTrace = null, string? requestPayload = null,
        string? correlationId = null, CancellationToken ct = default);

    Task<List<ErrorNotification>> GetUnresolvedAsync(ProjectName? project = null, CancellationToken ct = default);
    Task AcknowledgeAsync(int id, string acknowledgedBy, CancellationToken ct = default);
    Task ResolveAsync(int id, string resolvedBy, string? notes = null, CancellationToken ct = default);
}
