using Exoplanet.Shared.Entities;
using Exoplanet.Shared.Interfaces;
using Exoplanet.Shared.Models;
using Shared.Interfaces;
using Shared.Models;

namespace Exoplanet.Services;

public sealed class ExoplanetService : IExoplanetService
{
    private readonly IExoplanetApiClient _api;
    private readonly IExoplanetRepository _repo;

    private const string SourceName = "NASA_EXOPLANET_ARCHIVE";
    private const string SourceUrl = "https://exoplanetarchive.ipac.caltech.edu/TAP/sync?query=select+pl_name,hostname,disc_year+from+pscomppars&format=json";

    public ExoplanetService(IExoplanetApiClient api, IExoplanetRepository repo)
    {
        _api = api;
        _repo = repo;
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
    ///   8. [Phase 2.3 — AI explains diffs — not yet implemented]
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

            // Note: DELETE detection is logged but we don't delete rows.
            // NASA may temporarily drop records. The change_log records
            // the absence. A future workflow can handle actual deletion
            // if that's ever desired.

            // Step 7: Mark run complete
            run.RowsNew = diff.NewCount;
            run.RowsUpdated = diff.UpdatedCount;
            run.RowsDeleted = diff.DeletedCount;
            run.RowsUnchanged = diff.UnchangedCount;
            run.RowsFetched = incoming.Count;
            await _repo.CompleteIngestRunAsync(run);

            // Step 8: [Phase 2.3] AI explains diffs — TODO
            // if (diff.Changes.Count > 0)
            //     await _aiExplainer.GenerateReportAsync(run.Id, diff.Changes);

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
