using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Exoplanet.DAL;
using Exoplanet.Services.Prompts;
using Exoplanet.Shared.Entities;
using Exoplanet.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Tests;

/// <summary>
/// RAG Comparison — classifies the same planets with and without RAG context.
/// Real OpenAI API calls. No database writes. Results printed to test output.
///
/// Uses the SAME prompt as production (ClassificationPromptBuilder). The only
/// difference between the two runs is whether ragContext is null or populated.
/// </summary>
public class RagComparisonTests
{
    [Fact]
    public async Task RunRagComparison()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Production.json", optional: true)
            .Build();

        var apiKey = config["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException("OpenAI:ApiKey missing from appsettings.json");
        var model = config["OpenAI:Model"] ?? "gpt-4o-mini";
        var embeddingModel = config["OpenAI:EmbeddingModel"] ?? "text-embedding-3-small";
        var connString = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection missing");
        var vectorConnString = config.GetConnectionString("VectorConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:VectorConnection missing");

        // Load planets from main DB
        var planets = await LoadPlanetsAsync(connString);
        //planets = planets.Where(x => x.PlanetName == "HD 189733 b").ToList();
        Assert.True(planets.Count > 0, "No planets loaded. Check planet names match your database.");

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        // Run 1: Without RAG — same prompt, no context dictionary
        var promptWithout = ClassificationPromptBuilder.Build(planets, ragContext: null);
        var responseWithout = await CallOpenAiAsync(http, model, promptWithout);
        var resultsWithout = ParseResults(responseWithout);

        // Run 2: With RAG — retrieve top 3 references per planet via embedding similarity
        var ragContext = await GetRagContextPerPlanet(http, embeddingModel, planets, vectorConnString);
        var promptWith = ClassificationPromptBuilder.Build(planets, ragContext);
        var responseWith = await CallOpenAiAsync(http, model, promptWith);
        var resultsWith = ParseResults(responseWith);

        PrintComparison(planets, resultsWithout, resultsWith);

        Assert.True(true, "RAG comparison complete — see output above.");
    }

    private static async Task<List<PlanetEntity>> LoadPlanetsAsync(string connString)
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        var options = new DbContextOptionsBuilder<ExoplanetDbContext>()
            .UseNpgsql(connString)
            .Options;

