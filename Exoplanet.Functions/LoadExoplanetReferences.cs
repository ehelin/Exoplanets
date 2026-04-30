using Exoplanet.Shared.Interfaces;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Exoplanet.Functions;

public class LoadExoplanetReferences
{
    private readonly IRagIngestionService _ragIngestion;
    private readonly ILogger<LoadExoplanetReferences> _log;

    public LoadExoplanetReferences(IRagIngestionService ragIngestion, ILogger<LoadExoplanetReferences> log)
    {
        _ragIngestion = ragIngestion;
        _log = log;
    }

    [Function("LoadExoplanetReferences")]
    public async Task RunAsync([TimerTrigger("0 0 8 * * 1", RunOnStartup = true)] TimerInfo timer, CancellationToken ct)
    {
        _log.LogInformation("RAG ingestion start");
        await _ragIngestion.IngestReferencesAsync();
        _log.LogInformation("RAG ingestion complete");
    }
}