using Exoplanet.Services;
using Exoplanet.Shared.Interfaces;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Exoplanet.Functions;

public sealed class LoadExoPlanets
{
    private readonly IExoplanetService _svc;
    private readonly ILogger<LoadExoPlanets> _log;

    public LoadExoPlanets(IExoplanetService svc, ILogger<LoadExoPlanets> log)
    {
        _svc = svc;
        _log = log;
    }

    [Function("ExoplanetDaily")]
    public async Task RunAsync([TimerTrigger("0 0 10 * * *", RunOnStartup = true)] TimerInfo timer, CancellationToken ct)
    {
        _log.LogInformation("ExoplanetDaily start");

        var result = await _svc.RunAsync();

        _log.LogInformation(
            "ExoplanetDaily done. fetched={fetched} inserted={inserted} updated={updated} skipped={skipped}",
            result.Fetched, result.Inserted, result.Updated, result.Skipped);
    }
}
