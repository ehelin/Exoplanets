using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Exoplanet.Shared.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shared.Interfaces;

namespace Exoplanet.Services;

/// <summary>
/// Phase 2.3: AI as Explainer.
/// Reads change_log entries for a given ingest run, builds a prompt,
/// calls OpenAI, and writes the result to change_report.
/// The AI only sees diffs that code already detected — it never decides what changed.
/// </summary>
public sealed class ChangeReportService : IChangeReportService
{
    private readonly HttpClient _http;
    private readonly IExoplanetRepository _repo;
    private readonly ILogger<ChangeReportService> _log;
    private readonly string _model;

    public ChangeReportService(
        HttpClient http,
        IExoplanetRepository repo,
        IConfiguration config,
        ILogger<ChangeReportService> log)
    {
        _http = http;
        _repo = repo;
        _log = log;
        _model = config["OpenAI:Model"] ?? "gpt-4o-mini";

        var apiKey = config["OpenAI:ApiKey"];
        if (!string.IsNullOrEmpty(apiKey))
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    public async Task GenerateReportAsync(int ingestRunId)
    {
        // Step 1: Read the evidence — only change_log rows for this run
        var changes = await _repo.GetChangeLogByRunAsync(ingestRunId);

        if (changes.Count == 0)
        {
            _log.LogInformation("No changes for run {RunId}, skipping AI report.", ingestRunId);
            return;
        }

        // Step 2: Build the prompt from structured diffs
        var prompt = BuildPrompt(changes);

        _log.LogInformation(
            "Generating change report for run {RunId} with {ChangeCount} diffs using {Model}.",
            ingestRunId, changes.Count, _model);

        // Step 3: Call OpenAI
        var (reportText, tokensUsed) = await CallOpenAiAsync(prompt);

        // Step 4: Write audit record — prompt sent, response received, model used
        var report = new ChangeReportEntity
        {
            IngestRunId = ingestRunId,
            ModelUsed = _model,
            PromptSent = prompt,
            ReportText = reportText,
            TokensUsed = tokensUsed,
            GeneratedAt = DateTimeOffset.UtcNow
        };

        await _repo.WriteChangeReportAsync(report);

        _log.LogInformation(
            "Change report saved for run {RunId}. Tokens used: {Tokens}.",
            ingestRunId, tokensUsed);
    }

    private static string BuildPrompt(List<ChangeLogEntity> changes)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are a data pipeline reporter. Below is a structured diff from the latest NASA Exoplanet Archive ingestion.");
        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("- Only describe what the diffs show. Do not interpret scientific significance.");
        sb.AppendLine("- Do not speculate about discoveries, habitability, or implications.");
        sb.AppendLine("- A large number of INSERTs likely means the pipeline ran against an empty or reset database, not new discoveries.");
        sb.AppendLine("- State counts and specifics. Be factual and concise.");
        sb.AppendLine("- 2-4 sentences maximum.");
        sb.AppendLine();

        var inserts = changes.Count(c => c.ChangeType == "INSERT");
        var updates = changes.Count(c => c.ChangeType == "UPDATE");
        var deletes = changes.Count(c => c.ChangeType == "DELETE");

        sb.AppendLine($"Summary: {inserts} inserts, {updates} updates, {deletes} deletes.");
        sb.AppendLine();
        sb.AppendLine("--- CHANGES (sample, max 50) ---");

        var sampled = changes.Take(50).ToList();

        foreach (var c in sampled)
        {
            switch (c.ChangeType)
            {
                case "INSERT":
                    sb.AppendLine($"INSERT: {c.PlanetName}");
                    break;
                case "DELETE":
                    sb.AppendLine($"DELETE: {c.PlanetName}");
                    break;
                case "UPDATE":
                    sb.AppendLine($"UPDATE: {c.PlanetName} — {c.FieldName}: [{c.OldValue ?? "null"}] → [{c.NewValue ?? "null"}]");
                    break;
            }
        }

        if (changes.Count > 50)
            sb.AppendLine($"... and {changes.Count - 50} more.");

        sb.AppendLine("--- END CHANGES ---");
        return sb.ToString();
    }

    private async Task<(string reportText, int? tokensUsed)> CallOpenAiAsync(string prompt)
    {
        var requestBody = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            max_tokens = 500,
            temperature = 0.3
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync("https://api.openai.com/v1/chat/completions", content);
       // response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);

        var reportText = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "(empty response)";

        int? tokensUsed = null;
        if (doc.RootElement.TryGetProperty("usage", out var usage) &&
            usage.TryGetProperty("total_tokens", out var tokens))
        {
            tokensUsed = tokens.GetInt32();
        }

        return (reportText, tokensUsed);
    }
}
