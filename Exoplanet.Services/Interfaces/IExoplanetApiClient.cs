using System.Text.Json;

namespace Exoplanet.Services;

public interface IExoplanetApiClient
{
    IAsyncEnumerable<JsonElement> FetchPsCompParsAsync(CancellationToken ct);
}