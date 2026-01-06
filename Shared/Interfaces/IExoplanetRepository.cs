using Exoplanet.Shared.Entities;

namespace Shared.Interfaces
{
    public interface IExoplanetRepository
    {
        Task<HashSet<(string PlanetName, string HostStar)>> GetExistingKeysAsync();
        Task<int> InsertNewAsync(List<ExoplanetEntity> newEntities);
    }
}
