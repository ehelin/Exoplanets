using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Exoplanet.DAL;
using Exoplanet.Shared.Entities;
using Exoplanet.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NpgsqlTypes;
using Shared.Interfaces;

namespace Exoplanet.Services;

public sealed class RagRetrievalService : IRagRetrievalService
{
    private readonly HttpClient _http;
    private readonly VectorDbContext _vectorDb;
    private readonly IExoplanetRepository _repo;
    private readonly string _model;
    private const int TopK = 3;

    public RagRetrievalService(
        HttpClient http,
        VectorDbContext vectorDb,
        IExoplanetRepository repo,
        IConfiguration config)
    {
        _http = http;
        _vectorDb = vectorDb;
        _repo = repo;
        _model = config["OpenAI:EmbeddingModel"] ?? "text-embedding-3-small";

        var apiKey = config["OpenAI:ApiKey"];
        if (!string.IsNullOrEmpty(apiKey))
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    public async Task<List<RetrievedReference>> RetrieveAsync(
        string planetName, string planetDescription, int? ingestRunId = null)
    {
        // Get embedding for the query
        var queryEmbedding = await GetEmbeddingAsync(planetDescription);
        var embeddingStr = "[" + string.Join(",", queryEmbedding) + "]";

        // Query vector DB for top K most similar references
        var results = await _vectorDb.Database
            .SqlQueryRaw<RawReferenceResult>(
                "SELECT id, planet_name, reference_name, content, " +
                "1 - (embedding <=> {0}::vector) AS similarity " +
                "FROM exoplanet_reference " +
                "ORDER BY embedding <=> {0}::vector " +
                "LIMIT {1}",
                embeddingStr, TopK)
            .ToListAsync();

        var retrieved = results.Select(r => new RetrievedReference
        {
            ReferenceId = r.id,
            ReferenceName = r.reference_name,
            Content = r.content,
            SimilarityScore = r.similarity
        }).ToList();

        // Log retrieval
        if (ingestRunId.HasValue)
        {
            var logs = retrieved.Select(r => new RetrievalLogEntity
            {
                IngestRunId = ingestRunId.Value,
                PlanetName = planetName,
                ReferenceId = r.ReferenceId,
                ReferenceName = r.ReferenceName,
                SimilarityScore = r.SimilarityScore,
                WasReferenced = false,
                RetrievedAt = DateTimeOffset.UtcNow
            }).ToList();

            await _repo.WriteRetrievalLogsAsync(logs);
        }

        return retrieved;
    }

    private async Task<float[]> GetEmbeddingAsync(string text)
    {
        var requestBody = new
        {
            model = _model,
            input = text
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync("https://api.openai.com/v1/embeddings", content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);

        return doc.RootElement
            .GetProperty("data")[0]
            .GetProperty("embedding")
            .EnumerateArray()
            .Select(v => v.GetSingle())
            .ToArray();
    }
}

// Internal class for raw SQL query result mapping
public class RawReferenceResult
{
    public int id { get; set; }
    public string planet_name { get; set; } = null!;
    public string reference_name { get; set; } = null!;
    public string content { get; set; } = null!;
    public double similarity { get; set; }
}
