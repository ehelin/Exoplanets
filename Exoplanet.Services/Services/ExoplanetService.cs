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
    private readonly ILogger<ExoplanetService> _log;

    private const string SourceName = "NASA_EXOPLANET_ARCHIVE";
    private const string SourceUrl = "https://exoplanetarchive.ipac.caltech.edu/TAP/sync?query=select+pl_name,hostname,disc_year+from+pscomppars&format=json";

    public ExoplanetService(
        IExoplanetApiClient api,
        IExoplanetRepository repo,
        IChangeReportService reportService,
        ILogger<ExoplanetService> log)
    {
        _api = api;
        _repo = repo;
        _reportService = reportService;
        _log = log;
    }

    /// <summary>
    /// Phase 2 pipeline — evidence-first ordering:
    ///   1. Fetch from NASA
    ///   2. Create ingest_run record (RUNNING)
    ///   3. Load existing state from DB
    ///   4. Compute diffs (deterministic, no AI)
    ///   5. Write diffs to change_log
    ///   6. Apply mutations to exoplanets table
    ///   7. Complete ingest_run (COMPLETED)
    ///   8. AI explains diffs → change_report
    /// </summary>
    public async Task<ExoplanetRunResult> RunAsync()
    {
        // Step 1: Fetch from source
        var incoming = await _api.FetchExoplanetsAsync();

        // Step 2: Record that this run happened
        var run = await _repo.CreateIngestRunAsync(SourceName, SourceUrl, incoming.Count);

        try
        {
            // Step 3: Load current state
            var existing = await _repo.GetAllExistingAsync();

            // Step 4: Compute diffs — deterministic, code-only
            var diff = DiffEngine.ComputeDiffs(incoming, existing, run.Id);

            // Step 5: Write evidence BEFORE mutating
            await _repo.WriteChangeLogAsync(diff.Changes);

            // Step 6: Now apply mutations
            if (diff.ToInsert.Count > 0)
                await _repo.InsertNewAsync(diff.ToInsert);

            if (diff.ToUpdate.Count > 0)
                await _repo.UpdateExistingAsync(diff.ToUpdate);

            // Step 7: Mark run complete
            run.RowsNew = diff.NewCount;
            run.RowsUpdated = diff.UpdatedCount;
            run.RowsDeleted = diff.DeletedCount;
            run.RowsUnchanged = diff.UnchangedCount;
            run.RowsFetched = incoming.Count;
            await _repo.CompleteIngestRunAsync(run);

            // Step 8: AI explains what changed — Phase 2.3
            if (diff.Changes.Count > 0)
            {
                try
                {
                    await _reportService.GenerateReportAsync(run.Id);
                }
                catch (Exception ex)
                {
                    // AI failure should not fail the pipeline.
                    // The evidence is already recorded. The narrative is optional.
                    _log.LogWarning(ex, "AI report generation failed for run {RunId}. Evidence is intact.", run.Id);
                }
            }

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
            await _repo.FailIngestRunAsync(run, ex.Message);
            throw;
        }
    }
}
