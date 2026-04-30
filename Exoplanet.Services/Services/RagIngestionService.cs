using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Exoplanet.DAL;
using Exoplanet.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Exoplanet.Services;

public sealed class RagIngestionService : IRagIngestionService
{
    private readonly HttpClient _http;
    private readonly VectorDbContext _vectorDb;
    private readonly IPipelineLogger _plog;
    private readonly string _model;

    private const string NasaPsUrl =
        "https://exoplanetarchive.ipac.caltech.edu/TAP/sync?query=select+top+1000+pl_name,pl_refname,pl_pubdate,pl_bmasse,pl_rade,pl_orbper,pl_eqt,pl_dens,discoverymethod+from+ps&format=json";
   
    public RagIngestionService(
        HttpClient http,
        VectorDbContext vectorDb,
        IPipelineLogger plog,
        IConfiguration config)
    {
        _http = http;
        _vectorDb = vectorDb;
        _plog = plog;
        _model = config["OpenAI:EmbeddingModel"] ?? "text-embedding-3-small";

        var apiKey = config["OpenAI:ApiKey"];
        if (!string.IsNullOrEmpty(apiKey))
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    public async Task IngestReferencesAsync()
    {
        await _plog.Info("RAG ingestion starting — fetching references from NASA ps table.");

        // Check what we already have
        var existingCount = await _vectorDb.ExoplanetReferences.CountAsync();
        await _plog.Info($"Vector DB has {existingCount} existing references.");

        // Fetch from NASA
        var nasaHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        nasaHttp.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        //var response = await nasaHttp.GetAsync(NasaPsUrl);
        //response.EnsureSuccessStatusCode();

        //await using var stream = await response.Content.ReadAsStreamAsync();
        //using var doc = await JsonDocument.ParseAsync(stream);
        var responseStr = await nasaHttp.GetStringAsync(NasaPsUrl);
        using var doc = JsonDocument.Parse(responseStr);

        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Unexpected JSON shape from NASA ps table.");

        var references = new List<(string PlanetName, string RefName, string? PubDate, string Content)>();

        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var planetName = GetString(el, "pl_name");
            var refName = GetString(el, "pl_refname");
            if (string.IsNullOrWhiteSpace(planetName) || string.IsNullOrWhiteSpace(refName))
                continue;

            var content = BuildContent(el, planetName, refName);
            var pubDate = GetString(el, "pl_pubdate");

            references.Add((planetName.Trim(), refName.Trim(), pubDate, content));
        }

        await _plog.Info($"Fetched {references.Count} references from NASA.");

        // Get existing planet+reference combos to avoid duplicates
        var existingList = await _vectorDb.ExoplanetReferences
            .Select(r => r.PlanetName + "|" + r.ReferenceName)
            .ToListAsync();
        var existing = existingList.ToHashSet();

        var newRefs = references
            .Where(r => !existing.Contains(r.PlanetName + "|" + r.RefName))
            .ToList();

        await _plog.Info($"Found {newRefs.Count} new references to embed.");

        if (newRefs.Count == 0) return;

        // Embed and store in batches of 100
        var batchSize = 100;
        var batches = newRefs
            .Select((r, i) => new { Ref = r, Index = i })
            .GroupBy(x => x.Index / batchSize)
            .Select(g => g.Select(x => x.Ref).ToList())
            .ToList();

        int totalStored = 0;
        foreach (var batch in batches)
        {
            var texts = batch.Select(r => r.Content).ToList();
            var embeddings = await GetEmbeddingsAsync(texts);

            for (int i = 0; i < batch.Count; i++)
            {
                var r = batch[i];
                var embedding = embeddings[i];
                var embeddingStr = "[" + string.Join(",", embedding) + "]";

                await _vectorDb.Database.ExecuteSqlRawAsync(
                    "INSERT INTO exoplanet_reference (planet_name, reference_name, pub_date, content, embedding, created_utc) " +
                    "VALUES ({0}, {1}, {2}, {3}, {4}::vector, NOW())",
                    r.PlanetName, r.RefName, r.PubDate ?? "", r.Content, embeddingStr);
            }

            totalStored += batch.Count;
            await _plog.Info($"RAG ingestion: stored {totalStored}/{newRefs.Count} references.");
        }

        await _plog.Info($"RAG ingestion complete. {totalStored} new references embedded and stored.");
    }

    private async Task<List<float[]>> GetEmbeddingsAsync(List<string> texts)
    {
        var requestBody = new
        {
            model = _model,
            input = texts
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync("https://api.openai.com/v1/embeddings", content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);

        var embeddings = new List<float[]>();
        foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            var vector = item.GetProperty("embedding")
                .EnumerateArray()
                .Select(v => v.GetSingle())
                .ToArray();
            embeddings.Add(vector);
        }

        return embeddings;
    }

    private static string BuildContent(JsonElement el, string planetName, string refName)
    {
        var sb = new StringBuilder();
        sb.Append($"{planetName}, {refName}");

        var method = GetString(el, "discoverymethod");
        if (!string.IsNullOrEmpty(method)) sb.Append($", discovered via {method}");

        var mass = GetDouble(el, "pl_bmasse");
        if (mass.HasValue) sb.Append($", mass={mass:F2} Earth masses");

        var radius = GetDouble(el, "pl_rade");
        if (radius.HasValue) sb.Append($", radius={radius:F2} Earth radii");

        var period = GetDouble(el, "pl_orbper");
        if (period.HasValue) sb.Append($", period={period:F2} days");

        var temp = GetDouble(el, "pl_eqt");
        if (temp.HasValue) sb.Append($", temp={temp:F0} K");

        var density = GetDouble(el, "pl_dens");
        if (density.HasValue) sb.Append($", density={density:F2} g/cm3");

        return sb.ToString();
    }

    private static string? GetString(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Null
            ? v.GetString()
            : null;

    private static double? GetDouble(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : null;
}
