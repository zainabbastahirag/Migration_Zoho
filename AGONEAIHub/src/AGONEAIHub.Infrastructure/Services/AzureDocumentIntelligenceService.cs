using System.Diagnostics;
using System.Text.Json;
using AGONEAIHub.Core.Entities;
using AGONEAIHub.Core.Enums;
using AGONEAIHub.Core.Interfaces;
using AGONEAIHub.Infrastructure.Configuration;
using AGONEAIHub.Infrastructure.Data;
using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AGONEAIHub.Infrastructure.Services;

public class AzureDocumentIntelligenceService : IDocumentIntelligenceService
{
    private readonly DocumentAnalysisClient _client;
    private readonly AIHubDbContext _db;
    private readonly ILogger<AzureDocumentIntelligenceService> _log;

    public AzureDocumentIntelligenceService(
        IOptions<AzureDocIntelligenceSettings> settings,
        AIHubDbContext db,
        ILogger<AzureDocumentIntelligenceService> log)
    {
        _db = db;
        _log = log;
        var cfg = settings.Value;
        _client = new DocumentAnalysisClient(new Uri(cfg.Endpoint), new AzureKeyCredential(cfg.ApiKey));
    }

    public async Task<DocumentResult> AnalyzeFromUrlAsync(
        ProjectName project, string documentUrl, string modelId = "prebuilt-document",
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var job = new DocumentProcessingJob
        {
            Project = project,
            DocumentUrl = documentUrl,
            ModelId = modelId,
            Status = "Processing",
            CreatedAt = DateTime.UtcNow
        };
        _db.DocumentProcessingJobs.Add(job);
        await _db.SaveChangesAsync(ct);

        try
        {
            var operation = await _client.AnalyzeDocumentFromUriAsync(
                WaitUntil.Completed, modelId, new Uri(documentUrl), cancellationToken: ct);

            var analysisResult = operation.Value;
            sw.Stop();

            var extractedData = ExtractData(analysisResult);
            job.Status = "Completed";
            job.ExtractedDataJson = JsonSerializer.Serialize(extractedData);
            job.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            return new DocumentResult
            {
                Success = true,
                ExtractedDataJson = job.ExtractedDataJson,
                RawText = analysisResult.Content,
                PageCount = analysisResult.Pages.Count,
                DurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _log.LogError(ex, "Document analysis failed for {Url}", documentUrl);
            job.Status = "Failed";
            job.ErrorMessage = ex.Message;
            await _db.SaveChangesAsync(ct);

            return new DocumentResult { Success = false, ErrorMessage = ex.Message, DurationMs = sw.ElapsedMilliseconds };
        }
    }

    public async Task<DocumentResult> AnalyzeFromStreamAsync(
        ProjectName project, Stream documentStream, string fileName,
        string modelId = "prebuilt-document",
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var job = new DocumentProcessingJob
        {
            Project = project,
            DocumentUrl = $"stream://{fileName}",
            FileName = fileName,
            ModelId = modelId,
            Status = "Processing",
            CreatedAt = DateTime.UtcNow
        };
        _db.DocumentProcessingJobs.Add(job);
        await _db.SaveChangesAsync(ct);

        try
        {
            var operation = await _client.AnalyzeDocumentAsync(
                WaitUntil.Completed, modelId, documentStream, cancellationToken: ct);

            var analysisResult = operation.Value;
            sw.Stop();

            var extractedData = ExtractData(analysisResult);
            job.Status = "Completed";
            job.ExtractedDataJson = JsonSerializer.Serialize(extractedData);
            job.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            return new DocumentResult
            {
                Success = true,
                ExtractedDataJson = job.ExtractedDataJson,
                RawText = analysisResult.Content,
                PageCount = analysisResult.Pages.Count,
                DurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _log.LogError(ex, "Document analysis failed for stream {File}", fileName);
            job.Status = "Failed";
            job.ErrorMessage = ex.Message;
            await _db.SaveChangesAsync(ct);

            return new DocumentResult { Success = false, ErrorMessage = ex.Message, DurationMs = sw.ElapsedMilliseconds };
        }
    }

    private static Dictionary<string, object> ExtractData(AnalyzeResult result)
    {
        var data = new Dictionary<string, object>
        {
            ["content"] = result.Content,
            ["pageCount"] = result.Pages.Count
        };

        if (result.KeyValuePairs.Count > 0)
        {
            data["keyValuePairs"] = result.KeyValuePairs
                .Where(kvp => kvp.Key?.Content != null)
                .ToDictionary(kvp => kvp.Key.Content, kvp => (object)(kvp.Value?.Content ?? ""));
        }

        if (result.Tables.Count > 0)
        {
            data["tables"] = result.Tables.Select(t => new
            {
                rowCount = t.RowCount,
                columnCount = t.ColumnCount,
                cells = t.Cells.Select(c => new { c.RowIndex, c.ColumnIndex, c.Content })
            });
        }

        return data;
    }
}
