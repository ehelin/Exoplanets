using Exoplanet.Shared.Interfaces;
using Exoplanet.Shared.Models;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Exoplanet.Services;

public class ExoplanetApiClient : IExoplanetApiClient
{
    private readonly HttpClient _http;

    private const string RelativeUrl =
        "TAP/sync?query=select+pl_name,hostname,disc_year+from+pscomppars&format=json";

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
                PlanetName = el.TryGetProperty("pl_name", out var pn) && pn.ValueKind != JsonValueKind.Null
                    ? pn.GetString()
                    : null,

                HostStar = el.TryGetProperty("hostname", out var hs) && hs.ValueKind != JsonValueKind.Null
                    ? hs.GetString()
                    : null,

                DiscoveryYear = el.TryGetProperty("disc_year", out var dy) && dy.ValueKind == JsonValueKind.Number
                    ? dy.GetInt32()
                    : null
            });
        }

        return exoplanets;
    }
}
