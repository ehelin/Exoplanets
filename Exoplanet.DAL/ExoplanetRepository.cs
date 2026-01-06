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
}