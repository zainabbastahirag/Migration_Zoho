using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using AGONEAIHub.Core.Enums;
using AGONEAIHub.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace AGONEAIHub.Infrastructure.Services.Spot;

/// <summary>
/// Full SPOT report generation orchestrator — converted from Python SPOTAnalysisOrchestrator.
///
/// Pipeline:
///   1. Build document context from extracted layouts (grouped by docType)
///   2. Extract pre-fill numeric anchors (RM patterns) for financial grounding
///   3. Parallel sectional extraction: A1-A3, B1-B3, C1-C3, D1-D3, E1-E3 + exec_summ
///   4. Assemble V12 template structure
///   5. Generate final report (MD + JSON)
///
/// All prompts come from DB (PromptTemplates table, Project=AGONESPot).
/// </summary>
public class SpotReportOrchestrator
{
    private readonly IChatService _chat;
    private readonly ILogger _log;

    // Section → prompt mapping (matches Python SECTION_PROMPT_MAPPING)
    private static readonly Dictionary<string, Dictionary<string, string>> SectionPromptMapping = new()
    {
        ["A"] = new() { ["exec_summ"] = "exec-summ", ["A1"] = "section-a1", ["A2"] = "section-a2", ["A3"] = "section-a3" },
        ["B"] = new() { ["B1"] = "section-b1", ["B2"] = "section-b2", ["B3"] = "section-b3" },
        ["C"] = new() { ["C1"] = "section-c1", ["C2"] = "section-c2", ["C3"] = "section-c3" },
        ["D"] = new() { ["D1"] = "section-d1", ["D2"] = "section-d2", ["D3"] = "section-d3" },
        ["E"] = new() { ["E1"] = "section-e1", ["E2"] = "section-e2", ["E3"] = "section-e3" }
    };

    // Which doc types are relevant per section (matches Python SECTION_SOURCE_MAPPING)
    private static readonly Dictionary<string, string[]> SectionSourceMapping = new()
    {
        ["A1"] = new[] { "SSM", "Audit_Report" },
        ["A2"] = new[] { "Audit_Report" },
        ["A3"] = new[] { "Audit_Report", "CTOS" },
        ["B1"] = new[] { "SSM", "CTOS", "Audit_Report", "Supplementary" },
        ["B2"] = new[] { "SSM", "CTOS", "Audit_Report", "Supplementary" },
        ["B3"] = new[] { "SSM", "CTOS", "Audit_Report", "Supplementary" },
        ["C1"] = new[] { "SSM", "CTOS", "Audit_Report", "Supplementary" },
        ["C2"] = new[] { "SSM", "CTOS", "Audit_Report", "Supplementary" },
        ["C3"] = new[] { "SSM", "CTOS", "Audit_Report", "Supplementary" },
        ["D1"] = new[] { "SSM", "CTOS", "Audit_Report", "Supplementary" },
        ["D2"] = new[] { "SSM", "CTOS", "Audit_Report", "Supplementary" },
        ["D3"] = new[] { "SSM", "CTOS", "Audit_Report", "Supplementary" },
        ["E1"] = new[] { "CTOS" },
        ["E2"] = new[] { "CTOS", "Audit_Report", "Supplementary" },
        ["E3"] = new[] { "CTOS", "Audit_Report", "Supplementary" },
        ["exec_summ"] = new[] { "SSM", "CTOS", "Audit_Report", "Supplementary" }
    };

