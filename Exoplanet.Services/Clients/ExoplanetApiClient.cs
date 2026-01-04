using System.Net.Http.Headers;
using System.Text.Json;

namespace Exoplanet.Services;

public sealed class ExoplanetApiClient : IExoplanetApiClient
{
    private readonly HttpClient _http;

    private const string RelativeUrl =
        "TAP/sync?query=select+*+from+pscomppars&format=json";

    public ExoplanetApiClient(HttpClient http)
    {
        _http = http;
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async IAsyncEnumerable<JsonElement> FetchPsCompParsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, RelativeUrl);
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Unexpected JSON shape: expected an array.");

        foreach (var item in doc.RootElement.EnumerateArray())
            yield return item;
    }
}
