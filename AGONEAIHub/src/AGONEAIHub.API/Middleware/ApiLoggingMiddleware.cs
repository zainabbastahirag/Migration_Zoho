using System.Diagnostics;
using System.Text;
using AGONEAIHub.Core.Entities;
using AGONEAIHub.Infrastructure.Data;

namespace AGONEAIHub.API.Middleware;

/// <summary>
/// Auto-logs every HTTP request + response to aihub.ApiRequestLogs.
/// No manual code needed in controllers — this captures everything.
/// </summary>
public class ApiLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiLoggingMiddleware> _log;

    public ApiLoggingMiddleware(RequestDelegate next, ILogger<ApiLoggingMiddleware> log)
    {
        _next = next;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/swagger"))
        {
            await _next(context);
            return;
        }

        var sw = Stopwatch.StartNew();
        var correlationId = Guid.NewGuid().ToString("N");
        context.Items["CorrelationId"] = correlationId;

        // Capture request body
        string? requestBody = null;
        if (context.Request.ContentLength > 0 && context.Request.ContentLength < 50_000)
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            requestBody = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;
        }

        // Swap response body to capture it
        var originalBody = context.Response.Body;
        using var responseBuffer = new MemoryStream();
        context.Response.Body = responseBuffer;

        try
        {
            await _next(context);
        }
        finally
        {
            sw.Stop();

            // Read response body
            string? responseBody = null;
            if (responseBuffer.Length < 50_000)
            {
                responseBuffer.Position = 0;
                responseBody = await new StreamReader(responseBuffer).ReadToEndAsync();
            }

            // Copy response back to original stream
            responseBuffer.Position = 0;
            await responseBuffer.CopyToAsync(originalBody);
            context.Response.Body = originalBody;

            // Extract project from request body if present
            string? project = null;
            if (requestBody != null)
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(requestBody);
                    if (doc.RootElement.TryGetProperty("Project", out var p) ||
                        doc.RootElement.TryGetProperty("project", out p))
                    {
                        project = p.ValueKind == System.Text.Json.JsonValueKind.Number
                            ? ((Core.Enums.ProjectName)p.GetInt32()).ToString()
                            : p.GetString();
                    }
                }
                catch { }
            }

            // Save to DB
            try
            {
                using var scope = context.RequestServices.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AIHubDbContext>();

                db.ApiRequestLogs.Add(new ApiRequestLog
                {
                    CorrelationId = correlationId,
                    HttpMethod = context.Request.Method,
                    Path = context.Request.Path,
                    QueryString = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : null,
                    RequestBody = requestBody?.Length > 8000 ? requestBody[..8000] : requestBody,
                    StatusCode = context.Response.StatusCode,
                    ResponseBody = responseBody?.Length > 8000 ? responseBody[..8000] : responseBody,
                    DurationMs = sw.ElapsedMilliseconds,
                    Project = project,
                    UserAgent = context.Request.Headers.UserAgent.ToString(),
                    ClientIp = context.Connection.RemoteIpAddress?.ToString(),
                    CreatedAt = DateTime.UtcNow
                });

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to save API request log");
            }
        }
    }
}
