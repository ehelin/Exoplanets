using System.Text;
using Exoplanet.Shared.Entities;
using Exoplanet.Shared.Interfaces;

namespace Exoplanet.Services.Prompts;

/// <summary>
/// Run 2 - Remove worked examples. Thresholds and CRITICAL warnings still present.
/// </summary>
public sealed class NoWorkedExamplesPromptProvider : IPromptProvider
{
    public string GetPrompt(List<PlanetEntity> planets)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an exoplanet classifier. For each planet below, provide TWO classifications:");
        sb.AppendLine();
        sb.AppendLine("1. DATA QUALITY classification:");
        sb.AppendLine("   - CONFIRMED: has discovery year (1992-2026), mass or radius, and orbital data");
        sb.AppendLine("   - CANDIDATE: missing key measurements (no mass, no radius, no orbital period)");
        sb.AppendLine("   - ANOMALY: data quality concern (discovery year outside range, suspicious values)");
        sb.AppendLine();
        sb.AppendLine("2. PLAVALOVA CODE — EXACTLY 4 characters: [mass][temp][ecc][density].");
        sb.AppendLine();
        sb.AppendLine("   MASS — look at mass_earth and apply STRICTLY:");
        sb.AppendLine("     m = mass_earth < 0.1");
        sb.AppendLine("     e = mass_earth >= 0.1 AND mass_earth < 10.0");
        sb.AppendLine("     N = mass_earth >= 10.0 AND mass_earth < 100.0");
        sb.AppendLine("     J = mass_earth >= 100.0 AND mass_earth < 4000.0");
        sb.AppendLine("     W = mass_earth >= 4000.0");
        sb.AppendLine("   CRITICAL: If mass_earth >= 10.0, the code is N, NOT e.");
        sb.AppendLine("     mass_earth=9.99 -> e");
        sb.AppendLine("     mass_earth=10.0 -> N");
        sb.AppendLine("     mass_earth=10.1 -> N");
        sb.AppendLine("     mass_earth=11.0 -> N");
        sb.AppendLine("     mass_earth=18.7 -> N");
        sb.AppendLine("     mass_earth=85.8 -> N");
        sb.AppendLine("     mass_earth=318 -> J");
        sb.AppendLine();
        sb.AppendLine("   TEMPERATURE — look at temp_k and apply STRICTLY:");
        sb.AppendLine("     F = temp_k < 200");
        sb.AppendLine("     W = temp_k >= 200 AND temp_k < 450");
        sb.AppendLine("     G = temp_k >= 450 AND temp_k < 1000");
        sb.AppendLine("     R = temp_k >= 1000");
        sb.AppendLine("   Use UPPERCASE only: F, W, G, R.");
        sb.AppendLine("     temp_k=419 -> W");
        sb.AppendLine("     temp_k=858 -> G");
        sb.AppendLine("     temp_k=900 -> G");
        sb.AppendLine("     temp_k=1655 -> R");
        sb.AppendLine();
        sb.AppendLine("   ECCENTRICITY — look at ecc value:");
        sb.AppendLine("     0 = ecc < 0.1");
        sb.AppendLine("     1 = ecc >= 0.1 AND ecc < 0.3");
        sb.AppendLine("     2 = ecc >= 0.3 AND ecc < 0.6");
        sb.AppendLine("     3 = ecc >= 0.6");
        sb.AppendLine("   Examples: ecc=0.000 -> 0, ecc=0.017 -> 0, ecc=0.080 -> 0, ecc=0.110 -> 1, ecc=0.450 -> 2");
        sb.AppendLine();
        sb.AppendLine("   DENSITY — look at density value in g/cm3:");
        sb.AppendLine("     g = density < 1.0");
        sb.AppendLine("     w = density >= 1.0 AND density < 3.0");
        sb.AppendLine("     t = density >= 3.0 AND density < 8.0");
        sb.AppendLine("     i = density >= 8.0 AND density < 15.0");
        sb.AppendLine("     s = density >= 15.0");
        sb.AppendLine();
        sb.AppendLine("   Use '?' for any component where the data is missing.");
        sb.AppendLine("   The code MUST be EXACTLY 4 characters. One letter/digit per component.");
        sb.AppendLine("   BEFORE RESPONDING, verify each code is exactly 4 characters.");
        sb.AppendLine();
        ProductionPromptProvider.AppendPlanets(sb, planets);
        return sb.ToString();
    }
}

