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
    #region Constructor / Class Variables
    private readonly HttpClient _http;
    private readonly IExoplanetRepository _repo;
    private readonly IPipelineLogger _plog;
    private readonly string _model;
    private const int BatchSize = 50;
    private readonly IRagRetrievalService _ragRetrievalService;

    public ChangeClassifierService(
        HttpClient http,
        IExoplanetRepository repo,
        IPipelineLogger plog,
        IConfiguration config,
        IRagRetrievalService ragRetrievalService)
    {
        _http = http;
        _repo = repo;
        _plog = plog;
        _model = config["OpenAI:Model"] ?? "gpt-4o-mini";

        _ragRetrievalService = ragRetrievalService;

        var apiKey = config["OpenAI:ApiKey"];
        if (!string.IsNullOrEmpty(apiKey))
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }
    #endregion

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
                //var prompt = BuildClassificationPrompt(batch, planetLookup);
                var prompt = await BuildClassificationPromptAsync(batch, planetLookup, ingestRunId);

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

    private async Task<string> BuildClassificationPromptAsync(
        List<ChangeLogEntity> batch,
        Dictionary<string, PlanetEntity> planetLookup,
        int ingestRunId)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are an exoplanet classifier. For each planet below, provide THREE items:");
        #region Build rest of prompt
        sb.AppendLine();
        sb.AppendLine("1. DATA QUALITY classification:");
        sb.AppendLine("   - CONFIRMED: has discovery year (1992-2026), mass or radius, and orbital data");
        sb.AppendLine("   - CANDIDATE: missing key measurements (no mass, no radius, no orbital period)");
        sb.AppendLine("   - ANOMALY: data quality concern (discovery year outside range, suspicious values)");
        sb.AppendLine();
        sb.AppendLine("2. PLAVALOVA CODE \u0097 EXACTLY 4 characters: [mass][temp][ecc][density].");
        sb.AppendLine();
        sb.AppendLine("   MASS \u0097 look at mass_earth and apply STRICTLY:");
        sb.AppendLine("     m = mass_earth < 0.1");
        sb.AppendLine("     e = mass_earth >= 0.1 AND mass_earth < 10.0");
        sb.AppendLine("     N = mass_earth >= 10.0 AND mass_earth < 100.0");
        sb.AppendLine("     J = mass_earth >= 100.0 AND mass_earth < 4000.0");
        sb.AppendLine("     W = mass_earth >= 4000.0");
        sb.AppendLine("   CRITICAL: If mass_earth >= 10.0, the code is N, NOT e.");
        sb.AppendLine("     mass_earth=9.99 -> e");
        sb.AppendLine("     mass_earth=10.0 -> N");
        sb.AppendLine("     mass_earth=10.1 -> N");
        sb.AppendLine("     mass_earth=11.0 -> N");
        sb.AppendLine("     mass_earth=18.7 -> N");
        sb.AppendLine("     mass_earth=85.8 -> N");
        sb.AppendLine("     mass_earth=318 -> J");
        sb.AppendLine();
        sb.AppendLine("   TEMPERATURE \u0097 look at temp_k and apply STRICTLY:");
        sb.AppendLine("     F = temp_k < 200");
        sb.AppendLine("     W = temp_k >= 200 AND temp_k < 450");
        sb.AppendLine("     G = temp_k >= 450 AND temp_k < 1000");
        sb.AppendLine("     R = temp_k >= 1000");
        sb.AppendLine("   Use UPPERCASE only: F, W, G, R.");
        sb.AppendLine("     temp_k=419 -> W");
        sb.AppendLine("     temp_k=858 -> G");
        sb.AppendLine("     temp_k=900 -> G");
        sb.AppendLine("     temp_k=1655 -> R");
        sb.AppendLine();
        sb.AppendLine("   ECCENTRICITY \u0097 look at ecc value:");
        sb.AppendLine("     0 = ecc < 0.1");
        sb.AppendLine("     1 = ecc >= 0.1 AND ecc < 0.3");
        sb.AppendLine("     2 = ecc >= 0.3 AND ecc < 0.6");
        sb.AppendLine("     3 = ecc >= 0.6");
        sb.AppendLine("   Examples: ecc=0.000 -> 0, ecc=0.017 -> 0, ecc=0.080 -> 0, ecc=0.110 -> 1, ecc=0.450 -> 2");
        sb.AppendLine();
        sb.AppendLine("   DENSITY \u0097 look at density value in g/cm3:");
        sb.AppendLine("     g = density < 1.0");
        sb.AppendLine("     w = density >= 1.0 AND density < 3.0");
        sb.AppendLine("     t = density >= 3.0 AND density < 8.0");
        sb.AppendLine("     i = density >= 8.0 AND density < 15.0");
        sb.AppendLine("     s = density >= 15.0");
        sb.AppendLine();
        sb.AppendLine("   Use '?' for any component where the data is missing.");
        sb.AppendLine();
        sb.AppendLine("   WORKED EXAMPLES:");
        sb.AppendLine("     mass=1.0, temp=255, ecc=0.017, density=5.51 -> eW0t");
        sb.AppendLine("     mass=317.8, temp=110, ecc=0.049, density=1.33 -> JF0w");
        sb.AppendLine("     mass=18.7, temp=419, ecc=0.000, density=1.10 -> NW0w");
        sb.AppendLine("     mass=85.8, temp=900, ecc=0.110, density=0.35 -> NG1g");
        sb.AppendLine("     mass=11.0, temp=858, ecc=0.000, density=1.65 -> NG0w");
        sb.AppendLine("     mass=10.1, temp=1655, ecc=0.000, density=1.78 -> NR0w");
        sb.AppendLine();
        sb.AppendLine("   The code MUST be EXACTLY 4 characters. One letter/digit per component.");
        sb.AppendLine("   Do NOT include numeric values in the code.");
        sb.AppendLine("   WRONG: N900G1g  RIGHT: NG1g");
        sb.AppendLine("   WRONG: e255W0t  RIGHT: eW0t");
        sb.AppendLine("   WRONG: N0w (only 3 chars)  RIGHT: NW0w (4 chars)");
        sb.AppendLine();
        sb.AppendLine("   BEFORE RESPONDING, verify each code is exactly 4 characters.");
        sb.AppendLine();
        sb.AppendLine("3. SCIENTIFIC NOTE \u0097 if RESEARCH CONTEXT is provided for a planet, write a one-sentence note");
        sb.AppendLine("   explaining why this planet is scientifically interesting based on the research.");
        sb.AppendLine("   If no research context is provided, write 'No research context available.'");
        sb.AppendLine();
        sb.AppendLine("Respond ONLY with a JSON array. No markdown, no backticks.");
        sb.AppendLine("Each element: {\"planet_name\": \"...\", \"classification\": \"...\", \"plavalova_code\": \"...\", \"reasoning\": \"...\", \"scientific_note\": \"...\"}");
        sb.AppendLine("Keep reasoning to one sentence max.");
        sb.AppendLine();
        sb.AppendLine("--- PLANETS ---");
        #endregion

        foreach (var c in batch)
        {
            #region batch prompt setup
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
            #endregion

            // RAG: retrieve relevant references for this planet
            var description = $"{c.PlanetName} mass={p.PlanetMass?.ToString("F2")} temp={p.EquilibriumTemp?.ToString("F0")} density={p.PlanetDensity?.ToString("F2")}";
            var refs = await _ragRetrievalService.RetrieveAsync(c.PlanetName, description, ingestRunId);
            if (refs.Count > 0)
            {
                sb.AppendLine("  RESEARCH CONTEXT:");
                foreach (var r in refs)
                    sb.AppendLine($"  - {r.Content} (relevance: {r.SimilarityScore:F2})");
            }
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