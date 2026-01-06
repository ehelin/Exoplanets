namespace Exoplanet.Shared.Entities;

public class ExoplanetEntity
{
    public long ExoplanetId { get; set; }

    public string PlanetName { get; set; } = null!;
    public string HostStar { get; set; } = null!;
    public int? DiscoveryYear { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}