/// <summary>
/// Run 3 - Remove CRITICAL warnings. Worked examples also gone. Thresholds still present.
/// </summary>
public sealed class NoCriticalWarningsPromptProvider : IPromptProvider
{
    public string GetPrompt(List<PlanetEntity> planets)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an exoplanet classifier. For each planet below, provide TWO classifications:");
        sb.AppendLine();
        sb.AppendLine("1. DATA QUALITY classification:");
        sb.AppendLine("   - CONFIRMED: has discovery year (1992-2026), mass or radius, and orbital data");
        sb.AppendLine("   - CANDIDATE: missing key measurements (no mass, no radius, no orbital period)");
        sb.AppendLine("   - ANOMALY: data quality concern (discovery year outside range, suspicious values)");
        sb.AppendLine();
        sb.AppendLine("2. PLAVALOVA CODE — EXACTLY 4 characters: [mass][temp][ecc][density].");
        sb.AppendLine();
        sb.AppendLine("   MASS — look at mass_earth and apply STRICTLY:");
        sb.AppendLine("     m = mass_earth < 0.1");
        sb.AppendLine("     e = mass_earth >= 0.1 AND mass_earth < 10.0");
        sb.AppendLine("     N = mass_earth >= 10.0 AND mass_earth < 100.0");
        sb.AppendLine("     J = mass_earth >= 100.0 AND mass_earth < 4000.0");
        sb.AppendLine("     W = mass_earth >= 4000.0");
        sb.AppendLine();
        sb.AppendLine("   TEMPERATURE — look at temp_k and apply STRICTLY:");
        sb.AppendLine("     F = temp_k < 200");
        sb.AppendLine("     W = temp_k >= 200 AND temp_k < 450");
        sb.AppendLine("     G = temp_k >= 450 AND temp_k < 1000");
        sb.AppendLine("     R = temp_k >= 1000");
        sb.AppendLine();
        sb.AppendLine("   ECCENTRICITY — look at ecc value:");
        sb.AppendLine("     0 = ecc < 0.1");
        sb.AppendLine("     1 = ecc >= 0.1 AND ecc < 0.3");
        sb.AppendLine("     2 = ecc >= 0.3 AND ecc < 0.6");
        sb.AppendLine("     3 = ecc >= 0.6");
        sb.AppendLine();
        sb.AppendLine("   DENSITY — look at density value in g/cm3:");
        sb.AppendLine("     g = density < 1.0");
        sb.AppendLine("     w = density >= 1.0 AND density < 3.0");
        sb.AppendLine("     t = density >= 3.0 AND density < 8.0");
        sb.AppendLine("     i = density >= 8.0 AND density < 15.0");
        sb.AppendLine("     s = density >= 15.0");
        sb.AppendLine();
        sb.AppendLine("   Use '?' for any component where the data is missing.");
        sb.AppendLine("   The code MUST be EXACTLY 4 characters.");
        sb.AppendLine();
        ProductionPromptProvider.AppendPlanets(sb, planets);
        return sb.ToString();
    }
}

