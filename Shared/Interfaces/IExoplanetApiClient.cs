using Exoplanet.Shared.Models;
using System.Text.Json;

namespace Exoplanet.Shared.Interfaces;

public interface IExoplanetApiClient
{
    Task<List<ExoPlanet>> FetchExoplanetsAsync();
}