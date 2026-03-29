using Exoplanet.Shared.Entities;
using Exoplanet.Shared.Interfaces;
using Exoplanet.Shared.Models;
using Microsoft.EntityFrameworkCore.Metadata;
using Shared.Interfaces;
using Shared.Models;

namespace Exoplanet.Services;

public sealed class ExoplanetService : IExoplanetService
{
    private readonly IExoplanetApiClient _api;
    private readonly IExoplanetRepository _repo;
    private readonly IChangeReportService _reportService;
    private readonly IChangeClassifierService _classifier;
    private readonly IEvalRunner _evalRunner;
    private readonly IPipelineLogger _plog;

    private const string SourceName = "NASA_EXOPLANET_ARCHIVE";
    private const string SourceUrl = "https://exoplanetarchive.ipac.caltech.edu/TAP/sync?query=select+pl_name,hostname,disc_year,discoverymethod,pl_bmasse,pl_rade,pl_orbper,pl_orbsmax,pl_orbeccen,pl_eqt,pl_dens,pl_insol,st_teff,st_rad,st_mass,st_spectype,sy_dist,sy_snum,sy_pnum+from+pscomppars&format=json";

    public ExoplanetService(
        IExoplanetApiClient api,
        IExoplanetRepository repo,
        IChangeReportService reportService,
        IChangeClassifierService classifier,
        IEvalRunner evalRunner,
        IPipelineLogger plog)
    {
        _api = api;
        _repo = repo;
        _reportService = reportService;
        _classifier = classifier;
        _evalRunner = evalRunner;
        _plog = plog;
    }

    public async Task<ExoplanetRunResult> RunAsync()
    {
        await _plog.Info("Pipeline starting.");

        // Step 1: Fetch from source
        var incoming = await _api.FetchExoplanetsAsync();
        await _plog.Info($"Fetched {incoming.Count} records from NASA.");

        // Step 2: Record that this run happened
        var run = await _repo.CreateIngestRunAsync(SourceName, SourceUrl, incoming.Count);
        await _plog.Info($"Ingest run {run.Id} created.", run.Id);

        try
        {
            // Step 3: Load existing planet names for diff
            var existingNames = await _repo.GetExistingPlanetNamesAsync();
            var existingPlanets = await _repo.GetAllPlanetsAsync();
            var existingByName = existingPlanets.ToDictionary(p => p.PlanetName, p => p);

            // Normalize incoming
            var normalized = incoming
                .Where(x => !string.IsNullOrWhiteSpace(x.PlanetName) && !string.IsNullOrWhiteSpace(x.HostStar))
                .DistinctBy(x => x.PlanetName!.Trim())
                .ToList();

            var changes = new List<ChangeLogEntity>();
            var now = DateTimeOffset.UtcNow;
            int newCount = 0, updatedCount = 0, unchangedCount = 0;

            foreach (var inc in normalized)
            {
                var planetName = inc.PlanetName!.Trim();
                var hostStar = inc.HostStar!.Trim();

                // Get or create solar system and star
                var system = await _repo.GetOrCreateSolarSystemAsync(
                    hostStar, inc.DistanceParsecs, inc.NumStars, inc.NumPlanets);

                var star = await _repo.GetOrCreateStarAsync(
                    system.Id, hostStar,
                    inc.StarTemperature, inc.StarRadius, inc.StarMass, inc.StarSpectralType);

                if (!existingByName.TryGetValue(planetName, out var existing))
                {
                    // INSERT
                    changes.Add(new ChangeLogEntity
                    {
                        IngestRunId = run.Id,
                        PlanetName = planetName,
                        ChangeType = "INSERT",
                        DetectedAt = now
                    });

                    var planet = new PlanetEntity
                    {
                        SolarSystemId = system.Id,
                        PlanetName = planetName,
                        DiscoveryYear = inc.DiscoveryYear,
                        DiscoveryMethod = inc.DiscoveryMethod,
                        PlanetRadius = inc.PlanetRadius,
                        PlanetMass = inc.PlanetMass,
                        OrbitalPeriod = inc.OrbitalPeriod,
                        SemiMajorAxis = inc.SemiMajorAxis,
                        Eccentricity = inc.Eccentricity,
                        EquilibriumTemp = inc.EquilibriumTemp,
                        PlanetDensity = inc.PlanetDensity,
                        InsolationFlux = inc.InsolationFlux,
                        CreatedUtc = now,
                        UpdatedUtc = now
                    };

                    await _repo.InsertPlanetAsync(planet);
                    await _repo.LinkPlanetToStarAsync(planet.Id, star.Id);
                    newCount++;
                }
                else
                {
                    // UPDATE check — field-level diff
                    var fieldChanges = CompareFields(inc, existing, run.Id, now);

                    if (fieldChanges.Count > 0)
                    {
                        changes.AddRange(fieldChanges);
                        ApplyUpdates(inc, existing, now);
                        await _repo.UpdatePlanetAsync(existing);
                        updatedCount++;
                    }
                    else
                    {
                        unchangedCount++;
                    }
                }
            }

            // DELETE detection
            var incomingNames = normalized.Select(x => x.PlanetName!.Trim()).ToHashSet();
            var deletedCount = 0;
            foreach (var ex in existingPlanets)
            {
                if (!incomingNames.Contains(ex.PlanetName))
                {
                    changes.Add(new ChangeLogEntity
                    {
                        IngestRunId = run.Id,
                        PlanetName = ex.PlanetName,
                        ChangeType = "DELETE",
                        DetectedAt = now
                    });
                    deletedCount++;
                }
            }

            // Step 4: Write evidence
            Console.WriteLine($"DEBUG: changes.Count = {changes.Count}");

            await _repo.WriteChangeLogAsync(changes);
            await _plog.Info($"Diff: {newCount} new, {updatedCount} updated, {deletedCount} deleted, {unchangedCount} unchanged.", run.Id);

            // Step 5: Mark run complete
            run.RowsNew = newCount;
            run.RowsUpdated = updatedCount;
            run.RowsDeleted = deletedCount;
            run.RowsUnchanged = unchangedCount;
            run.RowsFetched = incoming.Count;
            await _repo.CompleteIngestRunAsync(run);

            // Step 6: AI classifies
            if (changes.Count > 0)
            {
                try
                {
                    await _classifier.ClassifyAsync(run.Id);
                    await _plog.Info("AI classification complete.", run.Id);
                }
                catch (Exception ex)
                {
                    await _plog.Warning($"AI classification failed: {ex.Message}", run.Id);
                }
            }

            // Step 7: AI generates summary report
            if (changes.Count > 0)
            {
                try
                {
                    await _reportService.GenerateReportAsync(run.Id);
                    await _plog.Info("AI report generated.", run.Id);
                }
                catch (Exception ex)
                {
                    await _plog.Warning($"AI report failed: {ex.Message}", run.Id);
                }
            }

            // Step 8: Evaluate AI performance
            try
            {
                await _evalRunner.EvaluateAsync(run.Id);
                await _plog.Info("AI evaluation complete.", run.Id);
            }
            catch (Exception ex)
            {
                await _plog.Warning($"AI evaluation failed: {ex.Message}", run.Id);
            }

            await _plog.Info("Pipeline complete.", run.Id);

            return new ExoplanetRunResult
            {
                Fetched = incoming.Count,
                ValidIncoming = newCount + updatedCount + unchangedCount,
                Existing = updatedCount + unchangedCount,
                Inserted = newCount,
                Updated = updatedCount,
                Skipped = incoming.Count - (newCount + updatedCount + unchangedCount)
            };
        }
        catch (Exception ex)
        {
            await _plog.Error($"Pipeline failed: {ex.Message}", run.Id, ex);
            await _repo.FailIngestRunAsync(run, ex.Message);
            throw;
        }
    }

