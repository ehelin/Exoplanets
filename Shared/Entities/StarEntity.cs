namespace Exoplanet.Shared.Entities;

public class StarEntity
{
    public int Id { get; set; }
    public int SolarSystemId { get; set; }
    public string Name { get; set; } = null!;
    public double? TemperatureK { get; set; }
    public double? RadiusSolar { get; set; }
    public double? MassSolar { get; set; }
    public string? SpectralType { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }

    public SolarSystemEntity SolarSystem { get; set; } = null!;
}
