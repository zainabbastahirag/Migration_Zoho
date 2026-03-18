using System.Diagnostics;
using System.Text.Json;
using AGONEAIHub.Core.Enums;
using AGONEAIHub.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace AGONEAIHub.Infrastructure.Services;

/// <summary>
/// AGONESPot-specific AI functions built on top of the generic ChatService.
/// All prompts come from DB (PromptTemplates table, Project=AGONESPot).
/// </summary>
public class SpotService : ISpotService
{
    private const ProjectName Project = ProjectName.AGONESPot;
    private readonly IChatService _chat;
    private readonly INotificationService _notifications;
    private readonly ILogger<SpotService> _log;

    public SpotService(IChatService chat, INotificationService notifications, ILogger<SpotService> log)
    {
        _chat = chat;
        _notifications = notifications;
        _log = log;
    }

    public async Task<SpotClassificationResult> ClassifyFileAsync(
        string fileName, string fileContent, string? additionalContext = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var correlationId = Guid.NewGuid().ToString("N");

        try
        {
            var variables = new Dictionary<string, string>
            {
                ["fileName"] = fileName,
                ["fileContent"] = fileContent.Length > 8000 ? fileContent[..8000] : fileContent,
                ["additionalContext"] = additionalContext ?? ""
            };

            var result = await _chat.ChatWithTemplateAsync(
                Project, "classify-file", variables, ct);

            sw.Stop();

            if (!result.Success)
            {
                await _notifications.CreateErrorNotificationAsync(
                    Project, "SpotService", "ClassifyFile",
                    result.ErrorMessage ?? "Unknown error", null, fileName, correlationId, ct);

                return new SpotClassificationResult
                {
                    Success = false, ErrorMessage = result.ErrorMessage,
                    CorrelationId = correlationId, DurationMs = sw.ElapsedMilliseconds
                };
            }

            // Parse the JSON response
            var parsed = TryParseClassification(result.Response);
            parsed.Success = true;
            parsed.RawResponse = result.Response;
            parsed.CorrelationId = correlationId;
            parsed.DurationMs = sw.ElapsedMilliseconds;
            return parsed;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _log.LogError(ex, "ClassifyFile failed for {FileName}", fileName);

            await _notifications.CreateErrorNotificationAsync(
                Project, "SpotService", "ClassifyFile",
                ex.Message, ex.StackTrace, fileName, correlationId, ct);

            return new SpotClassificationResult
            {
                Success = false, ErrorMessage = ex.Message,
                CorrelationId = correlationId, DurationMs = sw.ElapsedMilliseconds
            };
        }
    }

    public async Task<SpotReportResult> GenerateSpotReportAsync(
        string reportTitle, Dictionary<string, string> reportData,
        List<string>? sections = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var correlationId = Guid.NewGuid().ToString("N");
        var targetSections = sections ?? new List<string> { "SectionA", "SectionB", "SectionC", "SectionD" };
        var sectionResults = new Dictionary<string, string>();

        try
        {
            var auditDataJson = JsonSerializer.Serialize(reportData);

            // Generate each section sequentially using its own prompt template
            foreach (var section in targetSections)
            {
                var promptKey = $"generate-{section.ToLowerInvariant().Replace("section", "section-")}";
                // Normalize: "SectionA" → "generate-section-a"
                promptKey = section.ToLowerInvariant() switch
                {
                    "sectiona" => "generate-section-a",
                    "sectionb" => "generate-section-b",
                    "sectionc" => "generate-section-c",
                    "sectiond" => "generate-section-d",
                    _ => $"generate-{section.ToLowerInvariant()}"
                };

                var variables = new Dictionary<string, string>
                {
                    ["reportTitle"] = reportTitle,
                    ["auditData"] = auditDataJson
                };

                _log.LogInformation("Generating {Section} for report '{Title}'...", section, reportTitle);

                var result = await _chat.ChatWithTemplateAsync(
                    Project, promptKey, variables, ct);

                if (result.Success)
                {
                    sectionResults[section] = result.Response ?? "";
                    _log.LogInformation("  {Section} done ({Tokens} tokens, {Ms}ms)",
                        section, result.TotalTokens, result.DurationMs);
                }
                else
                {
                    sectionResults[section] = $"[ERROR: {result.ErrorMessage}]";

                    await _notifications.CreateErrorNotificationAsync(
                        Project, "SpotService", $"GenerateReport.{section}",
                        result.ErrorMessage ?? "Unknown error", null,
                        $"Report: {reportTitle}", correlationId, ct);
                }
            }

            sw.Stop();
            return new SpotReportResult
            {
                Success = sectionResults.Values.All(v => !v.StartsWith("[ERROR")),
                ReportTitle = reportTitle,
                SectionResults = sectionResults,
                CorrelationId = correlationId,
                TotalDurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _log.LogError(ex, "GenerateSpotReport failed for '{Title}'", reportTitle);

            await _notifications.CreateErrorNotificationAsync(
                Project, "SpotService", "GenerateSpotReport",
                ex.Message, ex.StackTrace, reportTitle, correlationId, ct);

            return new SpotReportResult
            {
                Success = false, ErrorMessage = ex.Message,
                ReportTitle = reportTitle,
                SectionResults = sectionResults,
                CorrelationId = correlationId,
                TotalDurationMs = sw.ElapsedMilliseconds
            };
        }
    }

    private static SpotClassificationResult TryParseClassification(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new SpotClassificationResult();

        try
        {
            // Try to extract JSON from the response (might have markdown code fences)
            var cleaned = json.Trim();
            if (cleaned.StartsWith("```")) cleaned = cleaned.Split('\n', 2).Last();
            if (cleaned.EndsWith("```")) cleaned = cleaned[..cleaned.LastIndexOf("```")];
            cleaned = cleaned.Trim();

            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;
            return new SpotClassificationResult
            {
                FileType = root.TryGetProperty("fileType", out var ft) ? ft.GetString() : null,
                Category = root.TryGetProperty("category", out var cat) ? cat.GetString() : null,
                RiskLevel = root.TryGetProperty("riskLevel", out var rl) ? rl.GetString() : null,
                Summary = root.TryGetProperty("summary", out var s) ? s.GetString() : null
            };
        }
        catch
        {
            return new SpotClassificationResult { Summary = json };
        }
    }
}
