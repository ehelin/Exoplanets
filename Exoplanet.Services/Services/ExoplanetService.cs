using Exoplanet.DAL;
using Exoplanet.Shared.Entities;
using Exoplanet.Shared.Interfaces;
using Exoplanet.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Shared.Interfaces;
using Shared.Models;

namespace Exoplanet.Services;

public sealed class ExoplanetService : IExoplanetService
{
    private readonly IExoplanetApiClient _api;
    private readonly IExoplanetRepository _repo;

    public ExoplanetService(IExoplanetApiClient api, IExoplanetRepository repo)
    {
        _api = api;
        _repo = repo;
    }

    public async Task<ExoplanetRunResult> RunAsync()
    {
        var incoming = await _api.FetchExoplanetsAsync();

        var normalized = incoming
            .Where(x => !string.IsNullOrWhiteSpace(x.PlanetName) && !string.IsNullOrWhiteSpace(x.HostStar))
            .Select(x => new
            {
                PlanetName = x.PlanetName!.Trim(),
                HostStar = x.HostStar!.Trim(),
                DiscoveryYear = x.DiscoveryYear
            })
            .DistinctBy(x => (x.PlanetName, x.HostStar))
            .ToList();

        // DAL owns reads
        var existingSet = await _repo.GetExistingKeysAsync();

        var toInsert = new List<ExoplanetEntity>(capacity: Math.Max(0, normalized.Count - existingSet.Count));

        foreach (var x in normalized)
        {
            if (existingSet.Contains((x.PlanetName, x.HostStar)))
                continue;

            toInsert.Add(new ExoplanetEntity
            {
                PlanetName = x.PlanetName,
                HostStar = x.HostStar,
                DiscoveryYear = x.DiscoveryYear,
                CreatedUtc = DateTimeOffset.UtcNow,
                UpdatedUtc = DateTimeOffset.UtcNow
            });
        }

        // DAL owns writes
        var inserted = await _repo.InsertNewAsync(toInsert);

        return new ExoplanetRunResult
        {
            Fetched = incoming.Count,
            ValidIncoming = normalized.Count,
            Existing = normalized.Count - toInsert.Count,
            Inserted = inserted,
            Skipped = incoming.Count - toInsert.Count
        };
    }
}
