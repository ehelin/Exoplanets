using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Exoplanet.DAL;
using Exoplanet.Shared.Entities;
using Exoplanet.Services.Prompts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Tests;

/// <summary>
/// RAG Comparison — classifies the same planets with and without RAG context.
/// Real OpenAI API calls. No database writes. Results printed to test output.
/// Shows the difference RAG makes in classification and scientific notes.
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
        planets = planets.Where(x => x.PlanetName == "Kepler-1167 b").ToList();
        Assert.True(planets.Count > 0, "No planets loaded. Check planet names match your database.");

        // Load references from vector DB
        var references = await LoadReferencesAsync(vectorConnString, planets);

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        // Run 1: Without RAG
        var promptWithout = BuildPrompt(planets, null);
        var responseWithout = await CallOpenAiAsync(http, model, promptWithout);
        var resultsWithout = ParseResults(responseWithout);

        // Run 2: With RAG — retrieve top 3 references per planet via embedding similarity
        var ragContext = await GetRagContextPerPlanet(http, embeddingModel, planets, vectorConnString);
        var promptWith = BuildPrompt(planets, ragContext);
        var responseWith = await CallOpenAiAsync(http, model, promptWith);
        var resultsWith = ParseResults(responseWith);

        // Print comparison
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

    private static async Task<Dictionary<string, List<string>>> LoadReferencesAsync(
        string vectorConnString, List<PlanetEntity> planets)
    {
        var options = new DbContextOptionsBuilder<VectorDbContext>()
            .UseNpgsql(vectorConnString)
            .Options;

        await using var db = new VectorDbContext(options);

        var planetNames = planets.Select(p => p.PlanetName).ToHashSet();
        var allRefs = await db.ExoplanetReferences
            .Where(r => planetNames.Contains(r.PlanetName))
            .ToListAsync();

        return allRefs
            .GroupBy(r => r.PlanetName)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => r.Content).ToList());
    }

    private static async Task<Dictionary<string, List<(string Content, double Similarity)>>> GetRagContextPerPlanet(
        HttpClient http, string embeddingModel, List<PlanetEntity> planets, string vectorConnString)
    {
        var options = new DbContextOptionsBuilder<VectorDbContext>()
            .UseNpgsql(vectorConnString)
            .Options;

        var result = new Dictionary<string, List<(string, double)>>();

        foreach (var planet in planets)
        {
            var description = $"{planet.PlanetName} mass={planet.PlanetMass?.ToString("F2")} temp={planet.EquilibriumTemp?.ToString("F0")} density={planet.PlanetDensity?.ToString("F2")}";

            // Get embedding for the query
            var queryEmbedding = await GetEmbeddingAsync(http, embeddingModel, description);
            var embeddingStr = "[" + string.Join(",", queryEmbedding) + "]";

            // Query vector DB for top 3 most similar references
            await using var db = new VectorDbContext(options);
            var refs = await db.Database
                .SqlQueryRaw<RawRefResult>(
                    "SELECT id, planet_name, reference_name, content, " +
                    "1 - (embedding <=> {0}::vector) AS similarity " +
                    "FROM exoplanet_reference " +
                    "ORDER BY embedding <=> {0}::vector " +
                    "LIMIT 3",
                    embeddingStr)
                .ToListAsync();

            result[planet.PlanetName] = refs
                .Select(r => (r.content, r.similarity))
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

    private static string BuildPrompt(
        List<PlanetEntity> planets,
        Dictionary<string, List<(string Content, double Similarity)>>? ragContext)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are an exoplanet classifier. For each planet below, provide THREE items:");
        sb.AppendLine();
        sb.AppendLine("1. DATA QUALITY classification: CONFIRMED, CANDIDATE, or ANOMALY");
        sb.AppendLine("2. PLAVALOVA CODE — EXACTLY 4 characters: [mass][temp][ecc][density]");
        sb.AppendLine("3. SCIENTIFIC NOTE — if RESEARCH CONTEXT is provided, write a one-sentence note");
        sb.AppendLine("   explaining why this planet is scientifically interesting based on the research.");
        //sb.AppendLine("   If no research context is provided, write 'No research context available.'");
        sb.AppendLine();
        sb.AppendLine("   MASS: m(<0.1), e(0.1-10), N(10-100), J(100-4000), W(4000+) Earth masses");
        sb.AppendLine("   TEMP: F(<200K), W(200-450K), G(450-1000K), R(1000K+)");
        sb.AppendLine("   ECC: 0(<0.1), 1(0.1-0.3), 2(0.3-0.6), 3(0.6+)");
        sb.AppendLine("   DENSITY: g(<1), w(1-3), t(3-8), i(8-15), s(15+) g/cm3");
        sb.AppendLine("   Use '?' for missing data.");
        sb.AppendLine();
        sb.AppendLine("Respond ONLY with a JSON array. No markdown, no backticks.");
        sb.AppendLine("Each element: {\"planet_name\": \"...\", \"classification\": \"...\", \"plavalova_code\": \"...\", \"reasoning\": \"...\", \"scientific_note\": \"...\"}");
        sb.AppendLine();
        sb.AppendLine("--- PLANETS ---");

        foreach (var p in planets)
        {
            sb.Append($"name={p.PlanetName}");
            sb.Append($", disc_year={p.DiscoveryYear?.ToString() ?? "?"}");
            sb.Append($", method={p.DiscoveryMethod ?? "?"}");
            sb.Append($", mass_earth={p.PlanetMass?.ToString("F2") ?? "?"}");
            sb.Append($", radius_earth={p.PlanetRadius?.ToString("F2") ?? "?"}");
            sb.Append($", period_days={p.OrbitalPeriod?.ToString("F2") ?? "?"}");
            sb.Append($", ecc={p.Eccentricity?.ToString("F3") ?? "?"}");
            sb.Append($", temp_k={p.EquilibriumTemp?.ToString("F0") ?? "?"}");
            sb.Append($", density={p.PlanetDensity?.ToString("F2") ?? "?"}");
            sb.AppendLine();

            if (ragContext != null && ragContext.TryGetValue(p.PlanetName, out var refs) && refs.Count > 0)
            {
                sb.AppendLine("  RESEARCH CONTEXT:");
                foreach (var (content, similarity) in refs)
                    sb.AppendLine($"  - {content} (relevance: {similarity:F2})");
            }
        }

        sb.AppendLine("--- END PLANETS ---");
        return sb.ToString();
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

        // Summary table
        sb.AppendLine($"{"Planet",-25} {"Code (no RAG)",15} {"Code (RAG)",15} {"Match?",8}");
        sb.AppendLine(new string('-', 65));

        foreach (var planet in planets)
        {
            var without = withoutRag.TryGetValue(planet.PlanetName, out var w) ? w : (Classification: "?", PlavalovaCode: "?", ScientificNote: "");
            var with = withRag.TryGetValue(planet.PlanetName, out var r) ? r : (Classification: "?", PlavalovaCode: "?", ScientificNote: "");
            var match = without.PlavalovaCode == with.PlavalovaCode ? "Same" : "DIFF";

            sb.AppendLine($"{planet.PlanetName,-25} {without.PlavalovaCode,15} {with.PlavalovaCode,15} {match,8}");
        }

        // Detailed scientific notes comparison
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

// Raw SQL result class for vector similarity query
public class RawRefResult
{
    public int id { get; set; }
    public string planet_name { get; set; } = null!;
    public string reference_name { get; set; } = null!;
    public string content { get; set; } = null!;
    public double similarity { get; set; }
}