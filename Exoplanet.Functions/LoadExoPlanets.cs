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

    // TODO: Change back to TimerTrigger for production
    // [Function("ExoplanetDaily")]
    // public async Task RunAsync([TimerTrigger("0 10 2 * * *")] TimerInfo timer, CancellationToken ct)

    //[Function("ExoplanetDaily")]
    //public async Task<HttpResponseData> RunAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req, CancellationToken ct)
    //{

    [Function("ExoplanetDaily")]
    public async Task RunAsync(
        [TimerTrigger("*/5 * * * * *", RunOnStartup = true)] TimerInfo timer,
        CancellationToken ct)
    {
        _log.LogInformation("ExoplanetDaily start");

        var result = await _svc.RunAsync();

        _log.LogInformation(
            "ExoplanetDaily done. fetched={fetched} inserted={inserted} updated={updated} skipped={skipped}",
            result.Fetched, result.Inserted, result.Updated, result.Skipped);

        //var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
       // await response.WriteStringAsync($"Fetched={result.Fetched}, Inserted={result.Inserted}, Updated={result.Updated}, Skipped={result.Skipped}");
        //return response;
    }
}