    private static readonly Regex RmPattern = new(
        @"\(?\s*-?\s*RM\s*[-(]?\s*[\d,]+(?:\.\d+)?\s*\)?\s*\)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public SpotReportOrchestrator(IChatService chat, ILogger log)
    {
        _chat = chat;
        _log = log;
    }

    /// <summary>
    /// Main entry point: generate full SPOT V12 report from extracted document data.
    /// Returns the assembled JSON structure ready for rendering.
    /// </summary>
    public async Task<SpotReportResult> GenerateAsync(
        string companyId,
        List<ExtractedDocument> documents,
        CancellationToken ct = default)
    {
        var result = new SpotReportResult { CompanyId = companyId };
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Build context grouped by doc type
            var fullContext = BuildContext(documents);
            var prefill = ExtractPrefillAnchors(fullContext);

            _log.LogInformation("[SPOT] Starting parallel extraction for {Company} ({Docs} documents, {Len} chars context)",
                companyId, documents.Count, fullContext.Length);

            // Parallel sectional extraction
            var sectionResults = await ExtractAllSectionsAsync(
                companyId, documents, fullContext, prefill, ct);

            // Assemble V12 template
            result.ReportJson = AssembleV12Template(companyId, sectionResults);
            result.ReportMarkdown = RenderMarkdown(companyId, sectionResults);
            result.Success = true;

            sw.Stop();
            result.DurationMs = sw.ElapsedMilliseconds;
            _log.LogInformation("[SPOT] Report generated for {Company} in {Ms}ms ({Sections} sections)",
                companyId, result.DurationMs, sectionResults.Count);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _log.LogError(ex, "[SPOT] Report generation failed for {Company}", companyId);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.DurationMs = sw.ElapsedMilliseconds;
        }

        return result;
    }

    // ================================================================
    //  PARALLEL SECTIONAL EXTRACTION
    // ================================================================

    private async Task<Dictionary<string, string>> ExtractAllSectionsAsync(
        string companyId,
        List<ExtractedDocument> documents,
        string fullContext,
        string prefillAnchors,
        CancellationToken ct)
    {
        var results = new ConcurrentDictionary<string, string>();

        // Build all tasks
        var tasks = new List<(string SectionId, string PromptKey)>();
        foreach (var (group, subsections) in SectionPromptMapping)
            foreach (var (subId, promptKey) in subsections)
                tasks.Add((subId, promptKey));

        // Run with limited parallelism (5 concurrent, matches Python max_workers=5)
        var semaphore = new SemaphoreSlim(5);

        var extractionTasks = tasks.Select(async task =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var sectionContext = BuildSectionContext(task.SectionId, documents, fullContext, prefillAnchors);

                var chatResult = await _chat.ChatWithTemplateAsync(
                    ProjectName.AGONESPot, task.PromptKey,
                    new Dictionary<string, string>
                    {
                        ["companyId"] = companyId,
                        ["context"] = sectionContext.Length > 30000 ? sectionContext[..30000] : sectionContext,
                        ["reportTitle"] = $"SPOT Report - {companyId}",
                        ["auditData"] = sectionContext.Length > 30000 ? sectionContext[..30000] : sectionContext
                    }, ct);

                results[task.SectionId] = chatResult.Success
                    ? chatResult.Response ?? ""
                    : $"[Section {task.SectionId} generation failed: {chatResult.ErrorMessage}]";

                _log.LogInformation("  [{Section}] {Status} ({Tokens} tokens, {Ms}ms)",
                    task.SectionId,
                    chatResult.Success ? "OK" : "FAILED",
                    chatResult.TotalTokens,
                    chatResult.DurationMs);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "  [{Section}] Exception", task.SectionId);
                results[task.SectionId] = $"[Section {task.SectionId} error: {ex.Message}]";
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(extractionTasks);
        return new Dictionary<string, string>(results);
    }

    // ================================================================
    //  CONTEXT BUILDING
    // ================================================================

    private static string BuildContext(List<ExtractedDocument> documents)
    {
        var parts = new List<string>();
        foreach (var doc in documents)
        {
            parts.Add($"=== [{doc.DocType}] {doc.FileName} ({doc.PageCount} pages) ===\n{doc.Text}");
        }
        return string.Join("\n\n---\n\n", parts);
    }