        await using var db = new ExoplanetDbContext(options);
        return await db.Planets.Take(10).ToListAsync();
    }

    /// <summary>
    /// Retrieves top-3 references per planet from the vector DB. Uses a NARRATIVE-style
    /// embedding query so the retriever favors discovery/findings text over flat
    /// measurement strings. Returns RetrievedReference (same shape production uses).
    /// </summary>
    private static async Task<Dictionary<string, List<RetrievedReference>>> GetRagContextPerPlanet(
        HttpClient http, string embeddingModel, List<PlanetEntity> planets, string vectorConnString)
    {
        var options = new DbContextOptionsBuilder<VectorDbContext>()
            .UseNpgsql(vectorConnString)
            .Options;

        var result = new Dictionary<string, List<RetrievedReference>>();

        foreach (var planet in planets)
        {
            // Narrative query — matches production retrieval query in ChangeClassifierService.
            var description =
                $"What is scientifically notable about {planet.PlanetName}? " +
                $"Discovery papers, instruments used, atmospheric findings.";

            var queryEmbedding = await GetEmbeddingAsync(http, embeddingModel, description);
            var embeddingStr = "[" + string.Join(",", queryEmbedding) + "]";

            await using var db = new VectorDbContext(options);
            var refs = await db.Database
                .SqlQueryRaw<RawRefResult>(
                    "SELECT id, planet_name, reference_name, content, " +
                    "1 - (embedding <=> {0}::vector) AS similarity " +
                    "FROM exoplanet_reference " +
                    "WHERE planet_name = {1} " +
                    "ORDER BY embedding <=> {0}::vector " +
                    "LIMIT 3",
                    embeddingStr, planet.PlanetName)
                .ToListAsync();

            // Show what the retriever actually pulled — proof for the blog post.
            Console.WriteLine($"--- Retrieved for {planet.PlanetName} ---");
            foreach (var r in refs)
            {
                var preview = r.content.Length > 120 ? r.content.Substring(0, 120) + "..." : r.content;
                Console.WriteLine($"  [sim={r.similarity:F3}] {preview}");
            }

            result[planet.PlanetName] = refs
                .Select(r => new RetrievedReference
                {
                    ReferenceId = r.id,
                    ReferenceName = r.reference_name,
                    Content = r.content,
                    SimilarityScore = r.similarity
                })
                .ToList();
        }

        return result;
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

    private static Dictionary<string, (string Classification, string PlavalovaCode, string ScientificNote)> ParseResults(string responseText)
    {
        var result = new Dictionary<string, (string, string, string)>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var cleaned = responseText.Trim();
            if (cleaned.StartsWith("```"))
                cleaned = cleaned.Replace("```json", "").Replace("```", "").Trim();

            using var doc = JsonDocument.Parse(cleaned);
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var name = element.GetProperty("planet_name").GetString();
                var classification = element.TryGetProperty("classification", out var c) ? c.GetString() ?? "" : "";
                var code = element.TryGetProperty("plavalova_code", out var pc) ? pc.GetString() ?? "" : "";
                var note = element.TryGetProperty("scientific_note", out var sn) ? sn.GetString() ?? "" : "";

                if (name != null)
                    result[name] = (classification, code, note);
            }
        }
        catch (JsonException) { }

        return result;
    }

    private static async Task<string> CallOpenAiAsync(HttpClient http, string model, string prompt)
    {
        var body = new
        {
            model,
            messages = new[] { new { role = "user", content = prompt } },
            max_tokens = 8000,
            temperature = 0.1
        };

        var json = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await http.PostAsync("https://api.openai.com/v1/chat/completions", content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";
    }

    private static void PrintComparison(
        List<PlanetEntity> planets,
        Dictionary<string, (string Classification, string PlavalovaCode, string ScientificNote)> withoutRag,
        Dictionary<string, (string Classification, string PlavalovaCode, string ScientificNote)> withRag)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("=== RAG COMPARISON RESULTS ===");
        sb.AppendLine();
        sb.AppendLine($"{"Planet",-25} {"Code (no RAG)",15} {"Code (RAG)",15} {"Match?",8}");
        sb.AppendLine(new string('-', 65));

        foreach (var planet in planets)
        {
            var without = withoutRag.TryGetValue(planet.PlanetName, out var w) ? w : (Classification: "?", PlavalovaCode: "?", ScientificNote: "");
            var with = withRag.TryGetValue(planet.PlanetName, out var r) ? r : (Classification: "?", PlavalovaCode: "?", ScientificNote: "");
            var match = without.PlavalovaCode == with.PlavalovaCode ? "Same" : "DIFF";
            sb.AppendLine($"{planet.PlanetName,-25} {without.PlavalovaCode,15} {with.PlavalovaCode,15} {match,8}");
        }

        sb.AppendLine();
        sb.AppendLine("=== SCIENTIFIC NOTES COMPARISON ===");

        foreach (var planet in planets)
        {
            var without = withoutRag.TryGetValue(planet.PlanetName, out var w) ? w : (Classification: "?", PlavalovaCode: "?", ScientificNote: "");
            var with = withRag.TryGetValue(planet.PlanetName, out var r) ? r : (Classification: "?", PlavalovaCode: "?", ScientificNote: "");

            sb.AppendLine();
            sb.AppendLine($"--- {planet.PlanetName} ---");
            sb.AppendLine($"  Without RAG: {without.ScientificNote}");
            sb.AppendLine($"  With RAG:    {with.ScientificNote}");
        }

        Console.WriteLine(sb.ToString());
    }
}

// Raw SQL result type for the vector similarity query
public class RawRefResult
{
    public int id { get; set; }
    public string planet_name { get; set; } = null!;
    public string reference_name { get; set; } = null!;
    public string content { get; set; } = null!;
    public double similarity { get; set; }
}