/// <summary>
/// Run 4 - Soften STRICTLY to approximately. No CRITICAL warnings, no worked examples.
/// </summary>
public sealed class SoftenedLanguagePromptProvider : IPromptProvider
{
    public string GetPrompt(List<PlanetEntity> planets)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an exoplanet classifier. For each planet below, provide TWO classifications:");
        sb.AppendLine();
        sb.AppendLine("1. DATA QUALITY classification:");
        sb.AppendLine("   - CONFIRMED: has discovery year (1992-2026), mass or radius, and orbital data");
        sb.AppendLine("   - CANDIDATE: missing key measurements (no mass, no radius, no orbital period)");
        sb.AppendLine("   - ANOMALY: data quality concern (discovery year outside range, suspicious values)");
        sb.AppendLine();
        sb.AppendLine("2. PLAVALOVA CODE — approximately 4 characters: [mass][temp][ecc][density].");
        sb.AppendLine();
        sb.AppendLine("   MASS — look at mass_earth and apply approximately:");
        sb.AppendLine("     m = mass_earth < 0.1");
        sb.AppendLine("     e = mass_earth >= 0.1 AND mass_earth < 10.0");
        sb.AppendLine("     N = mass_earth >= 10.0 AND mass_earth < 100.0");
        sb.AppendLine("     J = mass_earth >= 100.0 AND mass_earth < 4000.0");
        sb.AppendLine("     W = mass_earth >= 4000.0");
        sb.AppendLine();
        sb.AppendLine("   TEMPERATURE — look at temp_k and apply approximately:");
        sb.AppendLine("     F = temp_k < 200");
        sb.AppendLine("     W = temp_k >= 200 AND temp_k < 450");
        sb.AppendLine("     G = temp_k >= 450 AND temp_k < 1000");
        sb.AppendLine("     R = temp_k >= 1000");
        sb.AppendLine();
        sb.AppendLine("   ECCENTRICITY — look at ecc value:");
        sb.AppendLine("     0 = ecc < 0.1");
        sb.AppendLine("     1 = ecc >= 0.1 AND ecc < 0.3");
        sb.AppendLine("     2 = ecc >= 0.3 AND ecc < 0.6");
        sb.AppendLine("     3 = ecc >= 0.6");
        sb.AppendLine();
        sb.AppendLine("   DENSITY — look at density value in g/cm3:");
        sb.AppendLine("     g = density < 1.0");
        sb.AppendLine("     w = density >= 1.0 AND density < 3.0");
        sb.AppendLine("     t = density >= 3.0 AND density < 8.0");
        sb.AppendLine("     i = density >= 8.0 AND density < 15.0");
        sb.AppendLine("     s = density >= 15.0");
        sb.AppendLine();
        sb.AppendLine("   Use '?' for any component where the data is missing.");
        sb.AppendLine();
        ProductionPromptProvider.AppendPlanets(sb, planets);
        return sb.ToString();
    }
}

/// <summary>
/// Run 5 - Remove boundary examples. Softened language. Thresholds still numeric.
/// </summary>
public sealed class NoBoundaryExamplesPromptProvider : IPromptProvider
{
    public string GetPrompt(List<PlanetEntity> planets)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an exoplanet classifier. For each planet below, provide TWO classifications:");
        sb.AppendLine();
        sb.AppendLine("1. DATA QUALITY classification:");
        sb.AppendLine("   - CONFIRMED: has discovery year, mass or radius, and orbital data");
        sb.AppendLine("   - CANDIDATE: missing key measurements");
        sb.AppendLine("   - ANOMALY: data quality concern");
        sb.AppendLine();
        sb.AppendLine("2. PLAVALOVA CODE — 4 characters: [mass][temp][ecc][density].");
        sb.AppendLine();
        sb.AppendLine("   MASS:  m < 0.1,  e 0.1-10,  N 10-100,  J 100-4000,  W 4000+  (Earth masses)");
        sb.AppendLine("   TEMP:  F < 200,  W 200-450,  G 450-1000,  R 1000+  (Kelvin)");
        sb.AppendLine("   ECC:   0 < 0.1,  1 0.1-0.3,  2 0.3-0.6,  3 0.6+");
        sb.AppendLine("   DENSITY: g < 1,  w 1-3,  t 3-8,  i 8-15,  s 15+  (g/cm3)");
        sb.AppendLine();
        sb.AppendLine("   Use '?' for missing data.");
        sb.AppendLine();
        ProductionPromptProvider.AppendPlanets(sb, planets);
        return sb.ToString();
    }
}

