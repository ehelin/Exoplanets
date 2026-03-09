using Exoplanet.Shared.Entities;

namespace Shared.Interfaces
{
    public interface IExoplanetRepository
    {
        // Phase 1
        Task<HashSet<(string PlanetName, string HostStar)>> GetExistingKeysAsync();
        Task<int> InsertNewAsync(List<ExoplanetEntity> newEntities);

        // Phase 2: Full record access for diff
        Task<List<ExoplanetEntity>> GetAllExistingAsync();
        Task UpdateExistingAsync(List<ExoplanetEntity> updatedEntities);

        // Phase 2: Evidence
        Task<IngestRunEntity> CreateIngestRunAsync(string source, string? sourceUrl, int rowsFetched);
        Task CompleteIngestRunAsync(IngestRunEntity run);
        Task FailIngestRunAsync(IngestRunEntity run, string errorMessage);
        Task WriteChangeLogAsync(List<ChangeLogEntity> entries);
        Task<List<ChangeLogEntity>> GetChangeLogByRunAsync(int ingestRunId);
        Task WriteChangeReportAsync(ChangeReportEntity report);
    }
}
