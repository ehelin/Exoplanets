using Exoplanet.Shared.Entities;

namespace Shared.Interfaces
{
    public interface IExoplanetRepository
    {
        // Planet reads
        Task<List<PlanetEntity>> GetAllPlanetsAsync();
        Task<HashSet<string>> GetExistingPlanetNamesAsync();
        Task<PlanetEntity?> GetPlanetByNameAsync(string planetName);

        // Domain writes
        Task<SolarSystemEntity> GetOrCreateSolarSystemAsync(
            string hostStar, double? distanceParsecs, int? numStars, int? numPlanets);
        Task<StarEntity> GetOrCreateStarAsync(
            int solarSystemId, string name, double? tempK, double? radius, double? mass, string? spectralType);
        Task InsertPlanetAsync(PlanetEntity planet);
        Task UpdatePlanetAsync(PlanetEntity planet);
        Task LinkPlanetToStarAsync(int planetId, int starId);

        // Bulk
        Task BulkInsertSolarSystemsAsync(List<SolarSystemEntity> systems);
        Task BulkInsertStarsAsync(List<StarEntity> stars);
        Task BulkInsertPlanetsAsync(List<PlanetEntity> planets);
        Task BulkInsertPlanetStarsAsync(List<PlanetStarEntity> links);

        // Evidence
        Task<IngestRunEntity> CreateIngestRunAsync(string source, string? sourceUrl, int rowsFetched);
        Task CompleteIngestRunAsync(IngestRunEntity run);
        Task FailIngestRunAsync(IngestRunEntity run, string errorMessage);
        Task WriteChangeLogAsync(List<ChangeLogEntity> entries);
        Task<List<ChangeLogEntity>> GetChangeLogByRunAsync(int ingestRunId);
        Task WriteChangeReportAsync(ChangeReportEntity report);
        Task WriteEvalResultAsync(EvalResultEntity result);
        Task WriteEvalResultsAsync(List<EvalResultEntity> results);

        // Drift detection
        Task<double?> GetAverageEvalScoreAsync(int ingestRunId, string evalType);
        Task<List<int>> GetRecentCompletedRunIdsAsync(int excludeRunId, int count);

        // Classification
        Task ApplyClassificationsAsync(
            List<ChangeLogEntity> changes,
            Dictionary<string, (string Classification, string Reasoning)> classifications);
    }
}
