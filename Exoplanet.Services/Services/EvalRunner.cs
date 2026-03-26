using Exoplanet.Shared.Entities;
using Exoplanet.Shared.Interfaces;
using Shared.Interfaces;

namespace Exoplanet.Services;

public interface IEvalRunner
{
    Task EvaluateAsync(int ingestRunId);
}

public sealed class EvalRunner : IEvalRunner
{
    private readonly IExoplanetRepository _repo;
    private readonly IPipelineLogger _plog;

    public EvalRunner(IExoplanetRepository repo, IPipelineLogger plog)
    {
        _repo = repo;
        _plog = plog;
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