/// <summary>
/// Run 6 - Vague density thresholds. Mass and temperature still numeric.
/// </summary>
public sealed class VagueDensityPromptProvider : IPromptProvider
{
    public string GetPrompt(List<PlanetEntity> planets)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an exoplanet classifier. For each planet below, provide TWO classifications:");
        sb.AppendLine();
        sb.AppendLine("1. DATA QUALITY classification:");
        sb.AppendLine("   - CONFIRMED: has discovery year, mass or radius, and orbital data");
        sb.AppendLine("   - CANDIDATE: missing key measurements");
        sb.AppendLine("   - ANOMALY: data quality concern");
        sb.AppendLine();
        sb.AppendLine("2. PLAVALOVA CODE — 4 characters: [mass][temp][ecc][density].");
        sb.AppendLine();
        sb.AppendLine("   MASS:  m < 0.1,  e 0.1-10,  N 10-100,  J 100-4000,  W 4000+  (Earth masses)");
        sb.AppendLine("   TEMP:  F < 200,  W 200-450,  G 450-1000,  R 1000+  (Kelvin)");
        sb.AppendLine("   ECC:   0 < 0.1,  1 0.1-0.3,  2 0.3-0.6,  3 0.6+");
        sb.AppendLine("   DENSITY: g = very low density gas giant, w = water/ice density, t = rocky like Earth, i = iron-heavy, s = super-dense exotic");
        sb.AppendLine();
        sb.AppendLine("   Use '?' for missing data.");
        sb.AppendLine();
        ProductionPromptProvider.AppendPlanets(sb, planets);
        return sb.ToString();
    }
}

/// <summary>
/// Run 7 - Vague temperature thresholds. Mass still numeric, density and temperature vague.
/// </summary>
public sealed class VagueTemperaturePromptProvider : IPromptProvider
{
    public string GetPrompt(List<PlanetEntity> planets)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an exoplanet classifier. For each planet below, provide TWO classifications:");
        sb.AppendLine();
        sb.AppendLine("1. DATA QUALITY classification:");
        sb.AppendLine("   - CONFIRMED: has discovery year, mass or radius, and orbital data");
        sb.AppendLine("   - CANDIDATE: missing key measurements");
        sb.AppendLine("   - ANOMALY: data quality concern");
        sb.AppendLine();
        sb.AppendLine("2. PLAVALOVA CODE — 4 characters: [mass][temp][ecc][density].");
        sb.AppendLine();
        sb.AppendLine("   MASS:  m < 0.1,  e 0.1-10,  N 10-100,  J 100-4000,  W 4000+  (Earth masses)");
        sb.AppendLine("   TEMP:  F = frozen, W = possibly habitable water zone, G = hot gas conditions, R = roasting extreme heat");
        sb.AppendLine("   ECC:   0 < 0.1,  1 0.1-0.3,  2 0.3-0.6,  3 0.6+");
        sb.AppendLine("   DENSITY: g = very low density gas giant, w = water/ice density, t = rocky like Earth, i = iron-heavy, s = super-dense exotic");
        sb.AppendLine();
        sb.AppendLine("   Use '?' for missing data.");
        sb.AppendLine();
        ProductionPromptProvider.AppendPlanets(sb, planets);
        return sb.ToString();
    }
}

