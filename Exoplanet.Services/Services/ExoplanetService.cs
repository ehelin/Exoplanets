using Exoplanet.Shared.Entities;
using Exoplanet.Shared.Interfaces;
using Exoplanet.Shared.Models;
using Microsoft.Extensions.Logging;
using Shared.Interfaces;
using Shared.Models;

namespace Exoplanet.Services;

public sealed class ExoplanetService : IExoplanetService
{
    private readonly IExoplanetApiClient _api;
    private readonly IExoplanetRepository _repo;
    private readonly IChangeReportService _reportService;
    private readonly IPipelineLogger _plog;
    private readonly ILogger<ExoplanetService> _log;

    private const string SourceName = "NASA_EXOPLANET_ARCHIVE";
    private const string SourceUrl = "https://exoplanetarchive.ipac.caltech.edu/TAP/sync?query=select+pl_name,hostname,disc_year+from+pscomppars&format=json";

    public ExoplanetService(
        IExoplanetApiClient api,
        IExoplanetRepository repo,
        IChangeReportService reportService,
        IPipelineLogger plog,
        ILogger<ExoplanetService> log)
    {
        _api = api;
        _repo = repo;
        _reportService = reportService;
        _plog = plog;
        _log = log;
    }

    public async Task<ExoplanetRunResult> RunAsync()
    {
        await _plog.Info("Pipeline starting.");

        var incoming = await _api.FetchExoplanetsAsync();
        await _plog.Info($"Fetched {incoming.Count} records from NASA.");

        var run = await _repo.CreateIngestRunAsync(SourceName, SourceUrl, incoming.Count);
        await _plog.Info($"Ingest run {run.Id} created.", run.Id);

        try
        {
            var existing = await _repo.GetAllExistingAsync();
            var diff = DiffEngine.ComputeDiffs(incoming, existing, run.Id);
            await _plog.Info($"Diff: {diff.NewCount} new, {diff.UpdatedCount} updated, {diff.DeletedCount} deleted, {diff.UnchangedCount} unchanged.", run.Id);

            await _repo.WriteChangeLogAsync(diff.Changes);

            if (diff.ToInsert.Count > 0)
                await _repo.InsertNewAsync(diff.ToInsert);

            if (diff.ToUpdate.Count > 0)
                await _repo.UpdateExistingAsync(diff.ToUpdate);

            run.RowsNew = diff.NewCount;
            run.RowsUpdated = diff.UpdatedCount;
            run.RowsDeleted = diff.DeletedCount;
            run.RowsUnchanged = diff.UnchangedCount;
            run.RowsFetched = incoming.Count;
            await _repo.CompleteIngestRunAsync(run);

            if (diff.Changes.Count > 0)
            {
                try
                {
                    await _reportService.GenerateReportAsync(run.Id);
                    await _plog.Info("AI report generated.", run.Id);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "AI report generation failed for run {RunId}.", run.Id);
                    await _plog.Warning($"AI report failed: {ex.Message}", run.Id);
                }
            }

            await _plog.Info("Pipeline complete.", run.Id);

            return new ExoplanetRunResult
            {
                Fetched = incoming.Count,
                ValidIncoming = diff.NewCount + diff.UpdatedCount + diff.UnchangedCount,
                Existing = diff.UnchangedCount + diff.UpdatedCount,
                Inserted = diff.NewCount,
                Updated = diff.UpdatedCount,
                Skipped = incoming.Count - (diff.NewCount + diff.UpdatedCount + diff.UnchangedCount)
            };
        }
        catch (Exception ex)
        {
            await _plog.Error($"Pipeline failed: {ex.Message}", run.Id, ex);
            await _repo.FailIngestRunAsync(run, ex.Message);
            throw;
        }
    }
}