namespace Exoplanet.DAL;

public sealed class ExoplanetRaw
{
    public long Id { get; set; }

    public string PlName { get; set; } = "";
    public string Hostname { get; set; } = "";
    public int? DiscYear { get; set; }

    public string RowHash { get; set; } = "";
    public DateTime IngestedAtUtc { get; set; }

    // Store full record as JSONB
    public string PayloadJson { get; set; } = "{}";
}
