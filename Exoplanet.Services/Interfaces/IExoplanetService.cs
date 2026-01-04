namespace Exoplanet.Services;

public interface IExoplanetService
{
    Task<ExoplanetRunResult> RunAsync(CancellationToken ct);
}

public sealed record ExoplanetRunResult(int Fetched, int Inserted, int Updated, int Skipped);