    private static List<ChangeLogEntity> CompareFields(
        ExoPlanet inc, PlanetEntity existing, int runId, DateTimeOffset now)
    {
        var changes = new List<ChangeLogEntity>();

        void Check(string field, string? oldVal, string? newVal)
        {
            if (oldVal != newVal)
                changes.Add(new ChangeLogEntity
                {
                    IngestRunId = runId,
                    PlanetName = existing.PlanetName,
                    ChangeType = "UPDATE",
                    FieldName = field,
                    OldValue = oldVal,
                    NewValue = newVal,
                    DetectedAt = now
                });
        }

        Check("discovery_year", existing.DiscoveryYear?.ToString(), inc.DiscoveryYear?.ToString());
        Check("discovery_method", existing.DiscoveryMethod, inc.DiscoveryMethod);
        Check("planet_radius", existing.PlanetRadius?.ToString(), inc.PlanetRadius?.ToString());
        Check("planet_mass", existing.PlanetMass?.ToString(), inc.PlanetMass?.ToString());
        Check("orbital_period", existing.OrbitalPeriod?.ToString(), inc.OrbitalPeriod?.ToString());
        Check("semi_major_axis", existing.SemiMajorAxis?.ToString(), inc.SemiMajorAxis?.ToString());
        Check("eccentricity", existing.Eccentricity?.ToString(), inc.Eccentricity?.ToString());
        Check("equilibrium_temp", existing.EquilibriumTemp?.ToString(), inc.EquilibriumTemp?.ToString());
        Check("planet_density", existing.PlanetDensity?.ToString(), inc.PlanetDensity?.ToString());
        Check("insolation_flux", existing.InsolationFlux?.ToString(), inc.InsolationFlux?.ToString());

        return changes;
    }

    private static void ApplyUpdates(ExoPlanet inc, PlanetEntity existing, DateTimeOffset now)
    {
        existing.DiscoveryYear = inc.DiscoveryYear;
        existing.DiscoveryMethod = inc.DiscoveryMethod;
        existing.PlanetRadius = inc.PlanetRadius;
        existing.PlanetMass = inc.PlanetMass;
        existing.OrbitalPeriod = inc.OrbitalPeriod;
        existing.SemiMajorAxis = inc.SemiMajorAxis;
        existing.Eccentricity = inc.Eccentricity;
        existing.EquilibriumTemp = inc.EquilibriumTemp;
        existing.PlanetDensity = inc.PlanetDensity;
        existing.InsolationFlux = inc.InsolationFlux;
        existing.UpdatedUtc = now;
    }
}