/// <summary>
/// Run 8 - Vague mass thresholds. All three main dimensions now vague.
/// </summary>
public sealed class VagueMassPromptProvider : IPromptProvider
{
    public string GetPrompt(List<PlanetEntity> planets)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an exoplanet classifier. For each planet below, provide TWO classifications:");
        sb.AppendLine();
        sb.AppendLine("1. DATA QUALITY classification:");
        sb.AppendLine("   - CONFIRMED: has discovery year, mass or radius, and orbital data");
        sb.AppendLine("   - CANDIDATE: missing key measurements");
        sb.AppendLine("   - ANOMALY: data quality concern");
        sb.AppendLine();
        sb.AppendLine("2. PLAVALOVA CODE — 4 characters: [mass][temp][ecc][density].");
        sb.AppendLine();
        sb.AppendLine("   MASS:  m = Mercury-like tiny, e = Earth-like, N = Neptune-like, J = Jupiter-like, W = super massive");
        sb.AppendLine("   TEMP:  F = frozen, W = possibly habitable water zone, G = hot gas conditions, R = roasting extreme heat");
        sb.AppendLine("   ECC:   0 < 0.1,  1 0.1-0.3,  2 0.3-0.6,  3 0.6+");
        sb.AppendLine("   DENSITY: g = very low density gas giant, w = water/ice density, t = rocky like Earth, i = iron-heavy, s = super-dense exotic");
        sb.AppendLine();
        sb.AppendLine("   Use '?' for missing data.");
        sb.AppendLine();
        ProductionPromptProvider.AppendPlanets(sb, planets);
        return sb.ToString();
    }
}

/// <summary>
/// Run 9 - Remove 4-character format enforcement and verification step.
/// </summary>
public sealed class NoFormatEnforcementPromptProvider : IPromptProvider
{
    public string GetPrompt(List<PlanetEntity> planets)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an exoplanet classifier. For each planet below, provide TWO classifications:");
        sb.AppendLine();
        sb.AppendLine("1. DATA QUALITY classification: CONFIRMED, CANDIDATE, or ANOMALY.");
        sb.AppendLine();
        sb.AppendLine("2. PLAVALOVA CODE using [mass][temp][ecc][density].");
        sb.AppendLine();
        sb.AppendLine("   MASS:  m = Mercury-like tiny, e = Earth-like, N = Neptune-like, J = Jupiter-like, W = super massive");
        sb.AppendLine("   TEMP:  F = frozen, W = possibly habitable water zone, G = hot gas conditions, R = roasting extreme heat");
        sb.AppendLine("   ECC:   roughly circular = 0, slightly elliptical = 1, moderate = 2, highly elliptical = 3");
        sb.AppendLine("   DENSITY: g = gas giant, w = water density, t = terrestrial rocky, i = iron-heavy, s = super-dense");
        sb.AppendLine();
        ProductionPromptProvider.AppendPlanets(sb, planets);
        return sb.ToString();
    }
}

/// <summary>
/// Run 10 - Bare prompt. Labels only, no numbers, no examples, no enforcement.
/// </summary>
public sealed class BarePromptProvider : IPromptProvider
{
    public string GetPrompt(List<PlanetEntity> planets)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an exoplanet classifier.");
        sb.AppendLine();
        sb.AppendLine("For each planet, provide:");
        sb.AppendLine("1. DATA QUALITY: CONFIRMED, CANDIDATE, or ANOMALY");
        sb.AppendLine("2. PLAVALOVA CODE: a 4-character code representing mass, temperature, eccentricity, and density.");
        sb.AppendLine("   Mass: m=tiny, e=Earth-like, N=Neptune-like, J=Jupiter-like, W=massive");
        sb.AppendLine("   Temp: F=frozen, W=warm, G=hot, R=very hot");
        sb.AppendLine("   Eccentricity: 0=circular, 1=low, 2=moderate, 3=high");
        sb.AppendLine("   Density: g=gas, w=water, t=rocky, i=iron, s=super-dense");
        sb.AppendLine();
        ProductionPromptProvider.AppendPlanets(sb, planets);
        return sb.ToString();
    }
}
