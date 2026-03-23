using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Exoplanet.Shared.Entities;
using Exoplanet.Shared.Interfaces;
using Microsoft.Extensions.Configuration;
using Shared.Interfaces;

namespace Exoplanet.Services;

public sealed class ChangeClassifierService : IChangeClassifierService
{
    private readonly HttpClient _http;
    private readonly IExoplanetRepository _repo;
    private readonly IPipelineLogger _plog;
    private readonly string _model;
    private const int BatchSize = 500;

    public ChangeClassifierService(
        HttpClient http,
        IExoplanetRepository repo,
        IPipelineLogger plog,
        IConfiguration config)
    {
        _http = http;
        _repo = repo;
        _plog = plog;
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

        var planets = await _repo.GetAllPlanetsAsync();
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
                await _plog.Warning($"Batch {batchNum} classification failed: {ex.Message}", ingestRunId);
            }
        }

        await _plog.Info("Classification complete.", ingestRunId);
    }

    private static string BuildClassificationPrompt(
        List<ChangeLogEntity> batch,
        Dictionary<string, PlanetEntity> planetLookup)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are an exoplanet classifier. For each planet below, provide TWO classifications:");
        sb.AppendLine();
        sb.AppendLine("1. DATA QUALITY classification:");
        sb.AppendLine("   - CONFIRMED: has discovery year (1992-2026), mass or radius, and orbital data");
        sb.AppendLine("   - CANDIDATE: missing key measurements (no mass, no radius, no orbital period)");
        sb.AppendLine("   - ANOMALY: data quality concern (discovery year outside range, suspicious values)");
        sb.AppendLine();
        sb.AppendLine("2. PLAVALOVA CODE (mass-temperature-eccentricity-density):");
        sb.AppendLine("   Mass: m=Mercury, e=Earth, N=Neptune, J=Jupiter, W=warm Jupiter (>13 Jupiter masses)");
        sb.AppendLine("   Temperature: F=Frozen(<200K), W=Water(200-450K), G=Gaseous(450-1000K), R=Roaster(>1000K)");
        sb.AppendLine("   Eccentricity: 0=circular(e<0.1), 1=low(0.1-0.3), 2=moderate(0.3-0.6), 3=high(>0.6)");
        sb.AppendLine("   Density: g=gaseous(<1), w=water(1-3), t=terrestrial(3-8), i=iron(8-15), s=super-dense(>15)");
        sb.AppendLine("   Example: Earth = eW0t, Jupiter = JF0g. Use '?' for unknown components.");
        sb.AppendLine();
        sb.AppendLine("Respond ONLY with a JSON array. No markdown, no backticks.");
        sb.AppendLine("Each element: {\"planet_name\": \"...\", \"classification\": \"...\", \"plavalova_code\": \"...\", \"reasoning\": \"...\"}");
        sb.AppendLine("Keep reasoning to one sentence max.");
        sb.AppendLine();
        sb.AppendLine("--- PLANETS ---");

        foreach (var c in batch)
        {
            if (!planetLookup.TryGetValue(c.PlanetName, out var p))
                continue;

            sb.Append($"name={c.PlanetName}");
            sb.Append($", disc_year={p.DiscoveryYear?.ToString() ?? "?"}");
            sb.Append($", method={p.DiscoveryMethod ?? "?"}");
            sb.Append($", mass_earth={p.PlanetMass?.ToString("F2") ?? "?"}");
            sb.Append($", radius_earth={p.PlanetRadius?.ToString("F2") ?? "?"}");
            sb.Append($", period_days={p.OrbitalPeriod?.ToString("F2") ?? "?"}");
            sb.Append($", ecc={p.Eccentricity?.ToString("F3") ?? "?"}");
            sb.Append($", temp_k={p.EquilibriumTemp?.ToString("F0") ?? "?"}");
            sb.Append($", density={p.PlanetDensity?.ToString("F2") ?? "?"}");
            sb.Append($", insol={p.InsolationFlux?.ToString("F2") ?? "?"}");
            sb.AppendLine();
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
                cleaned = cleaned.Replace("```json", "").Replace("```", "").Trim();
            }

            using var doc = JsonDocument.Parse(cleaned);
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var name = element.GetProperty("planet_name").GetString();
                var classification = element.GetProperty("classification").GetString();
                var reasoning = element.GetProperty("reasoning").GetString();

                // Include plavalova code in the classification string
                var plavalova = element.TryGetProperty("plavalova_code", out var pc) ? pc.GetString() : null;
                var fullClassification = plavalova != null
                    ? $"{classification}|{plavalova}"
                    : classification ?? "";

                if (name != null && classification != null)
                {
                    result[name] = (fullClassification, reasoning ?? "");
                }
            }
        }
        catch (JsonException) { }

        return result;
    }

    private async Task<(string responseText, int? tokensUsed)> CallOpenAiAsync(string prompt)
    {
        var requestBody = new
        {
            model = _model,
            messages = new[] { new { role = "user", content = prompt } },
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
