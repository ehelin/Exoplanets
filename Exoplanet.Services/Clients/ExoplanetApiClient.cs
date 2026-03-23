using Exoplanet.Shared.Interfaces;
using Exoplanet.Shared.Models;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Exoplanet.Services;

public class ExoplanetApiClient : IExoplanetApiClient
{
    private readonly HttpClient _http;

    private const string RelativeUrl =
        "TAP/sync?query=select+pl_name,hostname,disc_year,discoverymethod,"
        + "pl_bmasse,pl_rade,pl_orbper,pl_orbsmax,pl_orbeccen,pl_eqt,pl_dens,pl_insol,"
        + "st_teff,st_rad,st_mass,st_spectype,"
        + "sy_dist,sy_snum,sy_pnum"
        + "+from+pscomppars&format=json";

    public ExoplanetApiClient(HttpClient http)
    {
        _http = http;
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<List<ExoPlanet>> FetchExoplanetsAsync()
    {
        using var resp = await _http.GetAsync(RelativeUrl);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);

        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Unexpected JSON shape: expected array.");

        var exoplanets = new List<ExoPlanet>(doc.RootElement.GetArrayLength());

        foreach (var el in doc.RootElement.EnumerateArray())
        {
            exoplanets.Add(new ExoPlanet
            {
                PlanetName = GetString(el, "pl_name"),
                HostStar = GetString(el, "hostname"),
                DiscoveryYear = GetInt(el, "disc_year"),
                DiscoveryMethod = GetString(el, "discoverymethod"),
                PlanetMass = GetDouble(el, "pl_bmasse"),
                PlanetRadius = GetDouble(el, "pl_rade"),
                OrbitalPeriod = GetDouble(el, "pl_orbper"),
                SemiMajorAxis = GetDouble(el, "pl_orbsmax"),
                Eccentricity = GetDouble(el, "pl_orbeccen"),
                EquilibriumTemp = GetDouble(el, "pl_eqt"),
                PlanetDensity = GetDouble(el, "pl_dens"),
                InsolationFlux = GetDouble(el, "pl_insol"),
                StarTemperature = GetDouble(el, "st_teff"),
                StarRadius = GetDouble(el, "st_rad"),
                StarMass = GetDouble(el, "st_mass"),
                StarSpectralType = GetString(el, "st_spectype"),
                DistanceParsecs = GetDouble(el, "sy_dist"),
                NumStars = GetInt(el, "sy_snum"),
                NumPlanets = GetInt(el, "sy_pnum")
            });
        }

        return exoplanets;
    }

    private static string? GetString(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Null
            ? v.GetString()
            : null;

    private static int? GetInt(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32()
            : null;

    private static double? GetDouble(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : null;
}
