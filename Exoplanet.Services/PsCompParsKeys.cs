using System.Text.Json.Serialization;

namespace Exoplanet.Services;

public sealed class PsCompParsKeys
{
    [JsonPropertyName("pl_name")]
    public string? PlName { get; set; }

    [JsonPropertyName("hostname")]
    public string? Hostname { get; set; }

    [JsonPropertyName("disc_year")]
    public int? DiscYear { get; set; }
}