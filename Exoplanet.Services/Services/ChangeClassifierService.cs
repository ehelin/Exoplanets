using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Exoplanet.Services.Prompts;
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

    /// <summary>
    /// Production prompt build path. Retrieves RAG context per planet, then hands
    /// everything to the shared ClassificationPromptBuilder. The test path
    /// (RagComparisonTests) calls the same builder directly.
    /// </summary>
    private async Task<string> BuildClassificationPromptAsync(
        List<ChangeLogEntity> batch,
        Dictionary<string, PlanetEntity> planetLookup,
        int ingestRunId)
    {
        // Resolve batch -> planet entities (skip any without a matching planet)
        var planetsInBatch = new List<PlanetEntity>();
        foreach (var c in batch)
        {
            if (planetLookup.TryGetValue(c.PlanetName, out var p))
                planetsInBatch.Add(p);
        }

        // Retrieve RAG context per planet using a narrative-style query.
        // (A numerical query like "mass=X temp=Y" pulls measurement strings;
        // a narrative query pulls discovery papers and findings.)
        var ragContext = new Dictionary<string, List<RetrievedReference>>();
        foreach (var p in planetsInBatch)
        {
            var description =
                $"What is scientifically notable about {p.PlanetName}? " +
                $"Discovery papers, instruments used, atmospheric findings.";

            var refs = await _ragRetrievalService.RetrieveAsync(p.PlanetName, description, ingestRunId);
            if (refs.Count > 0)
                ragContext[p.PlanetName] = refs;
        }

        return ClassificationPromptBuilder.Build(planetsInBatch, ragContext);
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
