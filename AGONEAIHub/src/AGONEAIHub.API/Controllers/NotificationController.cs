using AGONEAIHub.Core.Entities;
using AGONEAIHub.Core.Enums;
using AGONEAIHub.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AGONEAIHub.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Tags("Error Notifications")]
public class NotificationController : ControllerBase
{
    private readonly INotificationService _notifications;

    public NotificationController(INotificationService notifications) => _notifications = notifications;

    /// <summary>Get all unresolved error notifications, optionally filtered by project.</summary>
    [HttpGet("errors")]
    public async Task<ActionResult<List<ErrorNotification>>> GetErrors(
        [FromQuery] ProjectName? project, CancellationToken ct)
    {
        var errors = await _notifications.GetUnresolvedAsync(project, ct);
        return Ok(errors);
    }

    /// <summary>Acknowledge an error (someone is looking at it).</summary>
    [HttpPost("errors/{id}/acknowledge")]
    public async Task<IActionResult> Acknowledge(
        int id, [FromQuery] string acknowledgedBy, CancellationToken ct)
    {
        await _notifications.AcknowledgeAsync(id, acknowledgedBy, ct);
        return Ok(new { message = "Acknowledged" });
    }

    /// <summary>Mark an error as resolved.</summary>
    [HttpPost("errors/{id}/resolve")]
    public async Task<IActionResult> Resolve(
        int id, [FromQuery] string resolvedBy, [FromQuery] string? notes, CancellationToken ct)
    {
        await _notifications.ResolveAsync(id, resolvedBy, notes, ct);
        return Ok(new { message = "Resolved" });
    }
}