    /// <summary>
    /// Build section-specific context: global overview + filtered docs for this section.
    /// Matches Python's tiered context strategy.
    /// </summary>
    private static string BuildSectionContext(
        string sectionId,
        List<ExtractedDocument> documents,
        string fullContext,
        string prefillAnchors)
    {
        var sb = new System.Text.StringBuilder();

        if (!string.IsNullOrEmpty(prefillAnchors))
            sb.AppendLine(prefillAnchors);

        // Global layer: first 5000 chars for big-picture awareness
        sb.AppendLine("=== GLOBAL COMPANY CONTEXT ===");
        sb.AppendLine(fullContext.Length > 5000 ? fullContext[..5000] : fullContext);
        sb.AppendLine();

        // Local layer: docs filtered by section's relevant doc types
        if (SectionSourceMapping.TryGetValue(sectionId, out var allowedTypes))
        {
            var filtered = documents
                .Where(d => allowedTypes.Any(t =>
                    string.Equals(t, d.DocType, StringComparison.OrdinalIgnoreCase) ||
                    d.DocType.Contains(t, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (filtered.Count > 0)
            {
                sb.AppendLine($"=== SECTION-SPECIFIC DETAILS ({sectionId}) ===");
                foreach (var doc in filtered)
                {
                    var text = doc.Text.Length > 10000 ? doc.Text[..10000] : doc.Text;
                    sb.AppendLine($"[{doc.DocType}] {doc.FileName}:\n{text}\n");
                }
            }
        }
        else
        {
            sb.AppendLine(fullContext.Length > 15000 ? fullContext[..15000] : fullContext);
        }

        return sb.ToString();
    }

    // ================================================================
    //  PREFILL ANCHORS (heuristic RM extraction)
    // ================================================================

    private string ExtractPrefillAnchors(string text)
    {
        var labels = new Dictionary<string, string[]>
        {
            ["total_assets"] = new[] { "total assets", "total assets & liabilities" },
            ["total_equity"] = new[] { "total equity", "total equity & liabilities" },
            ["total_liabilities"] = new[] { "total liabilities" },
            ["revenue"] = new[] { "revenue", "annual revenue", "turnover" },
            ["net_profit"] = new[] { "net profit", "profit after tax", "loss for the year" }
        };

        var found = new Dictionary<string, string>();
        var lower = text.ToLowerInvariant();

        foreach (var (key, searchTerms) in labels)
        {
            foreach (var term in searchTerms)
            {
                var pos = lower.IndexOf(term, StringComparison.Ordinal);
                if (pos < 0) continue;
                var window = text.Substring(pos, Math.Min(100, text.Length - pos));
                var match = RmPattern.Match(window);
                if (match.Success)
                {
                    found[key] = match.Value.Trim();
                    break;
                }
            }
        }

        if (found.Count == 0) return "";

        return "Pre-extracted numeric anchors (heuristic):\n" +
               string.Join("\n", found.Select(kv => $"  {kv.Key}: {kv.Value}")) + "\n\n";
    }

    // ================================================================
    //  V12 TEMPLATE ASSEMBLY
    // ================================================================

    private static string AssembleV12Template(
        string companyId,
        Dictionary<string, string> sectionResults)
    {
        var now = DateTime.UtcNow;
        var template = new Dictionary<string, object>
        {
            ["metadata"] = new
            {
                version = "12",
                document_type = "Single Point of Truth (SPOT)",
                company_id = companyId,
                generated_at = now.ToString("O")
            },
            ["header"] = new
            {
                report_title = "Single Point of Truth (SPOT)",
                subject_company = companyId,
                report_date = $"{now.Day} {now:MMMM yyyy}"
            },
            ["sections"] = new Dictionary<string, object>
            {
                ["A"] = new { section_name = "A. ENTERPRISE & FINANCIAL PROFILE", subsections = ExtractGroup("A", sectionResults) },
                ["B"] = new { section_name = "B. STRATEGIC CAPABILITIES & MARKET POSITION", subsections = ExtractGroup("B", sectionResults) },
                ["C"] = new { section_name = "C. STATEMENT OF CAPABILITIES", subsections = ExtractGroup("C", sectionResults) },
                ["D"] = new { section_name = "D. OWNERSHIP & MANAGEMENT STRUCTURE", subsections = ExtractGroup("D", sectionResults) },
                ["E"] = new { section_name = "E. CREDIT RELATIONSHIP & PROFILE", subsections = ExtractGroup("E", sectionResults) }
            }
        };

        return JsonSerializer.Serialize(template, new JsonSerializerOptions { WriteIndented = true });
    }

    private static Dictionary<string, object> ExtractGroup(string group, Dictionary<string, string> results)
    {
        var subsections = new Dictionary<string, object>();

        if (SectionPromptMapping.TryGetValue(group, out var mapping))
        {
            foreach (var (subId, _) in mapping)
            {
                if (results.TryGetValue(subId, out var content))
                {
                    // Try to parse as JSON, fall back to raw text
                    try
                    {
                        var parsed = JsonSerializer.Deserialize<object>(content);
                        subsections[subId] = parsed!;
                    }
                    catch
                    {
                        subsections[subId] = new { content = content };
                    }
                }
            }
        }

        return subsections;
    }

    // ================================================================
    //  MARKDOWN RENDERER
    // ================================================================

    private static string RenderMarkdown(string companyId, Dictionary<string, string> sectionResults)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Single Point of Truth (SPOT) — {companyId}");
        sb.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC\n");

        var sectionNames = new Dictionary<string, string>
        {
            ["exec_summ"] = "Executive Summary",
            ["A1"] = "A.1 Corporate Identity Snapshot",
            ["A2"] = "A.2 Financial Statements",
            ["A3"] = "A.3 Financial Ratios & Analysis",
            ["B1"] = "B.1 Enterprise Value Proposition",
            ["B2"] = "B.2 Strategic Capabilities & Competitive Edge",
            ["B3"] = "B.3 Industry Benchmark",
            ["C1"] = "C.1 Delivery Competency & Capabilities",
            ["C2"] = "C.2 Accomplishment, Reference & Evidence",
            ["C3"] = "C.3 Accreditation & Certification",
            ["D1"] = "D.1 Ownership & Board Composition",
            ["D2"] = "D.2 Management Structure",
            ["D3"] = "D.3 Ultimate Beneficial Owners",
            ["E1"] = "E.1 Credit Standing",
            ["E2"] = "E.2 Compliance & Regulatory Adherence",
            ["E3"] = "E.3 Governance & Oversight"
        };

        // Executive summary first
        if (sectionResults.TryGetValue("exec_summ", out var execSumm))
        {
            sb.AppendLine("## Executive Summary\n");
            sb.AppendLine(execSumm);
            sb.AppendLine();
        }

        // Then all sections in order
        foreach (var id in new[] { "A1","A2","A3","B1","B2","B3","C1","C2","C3","D1","D2","D3","E1","E2","E3" })
        {
            if (sectionResults.TryGetValue(id, out var content))
            {
                var name = sectionNames.GetValueOrDefault(id, id);
                sb.AppendLine($"## {name}\n");
                sb.AppendLine(content);
                sb.AppendLine();
            }
        }

        sb.AppendLine("---");
        sb.AppendLine("*This report has been generated by AGONE AI Hub for assessment and profiling purposes only.*");

        return sb.ToString();
    }
}

// ================================================================
//  DATA MODELS
// ================================================================

public class ExtractedDocument
{
    public string FileName { get; set; } = string.Empty;
    public string DocType { get; set; } = "Unknown";
    public string Text { get; set; } = string.Empty;
    public int PageCount { get; set; }
}

public class SpotReportResult
{
    public bool Success { get; set; }
    public string CompanyId { get; set; } = string.Empty;
    public string? ReportMarkdown { get; set; }
    public string? ReportJson { get; set; }
    public long DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
}
