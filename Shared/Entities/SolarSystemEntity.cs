namespace Exoplanet.Shared.Entities;

public class SolarSystemEntity
{
    public int Id { get; set; }
    public double? DistanceParsecs { get; set; }
    public int? NumStars { get; set; }
    public int? NumPlanets { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}
