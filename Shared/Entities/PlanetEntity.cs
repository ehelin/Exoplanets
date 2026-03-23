namespace Exoplanet.Shared.Entities;

public class PlanetEntity
{
    public int Id { get; set; }
    public int SolarSystemId { get; set; }
    public string PlanetName { get; set; } = null!;
    public int? DiscoveryYear { get; set; }
    public string? DiscoveryMethod { get; set; }
    public double? PlanetRadius { get; set; }       // Earth radii
    public double? PlanetMass { get; set; }          // Earth masses
    public double? OrbitalPeriod { get; set; }       // days
    public double? SemiMajorAxis { get; set; }       // AU
    public double? Eccentricity { get; set; }
    public double? EquilibriumTemp { get; set; }     // Kelvin
    public double? PlanetDensity { get; set; }       // g/cm³
    public double? InsolationFlux { get; set; }      // Earth flux
    public string? Classification { get; set; }
    public string? PlavalovaCode { get; set; }
    public string? HabitabilityScore { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }

    public SolarSystemEntity SolarSystem { get; set; } = null!;
}
