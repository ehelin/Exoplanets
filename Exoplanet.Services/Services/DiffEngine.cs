using Exoplanet.Shared.Entities;
using Exoplanet.Shared.Models;

namespace Exoplanet.Services;

/// <summary>
/// Deterministic diff engine. Compares incoming NASA data against existing DB state.
/// Produces structured change log entries. No AI involved — this is pure code detection.
/// </summary>
public static class DiffEngine
{
    public static DiffResult ComputeDiffs(
        List<ExoPlanet> incoming,
        List<ExoplanetEntity> existing,
        int ingestRunId)
    {
        var now = DateTimeOffset.UtcNow;
        var changes = new List<ChangeLogEntity>();
        var toInsert = new List<ExoplanetEntity>();
        var toUpdate = new List<ExoplanetEntity>();

        // Index existing by natural key for O(1) lookup
        var existingByKey = existing.ToDictionary(
            e => (e.PlanetName, e.HostStar),
            e => e);

        // Normalize incoming — same logic the old service used
        var normalizedIncoming = incoming
            .Where(x => !string.IsNullOrWhiteSpace(x.PlanetName) && !string.IsNullOrWhiteSpace(x.HostStar))
            .Select(x => new ExoPlanet
            {
                PlanetName = x.PlanetName!.Trim(),
                HostStar = x.HostStar!.Trim(),
                DiscoveryYear = x.DiscoveryYear
            })
            .DistinctBy(x => (x.PlanetName, x.HostStar))
            .ToList();

        // Track which existing records we've seen (for delete detection)
        var seenKeys = new HashSet<(string, string)>();

        foreach (var inc in normalizedIncoming)
        {
            var key = (inc.PlanetName!, inc.HostStar!);
            seenKeys.Add(key);

            if (!existingByKey.TryGetValue(key, out var ex))
            {
                // ── INSERT: new planet ──
                changes.Add(new ChangeLogEntity
                {
                    IngestRunId = ingestRunId,
                    PlanetName = inc.PlanetName!,
                    ChangeType = "INSERT",
                    FieldName = null,
                    OldValue = null,
                    NewValue = null,
                    DetectedAt = now
                });

                toInsert.Add(new ExoplanetEntity
                {
                    PlanetName = inc.PlanetName!,
                    HostStar = inc.HostStar!,
                    DiscoveryYear = inc.DiscoveryYear,
                    CreatedUtc = now,
                    UpdatedUtc = now
                });
            }
            else
            {
                // ── UPDATE check: field-level diff ──
                var fieldChanges = CompareFields(inc, ex, ingestRunId, now);

                if (fieldChanges.Count > 0)
                {
                    changes.AddRange(fieldChanges);

                    // Apply changes to the existing entity for persistence
                    ex.DiscoveryYear = inc.DiscoveryYear;
                    ex.UpdatedUtc = now;
                    toUpdate.Add(ex);
                }
            }
        }

        // ── DELETE detection: in DB but not in source ──
        foreach (var ex in existing)
        {
            var key = (ex.PlanetName, ex.HostStar);
            if (!seenKeys.Contains(key))
            {
                changes.Add(new ChangeLogEntity
                {
                    IngestRunId = ingestRunId,
                    PlanetName = ex.PlanetName,
                    ChangeType = "DELETE",
                    FieldName = null,
                    OldValue = null,
                    NewValue = null,
                    DetectedAt = now
                });
            }
        }

        return new DiffResult
        {
            Changes = changes,
            ToInsert = toInsert,
            ToUpdate = toUpdate,
            NewCount = toInsert.Count,
            UpdatedCount = toUpdate.Count,
            DeletedCount = changes.Count(c => c.ChangeType == "DELETE"),
            UnchangedCount = normalizedIncoming.Count - toInsert.Count - toUpdate.Count
        };
    }

    /// <summary>
    /// Field-level comparison. One ChangeLogEntity per changed field.
    /// Add new field comparisons here as you expand the TAP query.
    /// </summary>
    private static List<ChangeLogEntity> CompareFields(
        ExoPlanet incoming,
        ExoplanetEntity existing,
        int ingestRunId,
        DateTimeOffset now)
    {
        var changes = new List<ChangeLogEntity>();

        // DiscoveryYear
        if (incoming.DiscoveryYear != existing.DiscoveryYear)
        {
            changes.Add(new ChangeLogEntity
            {
                IngestRunId = ingestRunId,
                PlanetName = existing.PlanetName,
                ChangeType = "UPDATE",
                FieldName = "discovery_year",
                OldValue = existing.DiscoveryYear?.ToString(),
                NewValue = incoming.DiscoveryYear?.ToString(),
                DetectedAt = now
            });
        }

        // ── Add more field comparisons here as schema grows ──
        // Example when you add orbital_period to the TAP query:
        //
        // if (incoming.OrbitalPeriod != existing.OrbitalPeriod)
        // {
        //     changes.Add(new ChangeLogEntity
        //     {
        //         IngestRunId = ingestRunId,
        //         PlanetName = existing.PlanetName,
        //         ChangeType = "UPDATE",
        //         FieldName = "orbital_period",
        //         OldValue = existing.OrbitalPeriod?.ToString(),
        //         NewValue = incoming.OrbitalPeriod?.ToString(),
        //         DetectedAt = now
        //     });
        // }

        return changes;
    }
}
