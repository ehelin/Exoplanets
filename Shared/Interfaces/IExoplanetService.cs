using Exoplanet.Shared.Models;
using Shared.Models;

namespace Exoplanet.Shared.Interfaces;

public interface IExoplanetService
{
    Task<ExoplanetRunResult> RunAsync();
}

