using Exoplanet.Shared.Entities;
using Exoplanet.Shared.Interfaces;
using Microsoft.Extensions.Configuration;
using Shared.Interfaces;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Exoplanet.Services;

public sealed class EvalRunner : IEvalRunner
{
    private readonly IExoplanetRepository _repo;
    private readonly IPipelineLogger _plog;
    private readonly HttpClient _http;           
    private readonly string _model;              
    private const double DriftThresholdPercent = 10.0;
    private const int RollingWindowSize = 5;
    private const int JudgeBatchSize = 50;       

    public EvalRunner(IExoplanetRepository repo, IPipelineLogger plog, HttpClient http, IConfiguration config)
    {
        _repo = repo;
        _plog = plog;
        _http = http;
        _model = config["OpenAI:Model"] ?? "gpt-4o-mini";

        var apiKey = config["OpenAI:ApiKey"];
        if (!string.IsNullOrEmpty(apiKey))
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    public async Task RunLlmJudgeAsync(int ingestRunId)
    {
        var planets = await _repo.GetAllPlanetsAsync();

        var changes = await _repo.GetChangeLogByRunAsync(ingestRunId);
        var classifiedThisRun = changes
            .Where(c => c.FieldName == "classification")
            .Select(c => c.PlanetName)
            .ToHashSet();

        var classifiedPlanets = planets
            .Where(p => p.Classification != null && classifiedThisRun.Contains(p.PlanetName))
            .ToList();

        if (classifiedPlanets.Count == 0)
        {
            await _plog.Info("LLM Judge: no classified planets to review.", ingestRunId);
            return;
        }

        await _plog.Info($"LLM Judge: reviewing {classifiedPlanets.Count} planets in batches of {JudgeBatchSize}.", ingestRunId);

        var batches = classifiedPlanets
            .Select((p, i) => new { Planet = p, Index = i })
            .GroupBy(x => x.Index / JudgeBatchSize)
            .Select(g => g.Select(x => x.Planet).ToList())
            .ToList();

        var results = new List<EvalResultEntity>();
        int batchNum = 0;

        foreach (var batch in batches)
        {
            batchNum++;
            await _plog.Info($"LLM Judge: batch {batchNum}/{batches.Count} ({batch.Count} planets).", ingestRunId);

            try
            {
                var prompt = BuildJudgePrompt(batch);
                var (responseText, tokensUsed) = await CallOpenAiAsync(prompt);
                var judgments = ParseJudgments(responseText);

                foreach (var planet in batch)
                {
                    if (judgments.TryGetValue(planet.PlanetName, out var judgment))
                    {
                        results.Add(new EvalResultEntity
                        {
                            IngestRunId = ingestRunId,
                            EvalType = "LLM_JUDGE",
                            PlanetName = planet.PlanetName,
                            ExpectedValue = null,
                            ActualValue = $"score={judgment.Score}|{judgment.Reasoning}",
                            Score = judgment.Score * 10,
                            Dimension = "reasoning",
                            PassFail = judgment.Score >= 7 ? "PASS" : "FAIL",
                            EvaluatedAt = DateTimeOffset.UtcNow
                        });
                    }
                }

                await _plog.Info($"LLM Judge: batch {batchNum} complete. Tokens: {tokensUsed}.", ingestRunId);
            }
            catch (Exception ex)
            {
                await _plog.Warning($"LLM Judge: batch {batchNum} failed: {ex.Message}", ingestRunId);
            }
        }

        await _repo.WriteEvalResultsAsync(results);

        var passCount = results.Count(r => r.PassFail == "PASS");
        var avgScore = results.Where(r => r.Score.HasValue)
            .Select(r => r.Score!.Value).DefaultIfEmpty(0).Average();

        await _plog.Info(
            $"LLM Judge complete. {passCount}/{results.Count} pass, avg score {avgScore:F0}%.",
            ingestRunId);
    }

    private static string BuildJudgePrompt(List<PlanetEntity> batch)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are an independent reviewer of exoplanet classifications.");
        sb.AppendLine("Another AI classified each planet below with a data quality label and a Plavalova code.");
        sb.AppendLine("Your job: review whether the classification makes sense given the raw data.");
        sb.AppendLine();
        sb.AppendLine("For each planet, score the classification from 1 to 10:");
        sb.AppendLine("  10 = perfect, classification and code match the data exactly");
        sb.AppendLine("  7-9 = reasonable, minor issues");
        sb.AppendLine("  4-6 = questionable, some components seem wrong");
        sb.AppendLine("  1-3 = clearly wrong, classification doesn't match the data");
        sb.AppendLine();
        sb.AppendLine("Respond ONLY with a JSON array. No markdown, no backticks.");
        sb.AppendLine("Each element: {\"planet_name\": \"...\", \"score\": N, \"reasoning\": \"...\"}");
        sb.AppendLine("Keep reasoning to one sentence max.");
        sb.AppendLine();
        sb.AppendLine("--- PLANETS ---");

        foreach (var p in batch)
        {
            var parts = p.Classification?.Split('|') ?? [];
            var dataQuality = parts.Length > 0 ? parts[0] : "?";
            var plavCode = parts.Length > 1 ? parts[1] : "?";

            sb.Append($"name={p.PlanetName}");
            sb.Append($", classification={dataQuality}");
            sb.Append($", plavalova_code={plavCode}");
            sb.Append($", mass_earth={p.PlanetMass?.ToString("F2") ?? "?"}");
            sb.Append($", radius_earth={p.PlanetRadius?.ToString("F2") ?? "?"}");
            sb.Append($", period_days={p.OrbitalPeriod?.ToString("F2") ?? "?"}");
            sb.Append($", ecc={p.Eccentricity?.ToString("F3") ?? "?"}");
            sb.Append($", temp_k={p.EquilibriumTemp?.ToString("F0") ?? "?"}");
            sb.Append($", density={p.PlanetDensity?.ToString("F2") ?? "?"}");
            sb.Append($", disc_year={p.DiscoveryYear?.ToString() ?? "?"}");
            sb.AppendLine();
        }

        sb.AppendLine("--- END PLANETS ---");
        return sb.ToString();
    }

