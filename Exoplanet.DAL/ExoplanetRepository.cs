using Exoplanet.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using Shared.Interfaces;

namespace Exoplanet.DAL;

public sealed class ExoplanetRepository : IExoplanetRepository
{
    private readonly ExoplanetDbContext _db;

    public ExoplanetRepository(ExoplanetDbContext db)
    {
        _db = db;
    }

    // ── Planet reads ───────────────────────────────────────

    public async Task<List<PlanetEntity>> GetAllPlanetsAsync()
    {
        return await _db.Planets.AsNoTracking().ToListAsync();
    }

    public async Task<HashSet<string>> GetExistingPlanetNamesAsync()
    {
        var names = await _db.Planets.AsNoTracking()
            .Select(p => p.PlanetName)
            .ToListAsync();
        return names.ToHashSet();
    }

    // ── Solar System / Star / Planet writes ─────────────────

    public async Task<SolarSystemEntity> GetOrCreateSolarSystemAsync(
        string hostStar, double? distanceParsecs, int? numStars, int? numPlanets)
    {
        var star = await _db.Stars.Include(s => s.SolarSystem)
            .FirstOrDefaultAsync(s => s.Name == hostStar);

        if (star != null)
            return star.SolarSystem;

        var now = DateTimeOffset.UtcNow;
        var system = new SolarSystemEntity
        {
            DistanceParsecs = distanceParsecs,
            NumStars = numStars,
            NumPlanets = numPlanets,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        _db.SolarSystems.Add(system);
        await _db.SaveChangesAsync();
        return system;
    }

    public async Task<StarEntity> GetOrCreateStarAsync(
        int solarSystemId, string name, double? tempK, double? radius, double? mass, string? spectralType)
    {
        var existing = await _db.Stars
            .FirstOrDefaultAsync(s => s.Name == name);

        if (existing != null)
            return existing;

        var now = DateTimeOffset.UtcNow;
        var star = new StarEntity
        {
            SolarSystemId = solarSystemId,
            Name = name,
            TemperatureK = tempK,
            RadiusSolar = radius,
            MassSolar = mass,
            SpectralType = spectralType,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        _db.Stars.Add(star);
        await _db.SaveChangesAsync();
        return star;
    }

    public async Task<PlanetEntity?> GetPlanetByNameAsync(string planetName)
    {
        return await _db.Planets.FirstOrDefaultAsync(p => p.PlanetName == planetName);
    }

    public async Task InsertPlanetAsync(PlanetEntity planet)
    {
        _db.Planets.Add(planet);
        await _db.SaveChangesAsync();
    }

    public async Task UpdatePlanetAsync(PlanetEntity planet)
    {
        _db.Planets.Update(planet);
        await _db.SaveChangesAsync();
    }

    public async Task LinkPlanetToStarAsync(int planetId, int starId)
    {
        var exists = await _db.PlanetStars
            .AnyAsync(ps => ps.PlanetId == planetId && ps.StarId == starId);

        if (!exists)
        {
            _db.PlanetStars.Add(new PlanetStarEntity { PlanetId = planetId, StarId = starId });
            await _db.SaveChangesAsync();
        }
    }

    // ── Bulk insert for initial load performance ────────────

    public async Task BulkInsertSolarSystemsAsync(List<SolarSystemEntity> systems)
    {
        if (systems.Count == 0) return;
        _db.SolarSystems.AddRange(systems);
        await _db.SaveChangesAsync();
    }

    public async Task BulkInsertStarsAsync(List<StarEntity> stars)
    {
        if (stars.Count == 0) return;
        _db.Stars.AddRange(stars);
        await _db.SaveChangesAsync();
    }

    public async Task BulkInsertPlanetsAsync(List<PlanetEntity> planets)
    {
        if (planets.Count == 0) return;
        _db.Planets.AddRange(planets);
        await _db.SaveChangesAsync();
    }

    public async Task BulkInsertPlanetStarsAsync(List<PlanetStarEntity> links)
    {
        if (links.Count == 0) return;
        _db.PlanetStars.AddRange(links);
        await _db.SaveChangesAsync();
    }

    // ── Evidence tables ────────────────────────────────────

    public async Task<IngestRunEntity> CreateIngestRunAsync(string source, string? sourceUrl, int rowsFetched)
    {
        var run = new IngestRunEntity
        {
            RunTimestamp = DateTimeOffset.UtcNow,
            Source = source,
            SourceUrl = sourceUrl,
            RowsFetched = rowsFetched,
            Status = "RUNNING"
        };

        _db.IngestRuns.Add(run);
        await _db.SaveChangesAsync();
        return run;
    }

    public async Task CompleteIngestRunAsync(IngestRunEntity run)
    {
        run.CompletedAt = DateTimeOffset.UtcNow;
        run.Status = "COMPLETED";
        _db.IngestRuns.Update(run);
        await _db.SaveChangesAsync();
    }

    public async Task FailIngestRunAsync(IngestRunEntity run, string errorMessage)
    {
        run.CompletedAt = DateTimeOffset.UtcNow;
        run.Status = "FAILED";
        run.ErrorMessage = errorMessage;
        _db.IngestRuns.Update(run);
        await _db.SaveChangesAsync();
    }

    public async Task WriteChangeLogAsync(List<ChangeLogEntity> entries)
    {
        if (entries.Count == 0) return;
        _db.ChangeLogs.AddRange(entries);
        await _db.SaveChangesAsync();
    }

    public async Task<List<ChangeLogEntity>> GetChangeLogByRunAsync(int ingestRunId)
    {
        return await _db.ChangeLogs.AsNoTracking()
            .Where(c => c.IngestRunId == ingestRunId)
            .OrderBy(c => c.ChangeType)
            .ThenBy(c => c.PlanetName)
            .ToListAsync();
    }

    public async Task WriteChangeReportAsync(ChangeReportEntity report)
    {
        _db.ChangeReports.Add(report);
        await _db.SaveChangesAsync();
    }

    public async Task WriteEvalResultAsync(EvalResultEntity result)
    {
        _db.EvalResults.Add(result);
        await _db.SaveChangesAsync();
    }

    public async Task WriteEvalResultsAsync(List<EvalResultEntity> results)
    {
        if (results.Count == 0) return;
        _db.EvalResults.AddRange(results);
        await _db.SaveChangesAsync();
    }

    // ── Drift detection ────────────────────────────────────

    public async Task<double?> GetAverageEvalScoreAsync(int ingestRunId, string evalType)
    {
        var scores = await _db.EvalResults.AsNoTracking()
            .Where(e => e.IngestRunId == ingestRunId && e.EvalType == evalType && e.Score.HasValue)
            .Select(e => e.Score!.Value)
            .ToListAsync();

        if (scores.Count == 0) return null;
        return scores.Average();
    }

    public async Task<List<int>> GetRecentCompletedRunIdsAsync(int excludeRunId, int count)
    {
        return await _db.IngestRuns.AsNoTracking()
            .Where(r => r.Status == "COMPLETED" && r.Id != excludeRunId)
            .OrderByDescending(r => r.Id)
            .Take(count)
            .Select(r => r.Id)
            .ToListAsync();
    }

    // ── Classification ─────────────────────────────────────

    public async Task ApplyClassificationsAsync(
        List<ChangeLogEntity> changes,
        Dictionary<string, (string Classification, string Reasoning)> classifications)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var change in changes)
        {
            if (!classifications.TryGetValue(change.PlanetName, out var result))
                continue;

            var planet = await _db.Planets
                .FirstOrDefaultAsync(p => p.PlanetName == change.PlanetName);

            if (planet != null)
            {
                var oldClassification = planet.Classification;
                planet.Classification = result.Classification;
                planet.UpdatedUtc = now;

                _db.ChangeLogs.Add(new ChangeLogEntity
                {
                    IngestRunId = change.IngestRunId,
                    PlanetName = change.PlanetName,
                    ChangeType = "UPDATE",
                    FieldName = "classification",
                    OldValue = oldClassification,
                    NewValue = result.Classification,
                    AiClassification = result.Classification,
                    AiReasoning = result.Reasoning,
                    DetectedAt = now
                });
            }
        }

        await _db.SaveChangesAsync();
    }
}
