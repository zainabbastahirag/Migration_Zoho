using AGONEAIHub.Core.Entities;
using AGONEAIHub.Core.Enums;
using AGONEAIHub.Core.Interfaces;
using AGONEAIHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AGONEAIHub.Infrastructure.Services;

public class NotificationService : INotificationService
{
    private readonly AIHubDbContext _db;
    private readonly ILogger<NotificationService> _log;

    public NotificationService(AIHubDbContext db, ILogger<NotificationService> log)
    {
        _db = db;
        _log = log;
    }

    public async Task CreateErrorNotificationAsync(
        ProjectName project, string service, string operation,
        string errorMessage, string? stackTrace = null, string? requestPayload = null,
        string? correlationId = null, CancellationToken ct = default)
    {
        var notification = new ErrorNotification
        {
            Project = project,
            Service = service,
            Operation = operation,
            ErrorMessage = errorMessage,
            ErrorStackTrace = stackTrace?.Length > 4000 ? stackTrace[..4000] : stackTrace,
            RequestPayload = requestPayload?.Length > 4000 ? requestPayload[..4000] : requestPayload,
            CorrelationId = correlationId ?? Guid.NewGuid().ToString("N"),
            Status = "New",
            CreatedAt = DateTime.UtcNow
        };

        _db.ErrorNotifications.Add(notification);
        await _db.SaveChangesAsync(ct);

        _log.LogWarning("Error notification created: [{Project}] {Service}.{Operation} — {Error}",
            project, service, operation, errorMessage);
    }

    public async Task<List<ErrorNotification>> GetUnresolvedAsync(
        ProjectName? project = null, CancellationToken ct = default)
    {
        var q = _db.ErrorNotifications.Where(n => n.Status != "Resolved");
        if (project.HasValue) q = q.Where(n => n.Project == project.Value);
        return await q.OrderByDescending(n => n.CreatedAt).Take(100).ToListAsync(ct);
    }

    public async Task AcknowledgeAsync(int id, string acknowledgedBy, CancellationToken ct = default)
    {
        var n = await _db.ErrorNotifications.FindAsync(new object[] { id }, ct);
        if (n != null)
        {
            n.Status = "Acknowledged";
            n.AcknowledgedAt = DateTime.UtcNow;
            n.AcknowledgedBy = acknowledgedBy;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task ResolveAsync(int id, string resolvedBy, string? notes = null, CancellationToken ct = default)
    {
        var n = await _db.ErrorNotifications.FindAsync(new object[] { id }, ct);
        if (n != null)
        {
            n.Status = "Resolved";
            n.ResolvedAt = DateTime.UtcNow;
            n.ResolvedBy = resolvedBy;
            n.ResolutionNotes = notes;
            await _db.SaveChangesAsync(ct);
        }
    }
}
