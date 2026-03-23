namespace Exoplanet.Shared.Models;

public sealed class ExoPlanet
{
    // Planet
    public string? PlanetName { get; init; }
    public int? DiscoveryYear { get; init; }
    public string? DiscoveryMethod { get; init; }
    public double? PlanetRadius { get; init; }
    public double? PlanetMass { get; init; }
    public double? OrbitalPeriod { get; init; }
    public double? SemiMajorAxis { get; init; }
    public double? Eccentricity { get; init; }
    public double? EquilibriumTemp { get; init; }
    public double? PlanetDensity { get; init; }
    public double? InsolationFlux { get; init; }

    // Star
    public string? HostStar { get; init; }
    public double? StarTemperature { get; init; }
    public double? StarRadius { get; init; }
    public double? StarMass { get; init; }
    public string? StarSpectralType { get; init; }

    // System
    public double? DistanceParsecs { get; init; }
    public int? NumStars { get; init; }
    public int? NumPlanets { get; init; }
}
