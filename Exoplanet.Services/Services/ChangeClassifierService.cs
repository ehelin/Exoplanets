using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Exoplanet.Shared.Entities;
using Exoplanet.Shared.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shared.Interfaces;

namespace Exoplanet.Services;

public sealed class ChangeClassifierService : IChangeClassifierService
{
    private readonly HttpClient _http;
    private readonly IExoplanetRepository _repo;
    private readonly IPipelineLogger _plog;
    private readonly ILogger<ChangeClassifierService> _log;
    private readonly string _model;
    private const int BatchSize = 50;

    public ChangeClassifierService(
        HttpClient http,
        IExoplanetRepository repo,
        IPipelineLogger plog,
        IConfiguration config,
        ILogger<ChangeClassifierService> log)
    {
        _http = http;
        _repo = repo;
        _plog = plog;
        _log = log;
        _model = config["OpenAI:Model"] ?? "gpt-4o-mini";

        var apiKey = config["OpenAI:ApiKey"];
        if (!string.IsNullOrEmpty(apiKey))
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    public async Task ClassifyAsync(int ingestRunId)
    {
        var changes = await _repo.GetChangeLogByRunAsync(ingestRunId);

        if (changes.Count == 0)
        {
            await _plog.Info("No changes to classify.", ingestRunId);
            return;
        }

        // Load planet data once for all batches
        var planets = await _repo.GetAllExistingAsync();
        var planetLookup = planets.ToDictionary(p => p.PlanetName, p => p);

        await _plog.Info($"Classifying {changes.Count} changes in batches of {BatchSize}.", ingestRunId);

        var batches = changes
            .Select((c, i) => new { Change = c, Index = i })
            .GroupBy(x => x.Index / BatchSize)
            .Select(g => g.Select(x => x.Change).ToList())
            .ToList();

        int batchNum = 0;
        foreach (var batch in batches)
        {
            batchNum++;
            await _plog.Info($"Processing batch {batchNum}/{batches.Count} ({batch.Count} changes).", ingestRunId);

            try
            {
                var prompt = BuildClassificationPrompt(batch, planetLookup);
                var (responseText, tokensUsed) = await CallOpenAiAsync(prompt);
                var classifications = ParseClassifications(responseText);

                await _repo.ApplyClassificationsAsync(batch, classifications);

                await _plog.Info($"Batch {batchNum} classified. Tokens: {tokensUsed}.", ingestRunId);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Classification batch {Batch} failed for run {RunId}.", batchNum, ingestRunId);
                await _plog.Warning($"Batch {batchNum} classification failed: {ex.Message}", ingestRunId);
            }
        }

        await _plog.Info("Classification complete.", ingestRunId);
    }

    private static string BuildClassificationPrompt(
        List<ChangeLogEntity> batch,
        Dictionary<string, ExoplanetEntity> planetLookup)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are a data quality classifier for an exoplanet database.");
        sb.AppendLine();
        sb.AppendLine("For each planet below, classify it as one of:");
        sb.AppendLine("- CONFIRMED: has a host star and a discovery year between 1992 and 2026");
        sb.AppendLine("- CANDIDATE: missing discovery year or host star");
        sb.AppendLine("- ANOMALY: discovery year before 1992 or after 2025, name appears malformed, or other data quality concern");
        sb.AppendLine();
        sb.AppendLine("Respond ONLY with a JSON array. No markdown, no backticks, no explanation outside the array.");
        sb.AppendLine("Each element: {\"planet_name\": \"...\", \"classification\": \"...\", \"reasoning\": \"...\"}");
        sb.AppendLine("Keep reasoning to one sentence max.");
        sb.AppendLine();
        sb.AppendLine("--- PLANETS ---");

        foreach (var c in batch)
        {
            var details = new StringBuilder();
            details.Append($"name={c.PlanetName}");

            if (planetLookup.TryGetValue(c.PlanetName, out var planet))
            {
                details.Append($", host_star={planet.HostStar}");
                details.Append($", discovery_year={planet.DiscoveryYear?.ToString() ?? "null"}");
            }

            if (c.ChangeType == "INSERT")
                details.Append(", change=NEW");
            else if (c.ChangeType == "UPDATE")
                details.Append($", change=UPDATE {c.FieldName}: [{c.OldValue ?? "null"}] → [{c.NewValue ?? "null"}]");
            else if (c.ChangeType == "DELETE")
                details.Append(", change=REMOVED_FROM_SOURCE");

            sb.AppendLine(details.ToString());
        }

        sb.AppendLine("--- END PLANETS ---");
        return sb.ToString();
    }

    private static Dictionary<string, (string Classification, string Reasoning)> ParseClassifications(string responseText)
    {
        var result = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var cleaned = responseText.Trim();
            if (cleaned.StartsWith("```"))
            {
                cleaned = cleaned
                    .Replace("```json", "")
                    .Replace("```", "")
                    .Trim();
            }

            using var doc = JsonDocument.Parse(cleaned);
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var name = element.GetProperty("planet_name").GetString();
                var classification = element.GetProperty("classification").GetString();
                var reasoning = element.GetProperty("reasoning").GetString();

                if (name != null && classification != null)
                {
                    result[name] = (classification, reasoning ?? "");
                }
            }
        }
        catch (JsonException)
        {
            // If parsing fails, return empty — the batch gets skipped and logged
        }

        return result;
    }

    private async Task<(string responseText, int? tokensUsed)> CallOpenAiAsync(string prompt)
    {
        var requestBody = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            max_tokens = 16000,
            temperature = 0.1
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync("https://api.openai.com/v1/chat/completions", content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);

        var text = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";

        int? tokensUsed = null;
        if (doc.RootElement.TryGetProperty("usage", out var usage) &&
            usage.TryGetProperty("total_tokens", out var tokens))
        {
            tokensUsed = tokens.GetInt32();
        }

        return (text, tokensUsed);
    }
}