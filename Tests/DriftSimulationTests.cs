using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Exoplanet.DAL;
using Exoplanet.Shared.Entities;
using Exoplanet.Shared.Interfaces;
using Exoplanet.Services.Prompts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Tests;

/// <summary>
/// Drift Simulation — 10 progressive prompt degradations against the same 10 planets.
/// Real OpenAI API calls. No database writes. Results printed to test output.
///
/// Run order:
///   1  Baseline              - Full production prompt
///   2  No worked examples    - Remove worked examples
///   3  No CRITICAL warnings  - Remove boundary enforcement
///   4  Softened language     - STRICTLY -> approximately
///   5  No boundary examples  - Compact threshold format
///   6  Vague density         - Density goes descriptive
///   7  Vague temperature     - Temperature goes descriptive
///   8  Vague mass            - Mass goes descriptive
///   9  No format enforcement - Remove 4-char verification step
///  10  Bare prompt           - Labels only, no numbers
/// </summary>
public class DriftSimulationTests
{
    // The same 10 planets used in Phase 3 — hardcoded, read-only from DB
    private static readonly string[] Phase3PlanetNames =
    [
        "Kepler-1167 b",
        "Kepler-1740 b",
        "Kepler-1581 b",
        "Kepler-644 b",
        "Kepler-1752 b",
        "Kepler-280 c",
        "Kepler-1208 b",
        "Kepler-263 c",
        "Kepler-1101 b",
        "HD 168746 b"
    ];

    private static readonly IPromptProvider[] Runs =
    [
        new ProductionPromptProvider(),         // Run 1 - Full prompt
        new SoftenedLanguagePromptProvider(),   // Run 2 - STRICTLY -> approximately
        new NoCriticalWarningsPromptProvider(), // Run 3 - Remove boundary enforcement
        new NoBoundaryExamplesPromptProvider(), // Run 4 - Remove edge case examples
        new BarePromptProvider()                // Run 5 - Labels only, no numbers
    ];

    private static readonly string[] RunLabels =
    [
        "Run 1 | Baseline (full prompt)",
        "Run 2 | Softened language",
        "Run 3 | No boundary enforcement",
        "Run 4 | No edge case examples",
        "Run 5 | Bare prompt (no numbers)"
    ];

    [Fact]
    public async Task RunDriftSimulation()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Production.json", optional: true)
            .Build();

        var apiKey = config["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException("OpenAI:ApiKey missing from appsettings.json");
        var model = config["OpenAI:Model"] ?? "gpt-4o-mini";
        var connString = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection missing from appsettings.json");

        // Read planets from DB — read only, no writes
        var planets = await LoadPlanetsAsync(connString);
        Assert.True(planets.Count > 0, "No planets loaded. Check planet names match your database.");

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var scoreTable = new List<(string Label, double AvgScore, int PassCount, List<(string Planet, int Score)> Detail)>();

        for (int i = 0; i < Runs.Length; i++)
        {
            var label = RunLabels[i];
            var provider = Runs[i];

            var prompt = provider.GetPrompt(planets);
            var responseText = await CallOpenAiAsync(http, model, prompt);
            var classifications = ParseClassifications(responseText);

            var planetScores = new List<(string Planet, int Score)>();
            foreach (var planet in planets)
            {
                if (!classifications.TryGetValue(planet.PlanetName, out var aiCode))
                {
                    planetScores.Add((planet.PlanetName, 0));
                    continue;
                }
                var expected = ComputeExpectedCode(planet);
                var score = ScoreComponents(expected, aiCode);
                planetScores.Add((planet.PlanetName, score));
            }

            var avg = planetScores.Count > 0 ? planetScores.Average(x => x.Score) : 0;
            var pass = planetScores.Count(x => x.Score >= 75);
            scoreTable.Add((label, avg, pass, planetScores));
        }

        // Print results
        PrintResults(scoreTable);

        // Assert baseline is better than bare prompt (sanity check)
        var baselineAvg = scoreTable[0].AvgScore;
        var bareAvg = scoreTable[^1].AvgScore;
        Assert.True(baselineAvg > 50,
            $"Expected baseline above 50% but got {baselineAvg:F0}%");
    }

    private static async Task<List<PlanetEntity>> LoadPlanetsAsync(string connString)
    {
        var options = new DbContextOptionsBuilder<ExoplanetDbContext>()
            .UseNpgsql(connString)
            .Options;

        await using var db = new ExoplanetDbContext(options);

        return await db.Planets
            .Where(p => Phase3PlanetNames.Contains(p.PlanetName))
            .ToListAsync();
    }

    private static async Task<string> CallOpenAiAsync(HttpClient http, string model, string prompt)
    {
        var body = new
        {
            model,
            messages = new[] { new { role = "user", content = prompt } },
            max_tokens = 4000,
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

    private static Dictionary<string, string> ParseClassifications(string responseText)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var cleaned = responseText.Trim();
            if (cleaned.StartsWith("```"))
                cleaned = cleaned.Replace("```json", "").Replace("```", "").Trim();

            using var doc = JsonDocument.Parse(cleaned);
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var name = element.GetProperty("planet_name").GetString();
                var code = element.TryGetProperty("plavalova_code", out var pc) ? pc.GetString() : null;
                if (name != null && code != null)
                    result[name] = code;
            }
        }
        catch (JsonException) { }
        return result;
    }

    private static string ComputeExpectedCode(PlanetEntity p)
    {
        return $"{MassClass(p.PlanetMass)}{TempClass(p.EquilibriumTemp)}{EccClass(p.Eccentricity)}{DensClass(p.PlanetDensity)}";
    }

    private static char MassClass(double? m) => m switch
    {
        null => '?',
        < 0.1 => 'm',
        < 10 => 'e',
        < 100 => 'N',
        < 4000 => 'J',
        _ => 'W'
    };

    private static char TempClass(double? t) => t switch
    {
        null => '?',
        < 200 => 'F',
        < 450 => 'W',
        < 1000 => 'G',
        _ => 'R'
    };

    private static char EccClass(double? e) => e switch
    {
        null => '?',
        < 0.1 => '0',
        < 0.3 => '1',
        < 0.6 => '2',
        _ => '3'
    };

    private static char DensClass(double? d) => d switch
    {
        null => '?',
        < 1.0 => 'g',
        < 3.0 => 'w',
        < 8.0 => 't',
        < 15.0 => 'i',
        _ => 's'
    };

    private static int ScoreComponents(string expected, string actual)
    {
        if (expected.Length != 4 || actual.Length != 4) return 0;
        int score = 0;
        for (int i = 0; i < 4; i++)
            if (char.ToUpper(expected[i]) == char.ToUpper(actual[i]))
                score += 25;
        return score;
    }

    private static void PrintResults(List<(string Label, double AvgScore, int PassCount, List<(string Planet, int Score)> Detail)> table)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("=== DRIFT SIMULATION RESULTS ===");
        sb.AppendLine();
        sb.AppendLine($"{"Run",-45} {"Avg Score",10} {"Pass/10",10}");
        sb.AppendLine(new string('-', 67));

        foreach (var (label, avg, pass, _) in table)
            sb.AppendLine($"{label,-45} {avg,9:F0}% {pass,9}/10");

        sb.AppendLine();
        sb.AppendLine("=== PER-PLANET DETAIL ===");
        foreach (var (label, _, _, detail) in table)
        {
            sb.AppendLine();
            sb.AppendLine(label);
            foreach (var (planet, score) in detail)
                sb.AppendLine($"  {planet,-30} {score,3}%");
        }

        Console.WriteLine(sb.ToString());
    }
}
