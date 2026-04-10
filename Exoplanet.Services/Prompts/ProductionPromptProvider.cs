using System.Text;
using Exoplanet.Shared.Entities;
using Exoplanet.Shared.Interfaces;

namespace Exoplanet.Services.Prompts;

/// <summary>
/// Run 1 - Full production prompt. All thresholds, CRITICAL warnings, worked examples, format enforcement.
/// </summary>
public sealed class ProductionPromptProvider : IPromptProvider
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
        sb.AppendLine();
        sb.AppendLine("   WORKED EXAMPLES:");
        sb.AppendLine("     mass=1.0, temp=255, ecc=0.017, density=5.51 -> eW0t");
        sb.AppendLine("     mass=317.8, temp=110, ecc=0.049, density=1.33 -> JF0w");
        sb.AppendLine("     mass=18.7, temp=419, ecc=0.000, density=1.10 -> NW0w");
        sb.AppendLine("     mass=85.8, temp=900, ecc=0.110, density=0.35 -> NG1g");
        sb.AppendLine("     mass=11.0, temp=858, ecc=0.000, density=1.65 -> NG0w");
        sb.AppendLine("     mass=10.1, temp=1655, ecc=0.000, density=1.78 -> NR0w");
        sb.AppendLine();
        sb.AppendLine("   The code MUST be EXACTLY 4 characters. One letter/digit per component.");
        sb.AppendLine("   Do NOT include numeric values in the code.");
        sb.AppendLine("   WRONG: N900G1g  RIGHT: NG1g");
        sb.AppendLine("   WRONG: e255W0t  RIGHT: eW0t");
        sb.AppendLine("   WRONG: N0w (only 3 chars)  RIGHT: NW0w (4 chars)");
        sb.AppendLine();
        sb.AppendLine("   BEFORE RESPONDING, verify each code is exactly 4 characters.");
        sb.AppendLine();
        AppendPlanets(sb, planets);
        return sb.ToString();
    }

    internal static void AppendPlanets(StringBuilder sb, List<PlanetEntity> planets)
    {
        sb.AppendLine("Respond ONLY with a JSON array. No markdown, no backticks.");
        sb.AppendLine("Each element: {\"planet_name\": \"...\", \"classification\": \"...\", \"plavalova_code\": \"...\", \"reasoning\": \"...\"}");
        sb.AppendLine("Keep reasoning to one sentence max.");
        sb.AppendLine();
        sb.AppendLine("--- PLANETS ---");
        foreach (var p in planets)
        {
            sb.Append($"name={p.PlanetName}");
            sb.Append($", disc_year={p.DiscoveryYear?.ToString() ?? "?"}");
            sb.Append($", method={p.DiscoveryMethod ?? "?"}");
            sb.Append($", mass_earth={p.PlanetMass?.ToString("F2") ?? "?"}");
            sb.Append($", radius_earth={p.PlanetRadius?.ToString("F2") ?? "?"}");
            sb.Append($", period_days={p.OrbitalPeriod?.ToString("F2") ?? "?"}");
            sb.Append($", ecc={p.Eccentricity?.ToString("F3") ?? "?"}");
            sb.Append($", temp_k={p.EquilibriumTemp?.ToString("F0") ?? "?"}");
            sb.Append($", density={p.PlanetDensity?.ToString("F2") ?? "?"}");
            sb.Append($", insol={p.InsolationFlux?.ToString("F2") ?? "?"}");
            sb.AppendLine();
        }
        sb.AppendLine("--- END PLANETS ---");
    }
}
