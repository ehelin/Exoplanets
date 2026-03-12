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

    // ── Phase 1 (still used) ───────────────────────────────

    public async Task<HashSet<(string PlanetName, string HostStar)>> GetExistingKeysAsync()
    {
        var keys = await _db.Exoplanets
            .AsNoTracking()
            .Select(e => new { e.PlanetName, e.HostStar })
            .ToListAsync();

        return keys
            .Select(k => (k.PlanetName, k.HostStar))
            .ToHashSet();
    }

    public async Task<int> InsertNewAsync(List<ExoplanetEntity> newEntities)
    {
        if (newEntities.Count == 0)
            return 0;

        _db.Exoplanets.AddRange(newEntities);
        return await _db.SaveChangesAsync();
    }

    // ── Phase 2: Full record loading for diff ──────────────

    public async Task<List<ExoplanetEntity>> GetAllExistingAsync()
    {
        return await _db.Exoplanets
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task UpdateExistingAsync(List<ExoplanetEntity> updatedEntities)
    {
        if (updatedEntities.Count == 0)
            return;

        foreach (var entity in updatedEntities)
        {
            _db.Exoplanets.Attach(entity);
            _db.Entry(entity).State = EntityState.Modified;
        }

        await _db.SaveChangesAsync();
    }

    // ── Phase 2: Evidence tables ───────────────────────────

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
        if (entries.Count == 0)
            return;

        _db.ChangeLogs.AddRange(entries);
        await _db.SaveChangesAsync();
    }

    public async Task<List<ChangeLogEntity>> GetChangeLogByRunAsync(int ingestRunId)
    {
        return await _db.ChangeLogs
            .AsNoTracking()
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

    // ── Classification: AI writes back to change_log and exoplanets ──

    public async Task ApplyClassificationsAsync(
        List<ChangeLogEntity> changes,
        Dictionary<string, (string Classification, string Reasoning)> classifications)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var change in changes)
        {
            if (!classifications.TryGetValue(change.PlanetName, out var result))
                continue;

            // Update change_log row with AI classification
            var logEntry = await _db.ChangeLogs.FindAsync(change.Id);
            if (logEntry != null)
            {
                logEntry.AiClassification = result.Classification;
                logEntry.AiReasoning = result.Reasoning;
            }

            // Update exoplanets table with classification
            var planet = await _db.Exoplanets
                .FirstOrDefaultAsync(p => p.PlanetName == change.PlanetName);

            if (planet != null)
            {
                var oldClassification = planet.Classification;
                planet.Classification = result.Classification;
                planet.UpdatedUtc = now;

                // Log the classification change in change_log
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
