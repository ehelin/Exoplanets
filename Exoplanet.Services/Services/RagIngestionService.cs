using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Exoplanet.DAL;
using Exoplanet.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Shared.Interfaces;

namespace Exoplanet.Services;

public sealed class RagIngestionService : IRagIngestionService
{
    #region Constructor / Class Variables
    private readonly HttpClient _http;
    private readonly VectorDbContext _vectorDb;
    private readonly IExoplanetRepository _repo;
    private readonly IPipelineLogger _plog;
    private readonly string _model;

    private const string NasaPsBaseUrl = "https://exoplanetarchive.ipac.caltech.edu/TAP/sync?query=select+pl_name,pl_refname,pl_pubdate,pl_bmasse,pl_rade,pl_orbper,pl_eqt,pl_dens,discoverymethod+from+ps+where+pl_name='{0}'&format=json";

    public RagIngestionService(
        HttpClient http,
        VectorDbContext vectorDb,
        IExoplanetRepository repo,
        IPipelineLogger plog,
        IConfiguration config)
    {
        _http = http;
        _vectorDb = vectorDb;
        _repo = repo;
        _plog = plog;
        _model = config["OpenAI:EmbeddingModel"] ?? "text-embedding-3-small";

        var apiKey = config["OpenAI:ApiKey"];
        if (!string.IsNullOrEmpty(apiKey))
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }
    #endregion

    public async Task IngestReferencesAsync()
    {
        await _plog.Info("RAG ingestion starting.");

        // Get planets from our database
        var planets = await _repo.GetAllPlanetsAsync();
        #region Loop Setup
        if (planets.Count == 0)
        {
            await _plog.Info("No planets in database. Skipping RAG ingestion.");
            return;
        }
        await _plog.Info($"Found {planets.Count} planets in database.");

        // Get existing references to avoid duplicates
        var existingList = await _vectorDb.ExoplanetReferences
            .Select(r => r.PlanetName + "|" + r.ReferenceName)
            .ToListAsync();
        var existing = existingList.ToHashSet();
        await _plog.Info($"Vector DB has {existing.Count} existing references.");

        var nasaHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        nasaHttp.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var allNewRefs = new List<(string PlanetName, string RefName, string? PubDate, string Content)>();
        int planetCount = 0;
        #endregion

        foreach (var planet in planets)
        {
            planetCount++;
            var encodedName = Uri.EscapeDataString(planet.PlanetName);
            var url = string.Format(NasaPsBaseUrl, encodedName);

            try
            {
                var responseStr = await nasaHttp.GetStringAsync(url);

                #region Processing Response for this planet
                using var doc = JsonDocument.Parse(responseStr);

                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var planetName = GetString(el, "pl_name");
                    var refName = GetString(el, "pl_refname");
                    if (string.IsNullOrWhiteSpace(planetName) || string.IsNullOrWhiteSpace(refName))
                        continue;

                    var key = planetName.Trim() + "|" + refName.Trim();
                    if (existing.Contains(key))
                        continue;
                    var content = BuildContent(el, planetName, refName);
                    var pubDate = GetString(el, "pl_pubdate");
                    allNewRefs.Add((planetName.Trim(), refName.Trim(), pubDate, content));
                    existing.Add(key);
                }
                #endregion
            }
            catch (Exception ex)
            {
                await _plog.Warning($"RAG ingestion: failed to fetch references for {planet.PlanetName}: {ex.Message}");
            }

            if (planetCount % 10 == 0)
                await _plog.Info($"RAG ingestion: fetched references for {planetCount}/{planets.Count} planets. {allNewRefs.Count} new refs so far.");
        }

        await _plog.Info($"RAG ingestion: fetched {allNewRefs.Count} new references from NASA for {planets.Count} planets.");

        if (allNewRefs.Count == 0) return;

        await SaveRagData(allNewRefs);
    }

    private async Task SaveRagData(List<(string PlanetName, string RefName, string? PubDate, string Content)> allNewRefs)
    {
        // Embed and store in batches of 100
        var batchSize = 100;
        var batches = allNewRefs
            .Select((r, i) => new { Ref = r, Index = i })
            .GroupBy(x => x.Index / batchSize)
            .Select(g => g.Select(x => x.Ref).ToList())
            .ToList();

        int totalStored = 0;
        foreach (var batch in batches)
        {
            try
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
                await _plog.Info($"RAG ingestion: embedded and stored {totalStored}/{allNewRefs.Count} references.");
            }
            catch (Exception ex)
            {
                await _plog.Warning($"RAG ingestion: batch embed/store failed: {ex.Message}");
            }
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