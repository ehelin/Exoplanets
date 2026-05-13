using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Exoplanet.DAL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ExoPlanet.RagLoad;

internal class Program
{
    static async Task Main(string[] args)
    {
        var csvPath = "C:\\temp\\ExoPlanets\\Documents\\rag_abstracts.csv";
        if (!File.Exists(csvPath))
        {
            Console.WriteLine($"File not found: {csvPath}");
            return;
        }

        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.Production.json", optional: false)
            .Build();

        var vectorConnString = config.GetConnectionString("VectorConnection")
            ?? throw new InvalidOperationException("VectorConnection missing from config.");
        var apiKey = config["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException("OpenAI:ApiKey missing from config.");
        var embeddingModel = config["OpenAI:EmbeddingModel"] ?? "text-embedding-3-small";

        var options = new DbContextOptionsBuilder<VectorDbContext>()
            .UseNpgsql(vectorConnString)
            .Options;

        using var db = new VectorDbContext(options);
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        // Read CSV (skip header)
        var lines = File.ReadAllLines(csvPath).Skip(1).ToList();
        Console.WriteLine($"Read {lines.Count} entries from {csvPath}");

        int loaded = 0;
        foreach (var line in lines)
        {
            // Parse CSV — handle quoted content with commas
            var firstComma = line.IndexOf(',');
            if (firstComma < 0) continue;

            var planetName = line[..firstComma].Trim().Trim('"');
            var content = line[(firstComma + 1)..].Trim().Trim('"');

            if (string.IsNullOrWhiteSpace(planetName) || string.IsNullOrWhiteSpace(content))
                continue;

            Console.WriteLine($"Embedding: {planetName}...");

            // Get embedding
            var embedding = await GetEmbeddingAsync(http, embeddingModel, content);
            var embeddingStr = "[" + string.Join(",", embedding) + "]";

            // Insert into vector DB
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO exoplanet_reference (planet_name, reference_name, pub_date, content, embedding, created_utc) " +
                "VALUES ({0}, {1}, {2}, {3}, {4}::vector, NOW())",
                planetName, "abstract", "", content, embeddingStr);

            loaded++;
            Console.WriteLine($"  Stored. ({loaded}/{lines.Count})");
        }

        Console.WriteLine($"Done. Loaded {loaded} abstracts into vector DB.");
    }

    private static async Task<float[]> GetEmbeddingAsync(HttpClient http, string model, string text)
    {
        var requestBody = new { model, input = text };
        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await http.PostAsync("https://api.openai.com/v1/embeddings", content);
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