    private static Dictionary<string, (int Score, string Reasoning)> ParseJudgments(string responseText)
    {
        var result = new Dictionary<string, (int, string)>(StringComparer.OrdinalIgnoreCase);

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
                var score = element.GetProperty("score").GetInt32();
                var reasoning = element.TryGetProperty("reasoning", out var r) ? r.GetString() ?? "" : "";

                if (name != null)
                {
                    result[name] = (Math.Clamp(score, 1, 10), reasoning);
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

    public async Task EvaluateAsync(int ingestRunId)
    {
        var planets = await _repo.GetAllPlanetsAsync();

        var changes = await _repo.GetChangeLogByRunAsync(ingestRunId);
        var classifiedThisRun = changes
            .Where(c => c.FieldName == "classification")
            .Select(c => c.PlanetName)
            .ToHashSet();

        var classifiedPlanets = planets
            .Where(p => p.Classification != null && classifiedThisRun.Contains(p.PlanetName))
            .ToList();

        if (classifiedPlanets.Count == 0)
        {
            await _plog.Info("No classified planets to evaluate this run.", ingestRunId);
            return;
        }

        await _plog.Info($"Evaluating {classifiedPlanets.Count} planets classified this run.", ingestRunId);

        var results = new List<EvalResultEntity>();

        foreach (var planet in classifiedPlanets)
        {
            var parts = planet.Classification!.Split('|');
            var aiDataQuality = parts[0];
            var aiPlavalovaCode = parts.Length > 1 ? parts[1] : null;

            // ── Eval 1: Data Quality Classification ──────────────
            var expectedDataQuality = ComputeExpectedDataQuality(planet);

            results.Add(new EvalResultEntity
            {
                IngestRunId = ingestRunId,
                EvalType = "CLASSIFICATION",
                PlanetName = planet.PlanetName,
                ExpectedValue = expectedDataQuality,
                ActualValue = aiDataQuality,
                Score = aiDataQuality == expectedDataQuality ? 100 : 0,
                Dimension = "accuracy",
                PassFail = aiDataQuality == expectedDataQuality ? "PASS" : "FAIL",
                EvaluatedAt = DateTimeOffset.UtcNow
            });

            // ── Eval 2: Plávalová Code ───────────────────────────
            if (aiPlavalovaCode != null)
            {
                var expectedCode = ComputeExpectedPlavalovaCode(planet);

                var componentScore = ScorePlavalovaComponents(expectedCode, aiPlavalovaCode);

                results.Add(new EvalResultEntity
                {
                    IngestRunId = ingestRunId,
                    EvalType = "PLAVALOVA",
                    PlanetName = planet.PlanetName,
                    ExpectedValue = expectedCode,
                    ActualValue = aiPlavalovaCode,
                    Score = componentScore,
                    Dimension = "accuracy",
                    PassFail = componentScore >= 75 ? "PASS" : "FAIL",
                    EvaluatedAt = DateTimeOffset.UtcNow
                });
            }
            else
            {
                results.Add(new EvalResultEntity
                {
                    IngestRunId = ingestRunId,
                    EvalType = "PLAVALOVA",
                    PlanetName = planet.PlanetName,
                    ExpectedValue = ComputeExpectedPlavalovaCode(planet),
                    ActualValue = null,
                    Score = 0,
                    Dimension = "completeness",
                    PassFail = "FAIL",
                    EvaluatedAt = DateTimeOffset.UtcNow
                });
            }
        }

        await _repo.WriteEvalResultsAsync(results);

        var classificationPass = results.Count(r => r.EvalType == "CLASSIFICATION" && r.PassFail == "PASS");
        var classificationTotal = results.Count(r => r.EvalType == "CLASSIFICATION");
        var plavalovaPass = results.Count(r => r.EvalType == "PLAVALOVA" && r.PassFail == "PASS");
        var plavalovaTotal = results.Count(r => r.EvalType == "PLAVALOVA");
        var avgPlavalovaScore = results.Where(r => r.EvalType == "PLAVALOVA" && r.Score.HasValue)
            .Select(r => r.Score!.Value).DefaultIfEmpty(0).Average();

        await _plog.Info(
            $"Eval complete. Classification: {classificationPass}/{classificationTotal} pass. " +
            $"Plavalova: {plavalovaPass}/{plavalovaTotal} pass, avg score {avgPlavalovaScore:F0}%.",
            ingestRunId);
    }

    public async Task CheckForDriftAsync(int ingestRunId)
    {
        var currentAvg = await _repo.GetAverageEvalScoreAsync(ingestRunId, "PLAVALOVA");
        if (currentAvg == null)
        {
            await _plog.Info("Drift check: no eval scores for this run.", ingestRunId);
            return;
        }

        var recentRunIds = await _repo.GetRecentCompletedRunIdsAsync(ingestRunId, RollingWindowSize);
        if (recentRunIds.Count == 0)
        {
            await _plog.Info($"Drift check: first run, baseline score {currentAvg:F1}%.", ingestRunId);
            return;
        }

        var recentScores = new List<double>();
        foreach (var runId in recentRunIds)
        {
            var avg = await _repo.GetAverageEvalScoreAsync(runId, "PLAVALOVA");
            if (avg.HasValue)
                recentScores.Add(avg.Value);
        }

        if (recentScores.Count == 0)
        {
            await _plog.Info($"Drift check: no prior scores to compare. Current: {currentAvg:F1}%.", ingestRunId);
            return;
        }

        var rollingAvg = recentScores.Average();
        var drop = rollingAvg - currentAvg.Value;

        if (drop > DriftThresholdPercent)
        {
            await _plog.Warning(
                $"DRIFT DETECTED: Plavalova score dropped {drop:F1}% " +
                $"(rolling avg {rollingAvg:F1}% -> current {currentAvg:F1}%). " +
                $"Threshold: {DriftThresholdPercent}%.",
                ingestRunId);
        }
        else
        {
            await _plog.Info(
                $"Drift check: stable (rolling avg {rollingAvg:F1}% -> current {currentAvg:F1}%, " +
                $"delta {drop:F1}%, threshold {DriftThresholdPercent}%).",
                ingestRunId);
        }
    }

    private static string ComputeExpectedDataQuality(PlanetEntity p)
    {
        if (p.DiscoveryYear.HasValue && (p.DiscoveryYear < 1992 || p.DiscoveryYear > 2026))
            return "ANOMALY";

        if (!p.DiscoveryYear.HasValue)
            return "CANDIDATE";
        if (!p.PlanetMass.HasValue && !p.PlanetRadius.HasValue)
            return "CANDIDATE";

        return "CONFIRMED";
    }

    private static string ComputeExpectedPlavalovaCode(PlanetEntity p)
    {
        var mass = ComputeMassClass(p.PlanetMass);
        var temp = ComputeTempClass(p.EquilibriumTemp);
        var ecc = ComputeEccClass(p.Eccentricity);
        var dens = ComputeDensityClass(p.PlanetDensity);

        return $"{mass}{temp}{ecc}{dens}";
    }

    private static char ComputeMassClass(double? massEarth)
    {
        if (!massEarth.HasValue) return '?';
        var m = massEarth.Value;
        if (m < 0.1) return 'm';
        if (m < 10) return 'e';
        if (m < 100) return 'N';
        if (m < 4000) return 'J';
        return 'W';
    }

    private static char ComputeTempClass(double? tempK)
    {
        if (!tempK.HasValue) return '?';
        var t = tempK.Value;
        if (t < 200) return 'F';
        if (t < 450) return 'W';
        if (t < 1000) return 'G';
        return 'R';
    }

    private static char ComputeEccClass(double? ecc)
    {
        if (!ecc.HasValue) return '?';
        var e = ecc.Value;
        if (e < 0.1) return '0';
        if (e < 0.3) return '1';
        if (e < 0.6) return '2';
        return '3';
    }

    private static char ComputeDensityClass(double? density)
    {
        if (!density.HasValue) return '?';
        var d = density.Value;
        if (d < 1) return 'g';
        if (d < 3) return 'w';
        if (d < 8) return 't';
        if (d < 15) return 'i';
        return 's';
    }

    private static int ScorePlavalovaComponents(string expected, string actual)
    {
        if (expected.Length != 4 || actual.Length != 4)
            return 0;

        int score = 0;
        for (int i = 0; i < 4; i++)
        {
            if (char.ToUpper(expected[i]) == char.ToUpper(actual[i]))
                score += 25;
        }

        return score;
    }
}
