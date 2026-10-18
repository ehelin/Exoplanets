# Post 1 — Reconnaissance Report

## 1. File Tree

### Shared\
```
Shared.csproj
Entities/
  AgentHandoff.cs
  AtmosphereEntity.cs
  ChangeLogEntity.cs
  ChangeReportEntity.cs
  EvalResultEntity.cs
  ExoplanetEntity.cs
  ExoplanetReferenceEntity.cs
  IngestRunEntity.cs
  PipelineLogEntity.cs
  PlanetEntity.cs
  PlanetStarEntity.cs
  RetrievalLogEntity.cs
  SolarSystemEntity.cs
  StarEntity.cs
Interfaces/
  IAgent.cs
  IAgentBoundary.cs
  IChangeClassifierService.cs
  IChangeReportService.cs
  IEvalRunner.cs
  IExoplanetApiClient.cs
  IExoplanetRepository.cs
  IExoplanetService.cs
  IPipelineLogger.cs
  IPromptProvider.cs
  IRagService.cs
Models/
  DiffResult.cs
  Exoplanet.cs
  ExoplanetRunResult.cs
```

### Exoplanet.Services\
```
Exoplanet.Services.csproj
PsCompParsKeys.cs
Clients/
  ExoplanetApiClient.cs
Prompts/
  ClassificationPromptBuilder.cs
  DegradedPromptProviders.cs
  ProductionPromptProvider.cs
Services/
  AgentBoundary.cs
  Agents/              (empty subdirectory — no files at depth 2)
  ChangeClassifierService.cs
  ChangeReportService.cs
  ClassifierAgent.cs
  DiffEngine.cs
  EvalRunner.cs
  ExoplanetService.cs
  NarratorAgent.cs
  PipelineLogger.cs
  RagIngestionService.cs
  RagRetrievalService.cs
  ServicesServiceCollectionExtensions.cs
```

### Exoplanet.DAL\
```
Exoplanet.DAL.csproj
DalServiceCollectionExtensions.cs
ExoplanetDbContext.cs
ExoplanetRepository.cs
VectorDbContext.cs
```

### Exoplanet.Console\
```
Exoplanet.Console.csproj
Program.cs
appsettings.json
appsettings.Development.json
appsettings.Production.json
```

---

## 2. File Contents

### Shared/Interfaces/IAgent.cs
```csharp
namespace Exoplanet.Shared.Interfaces;

public interface IAgent
{
    string AgentName { get; }
    Task ExecuteAsync(int ingestRunId);
}
```

### Shared/Interfaces/IAgentBoundary.cs
```csharp
using Exoplanet.Shared.Entities;

namespace Exoplanet.Shared.Interfaces;

public interface IAgentBoundary
{
    Task RecordHandoffAsync(
        int ingestId,
        string agentName,
        string inputSummary,
        string outputSummary);

    Task<AgentHandoff?> ReadPreviousHandoffAsync(
        int ingestId,
        string previousAgentName);
}
```

### Shared/Interfaces/IChangeClassifierService.cs
```csharp
namespace Exoplanet.Services;

public interface IChangeClassifierService
{
    Task<Dictionary<string, (string Classification, string Reasoning)>> ClassifyAsync(int ingestRunId);
}
```

### Shared/Models/ClassificationStrategy.cs
**MISSING** — file does not exist anywhere in the solution.

### Exoplanet.Services/Services/ClassifierAgent.cs
```csharp
using System.Text.Json;
using Exoplanet.Shared.Interfaces;
using Shared.Interfaces;

namespace Exoplanet.Services.Services;

public sealed class ClassifierAgent : IAgent
{
    private readonly IChangeClassifierService _classifier;
    private readonly IAgentBoundary _boundary;
    private readonly IExoplanetRepository _repo;
    private readonly IPipelineLogger _plog;

    public string AgentName => "classifier";

    public ClassifierAgent(
        IChangeClassifierService classifier,
        IAgentBoundary boundary,
        IExoplanetRepository repo,
        IPipelineLogger plog)
    {
        _classifier = classifier;
        _boundary = boundary;
        _repo = repo;
        _plog = plog;
    }

    public async Task ExecuteAsync(int ingestRunId)
    {
        // What the agent received
        var changes = await _repo.GetChangeLogByRunAsync(ingestRunId);
        var inputSummary = $"{changes.Count} change_log rows for run {ingestRunId}";

        // Do the job (service is doing the thinking)
        var classifications = await _classifier.ClassifyAsync(ingestRunId);

        // What the agent produced
        var outputSummary = JsonSerializer.Serialize(
            classifications.Select(c => new
            {
                planet = c.Key,
                classification = c.Value.Classification,
                reasoning = c.Value.Reasoning
            }));

        // Log the boundary crossing
        await _boundary.RecordHandoffAsync(
            ingestRunId,
            AgentName,
            inputSummary,
            outputSummary);

        await _plog.Info($"{AgentName} agent complete.", ingestRunId);
    }
}
```

### Exoplanet.Services/Services/NarratorAgent.cs
```csharp
using Exoplanet.Shared.Interfaces;
using Shared.Interfaces;

namespace Exoplanet.Services.Services;

public sealed class NarratorAgent : IAgent
{
    private readonly IChangeReportService _reportService;
    private readonly IAgentBoundary _boundary;
    private readonly IPipelineLogger _plog;

    public string AgentName => "narrator";

    public NarratorAgent(
        IChangeReportService reportService,
        IAgentBoundary boundary,
        IPipelineLogger plog)
    {
        _reportService = reportService;
        _boundary = boundary;
        _plog = plog;
    }

    public async Task ExecuteAsync(int ingestRunId)
    {
        // Read the classifier's handoff — this is the only input the narrator sees
        var classifierHandoff = await _boundary.ReadPreviousHandoffAsync(ingestRunId, "classifier");

        if (classifierHandoff == null)
        {
            await _plog.Warning($"{AgentName} agent: no classifier handoff for run {ingestRunId}, skipping.", ingestRunId);
            return;
        }

        // Do the job (service is doing the thinking)
        var reportText = await _reportService.GenerateReportAsync(ingestRunId);

        // Log the boundary crossing — input is what the classifier passed us
        await _boundary.RecordHandoffAsync(
            ingestRunId,
            AgentName,
            classifierHandoff.OutputSummary,
            reportText);

        await _plog.Info($"{AgentName} agent complete.", ingestRunId);
    }
}
```

### Exoplanet.Services/Services/AgentBoundary.cs
```csharp
using Exoplanet.DAL;
using Exoplanet.Shared.Entities;
using Exoplanet.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Exoplanet.Services.Services;

public sealed class AgentBoundary : IAgentBoundary
{
    private readonly ExoplanetDbContext _db;
    private readonly IPipelineLogger _plog;

    public AgentBoundary(ExoplanetDbContext db, IPipelineLogger plog)
    {
        _db = db;
        _plog = plog;
    }

    public async Task RecordHandoffAsync(
        int ingestId,
        string agentName,
        string inputSummary,
        string outputSummary)
    {
        var handoff = new AgentHandoff
        {
            IngestId = ingestId,
            AgentName = agentName,
            InputSummary = inputSummary,
            OutputSummary = outputSummary,
            HandoffTimestamp = DateTime.UtcNow
        };

        _db.AgentHandoffs.Add(handoff);
        await _db.SaveChangesAsync();

        await _plog.Info($"Handoff recorded: agent={agentName}, ingestId={ingestId}");
    }

    public async Task<AgentHandoff?> ReadPreviousHandoffAsync(
        int ingestId,
        string previousAgentName)
    {
        return await _db.AgentHandoffs
            .Where(h => h.IngestId == ingestId && h.AgentName == previousAgentName)
            .OrderByDescending(h => h.HandoffTimestamp)
            .FirstOrDefaultAsync();
    }
}
```

### Exoplanet.Services/Services/ChangeClassifierService.cs
```csharp
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Exoplanet.Services.Prompts;
using Exoplanet.Shared.Entities;
using Exoplanet.Shared.Interfaces;
using Microsoft.Extensions.Configuration;
using Shared.Interfaces;

namespace Exoplanet.Services;

public sealed class ChangeClassifierService : IChangeClassifierService
{
    #region Constructor / Class Variables
    private readonly HttpClient _http;
    private readonly IExoplanetRepository _repo;
    private readonly IPipelineLogger _plog;
    private readonly string _model;
    private const int BatchSize = 50;
    private readonly IRagRetrievalService _ragRetrievalService;

    public ChangeClassifierService(
        HttpClient http,
        IExoplanetRepository repo,
        IPipelineLogger plog,
        IConfiguration config,
        IRagRetrievalService ragRetrievalService)
    {
        _http = http;
        _repo = repo;
        _plog = plog;
        _model = config["OpenAI:Model"] ?? "gpt-4o-mini";

        _ragRetrievalService = ragRetrievalService;

        var apiKey = config["OpenAI:ApiKey"];
        if (!string.IsNullOrEmpty(apiKey))
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }
    #endregion

    public async Task<Dictionary<string, (string Classification, string Reasoning)>> ClassifyAsync(int ingestRunId)
    {
        var allClassifications = new Dictionary<string, (string Classification, string Reasoning)>(
            StringComparer.OrdinalIgnoreCase);

        var changes = await _repo.GetChangeLogByRunAsync(ingestRunId);

        if (changes.Count == 0)
        {
            await _plog.Info("No changes to classify.", ingestRunId);
            return allClassifications;
        }

        var planets = await _repo.GetAllPlanetsAsync();
        var planetLookup = planets.ToDictionary(p => p.PlanetName, p => p);

        await _plog.Info($"Classifying {changes.Count} changes in batches of {BatchSize}.", ingestRunId);

        var batches = changes
            .Select((c, i) => new { Change = c, Index = i })
            .GroupBy(x => x.Index / BatchSize)
            .Select(g => g.Select(x => x.Change).ToList())
            .ToList();

        int batchNum = 0;
        foreach (var batch in batches)
        {
            batchNum++;
            await _plog.Info($"Processing batch {batchNum}/{batches.Count} ({batch.Count} changes).", ingestRunId);

            try
            {
                var prompt = await BuildClassificationPromptAsync(batch, planetLookup, ingestRunId);

                var (responseText, tokensUsed) = await CallOpenAiAsync(prompt);
                var classifications = ParseClassifications(responseText);

                await _repo.ApplyClassificationsAsync(batch, classifications);

                foreach (var kvp in classifications)
                    allClassifications[kvp.Key] = kvp.Value;

                await _plog.Info($"Batch {batchNum} classified. Tokens: {tokensUsed}.", ingestRunId);
            }
            catch (Exception ex)
            {
                await _plog.Warning($"Batch {batchNum} classification failed: {ex.Message}", ingestRunId);
            }
        }

        await _plog.Info("Classification complete.", ingestRunId);
        return allClassifications;
    }

    /// <summary>
    /// Production prompt build path. Retrieves RAG context per planet, then hands
    /// everything to the shared ClassificationPromptBuilder. The test path
    /// (RagComparisonTests) calls the same builder directly.
    /// </summary>
    private async Task<string> BuildClassificationPromptAsync(
        List<ChangeLogEntity> batch,
        Dictionary<string, PlanetEntity> planetLookup,
        int ingestRunId)
    {
        // Resolve batch -> planet entities (skip any without a matching planet)
        var planetsInBatch = new List<PlanetEntity>();
        foreach (var c in batch)
        {
            if (planetLookup.TryGetValue(c.PlanetName, out var p))
                planetsInBatch.Add(p);
        }

        // Retrieve RAG context per planet using a narrative-style query.
        // (A numerical query like "mass=X temp=Y" pulls measurement strings;
        // a narrative query pulls discovery papers and findings.)
        var ragContext = new Dictionary<string, List<RetrievedReference>>();
        foreach (var p in planetsInBatch)
        {
            var description =
                $"What is scientifically notable about {p.PlanetName}? " +
                $"Discovery papers, instruments used, atmospheric findings.";

            var refs = await _ragRetrievalService.RetrieveAsync(p.PlanetName, description, ingestRunId);
            if (refs.Count > 0)
                ragContext[p.PlanetName] = refs;
        }

        return ClassificationPromptBuilder.Build(planetsInBatch, ragContext);
    }

    private static Dictionary<string, (string Classification, string Reasoning)> ParseClassifications(string responseText)
    {
        var result = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var cleaned = responseText.Trim();
            if (cleaned.StartsWith("```"))
            {
                cleaned = cleaned.Replace("```json", "").Replace("```", "").Trim();
            }

            using var doc = JsonDocument.Parse(cleaned);
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var name = element.GetProperty("planet_name").GetString();
                var classification = element.GetProperty("classification").GetString();
                var reasoning = element.GetProperty("reasoning").GetString();

                var plavalova = element.TryGetProperty("plavalova_code", out var pc) ? pc.GetString() : null;
                var fullClassification = plavalova != null
                    ? $"{classification}|{plavalova}"
                    : classification ?? "";

                if (name != null && classification != null)
                {
                    result[name] = (fullClassification, reasoning ?? "");
                }
            }
        }
        catch (JsonException) { }

        return result;
    }

    private async Task<(string responseText, int? tokensUsed)> CallOpenAiAsync(string prompt)
    {
        var requestBody = new
        {
            model = _model,
            messages = new[] { new { role = "user", content = prompt } },
            max_tokens = 16000,
            temperature = 0.1
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync("https://api.openai.com/v1/chat/completions", content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);

        var text = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";

        int? tokensUsed = null;
        if (doc.RootElement.TryGetProperty("usage", out var usage) &&
            usage.TryGetProperty("total_tokens", out var tokens))
        {
            tokensUsed = tokens.GetInt32();
        }

        return (text, tokensUsed);
    }
}
```

### Exoplanet.Services/Services/ExoplanetService.cs
```csharp
using Exoplanet.Services.Services;
using Exoplanet.Shared.Entities;
using Exoplanet.Shared.Interfaces;
using Exoplanet.Shared.Models;
using Shared.Interfaces;
using Shared.Models;

namespace Exoplanet.Services;

public sealed class ExoplanetService : IExoplanetService
{
    #region Constructor / Class Varibles
    private readonly IExoplanetApiClient _api;
    private readonly IExoplanetRepository _repo;
    private readonly ClassifierAgent _classifierAgent;
    private readonly NarratorAgent _narratorAgent;
    private readonly IEvalRunner _evalRunner;
    private readonly IPipelineLogger _plog;
    private readonly IRagIngestionService _rag;

    private const string SourceName = "NASA_EXOPLANET_ARCHIVE";
    private const string SourceUrl = "https://exoplanetarchive.ipac.caltech.edu/TAP/sync?query=select+pl_name,hostname,disc_year,discoverymethod,pl_bmasse,pl_rade,pl_orbper,pl_orbsmax,pl_orbeccen,pl_eqt,pl_dens,pl_insol,st_teff,st_rad,st_mass,st_spectype,sy_dist,sy_snum,sy_pnum+from+pscomppars&format=json";

    public ExoplanetService(
        IExoplanetApiClient api,
        IExoplanetRepository repo,
        ClassifierAgent classifierAgent,
        NarratorAgent narratorAgent,
        IEvalRunner evalRunner,
        IPipelineLogger plog,
        IRagIngestionService rag)
    {
        _api = api;
        _repo = repo;
        _classifierAgent = classifierAgent;
        _narratorAgent = narratorAgent;
        _evalRunner = evalRunner;
        _plog = plog;
        _rag = rag;
    }
    #endregion

    public async Task<ExoplanetRunResult> RunAsync()
    {
        await _plog.Info("Pipeline starting.");

        var incoming = await FetchFromSourceAsync();
        incoming = incoming.Where(x => x.PlanetName.ToLower() == "hd 189733 b").ToList();

        var run = await _repo.CreateIngestRunAsync(SourceName, SourceUrl, incoming.Count);
        await _plog.Info($"Ingest run {run.Id} created.", run.Id);

        try
        {
            var (changes, newCount, updatedCount, deletedCount, unchangedCount) =
                await IngestAndDiffAsync(incoming, run.Id);

            await _repo.WriteChangeLogAsync(changes);
            await _plog.Info($"Diff: {newCount} new, {updatedCount} updated, {deletedCount} deleted, {unchangedCount} unchanged.", run.Id);

            await CompleteRunAsync(run, incoming.Count, newCount, updatedCount, deletedCount, unchangedCount);

            // RAG: ingest references for all planets before classification
            await RunRagIngestionAsync();

            if (changes.Count > 0)
            {
                await RunAgentAsync(_classifierAgent, run.Id);
                await RunAgentAsync(_narratorAgent, run.Id);
            }

            await RunEvalAsync(run.Id);
            await RunDriftCheckAsync(run.Id);
            await RunLlmJudgeAsync(run.Id);

            await _plog.Info("Pipeline complete.", run.Id);

            return new ExoplanetRunResult
            {
                Fetched = incoming.Count,
                ValidIncoming = newCount + updatedCount + unchangedCount,
                Existing = updatedCount + unchangedCount,
                Inserted = newCount,
                Updated = updatedCount,
                Skipped = incoming.Count - (newCount + updatedCount + unchangedCount)
            };
        }
        catch (Exception ex)
        {
            await _plog.Error($"Pipeline failed: {ex.Message}", run.Id, ex);
            await _repo.FailIngestRunAsync(run, ex.Message);
            throw;
        }
    }

    private async Task RunAgentAsync(IAgent agent, int runId)
    {
        try
        {
            await agent.ExecuteAsync(runId);
        }
        catch (Exception ex)
        {
            await _plog.Warning($"{agent.AgentName} agent failed: {ex.Message}", runId);
        }
    }

    private async Task RunRagIngestionAsync()
    {
        try
        {
            await _rag.IngestReferencesAsync();
            await _plog.Info("RAG ingestion complete.");
        }
        catch (Exception ex)
        {
            await _plog.Warning($"RAG ingestion failed: {ex.Message}");
        }
    }

    private async Task RunLlmJudgeAsync(int runId)
    {
        try
        {
            await _evalRunner.RunLlmJudgeAsync(runId);
            await _plog.Info("LLM Judge evaluation complete.", runId);
        }
        catch (Exception ex)
        {
            await _plog.Warning($"LLM Judge evaluation failed: {ex.Message}", runId);
        }
    }

    private async Task<List<ExoPlanet>> FetchFromSourceAsync()
    {
        var incoming = await _api.FetchExoplanetsAsync();
        await _plog.Info($"Fetched {incoming.Count} records from NASA.");
        return incoming;
    }

    private async Task<(List<ChangeLogEntity> changes, int newCount, int updatedCount, int deletedCount, int unchangedCount)>
        IngestAndDiffAsync(List<ExoPlanet> incoming, int runId)
    {
        var existingPlanets = await _repo.GetAllPlanetsAsync();
        var existingByName = existingPlanets.ToDictionary(p => p.PlanetName, p => p);

        var normalized = incoming
            .Where(x => !string.IsNullOrWhiteSpace(x.PlanetName) && !string.IsNullOrWhiteSpace(x.HostStar))
            .DistinctBy(x => x.PlanetName!.Trim())
            .ToList();

        var changes = new List<ChangeLogEntity>();
        var now = DateTimeOffset.UtcNow;
        int newCount = 0, updatedCount = 0, unchangedCount = 0;

        foreach (var inc in normalized)
        {
            var planetName = inc.PlanetName!.Trim();
            var hostStar = inc.HostStar!.Trim();

            var system = await _repo.GetOrCreateSolarSystemAsync(
                hostStar, inc.DistanceParsecs, inc.NumStars, inc.NumPlanets);

            var star = await _repo.GetOrCreateStarAsync(
                system.Id, hostStar,
                inc.StarTemperature, inc.StarRadius, inc.StarMass, inc.StarSpectralType);

            if (!existingByName.TryGetValue(planetName, out var existing))
            {
                changes.Add(new ChangeLogEntity
                {
                    IngestRunId = runId,
                    PlanetName = planetName,
                    ChangeType = "INSERT",
                    DetectedAt = now
                });

                var planet = new PlanetEntity
                {
                    SolarSystemId = system.Id,
                    PlanetName = planetName,
                    DiscoveryYear = inc.DiscoveryYear,
                    DiscoveryMethod = inc.DiscoveryMethod,
                    PlanetRadius = inc.PlanetRadius,
                    PlanetMass = inc.PlanetMass,
                    OrbitalPeriod = inc.OrbitalPeriod,
                    SemiMajorAxis = inc.SemiMajorAxis,
                    Eccentricity = inc.Eccentricity,
                    EquilibriumTemp = inc.EquilibriumTemp,
                    PlanetDensity = inc.PlanetDensity,
                    InsolationFlux = inc.InsolationFlux,
                    CreatedUtc = now,
                    UpdatedUtc = now
                };

                await _repo.InsertPlanetAsync(planet);
                await _repo.LinkPlanetToStarAsync(planet.Id, star.Id);
                newCount++;
            }
            else
            {
                var fieldChanges = CompareFields(inc, existing, runId, now);

                if (fieldChanges.Count > 0)
                {
                    changes.AddRange(fieldChanges);
                    ApplyUpdates(inc, existing, now);
                    await _repo.UpdatePlanetAsync(existing);
                    updatedCount++;
                }
                else
                {
                    unchangedCount++;
                }
            }
        }

        var incomingNames = normalized.Select(x => x.PlanetName!.Trim()).ToHashSet();
        var deletedCount = 0;
        foreach (var ex in existingPlanets)
        {
            if (!incomingNames.Contains(ex.PlanetName))
            {
                changes.Add(new ChangeLogEntity
                {
                    IngestRunId = runId,
                    PlanetName = ex.PlanetName,
                    ChangeType = "DELETE",
                    DetectedAt = now
                });
                deletedCount++;
            }
        }

        return (changes, newCount, updatedCount, deletedCount, unchangedCount);
    }

    private async Task CompleteRunAsync(IngestRunEntity run, int fetched, int newCount, int updatedCount, int deletedCount, int unchangedCount)
    {
        run.RowsNew = newCount;
        run.RowsUpdated = updatedCount;
        run.RowsDeleted = deletedCount;
        run.RowsUnchanged = unchangedCount;
        run.RowsFetched = fetched;
        await _repo.CompleteIngestRunAsync(run);
    }

    private async Task RunEvalAsync(int runId)
    {
        try
        {
            await _evalRunner.EvaluateAsync(runId);
            await _plog.Info("AI evaluation complete.", runId);
        }
        catch (Exception ex)
        {
            await _plog.Warning($"AI evaluation failed: {ex.Message}", runId);
        }
    }

    private async Task RunDriftCheckAsync(int runId)
    {
        try
        {
            await _evalRunner.CheckForDriftAsync(runId);
        }
        catch (Exception ex)
        {
            await _plog.Warning($"Drift check failed: {ex.Message}", runId);
        }
    }

    private static List<ChangeLogEntity> CompareFields(
        ExoPlanet inc, PlanetEntity existing, int runId, DateTimeOffset now)
    {
        var changes = new List<ChangeLogEntity>();

        void Check(string field, string? oldVal, string? newVal)
        {
            if (oldVal != newVal)
                changes.Add(new ChangeLogEntity
                {
                    IngestRunId = runId,
                    PlanetName = existing.PlanetName,
                    ChangeType = "UPDATE",
                    FieldName = field,
                    OldValue = oldVal,
                    NewValue = newVal,
                    DetectedAt = now
                });
        }

        Check("discovery_year", existing.DiscoveryYear?.ToString(), inc.DiscoveryYear?.ToString());
        Check("discovery_method", existing.DiscoveryMethod, inc.DiscoveryMethod);
        Check("planet_radius", existing.PlanetRadius?.ToString(), inc.PlanetRadius?.ToString());
        Check("planet_mass", existing.PlanetMass?.ToString(), inc.PlanetMass?.ToString());
        Check("orbital_period", existing.OrbitalPeriod?.ToString(), inc.OrbitalPeriod?.ToString());
        Check("semi_major_axis", existing.SemiMajorAxis?.ToString(), inc.SemiMajorAxis?.ToString());
        Check("eccentricity", existing.Eccentricity?.ToString(), inc.Eccentricity?.ToString());
        Check("equilibrium_temp", existing.EquilibriumTemp?.ToString(), inc.EquilibriumTemp?.ToString());
        Check("planet_density", existing.PlanetDensity?.ToString(), inc.PlanetDensity?.ToString());
        Check("insolation_flux", existing.InsolationFlux?.ToString(), inc.InsolationFlux?.ToString());

        return changes;
    }

    private static void ApplyUpdates(ExoPlanet inc, PlanetEntity existing, DateTimeOffset now)
    {
        existing.DiscoveryYear = inc.DiscoveryYear;
        existing.DiscoveryMethod = inc.DiscoveryMethod;
        existing.PlanetRadius = inc.PlanetRadius;
        existing.PlanetMass = inc.PlanetMass;
        existing.OrbitalPeriod = inc.OrbitalPeriod;
        existing.SemiMajorAxis = inc.SemiMajorAxis;
        existing.Eccentricity = inc.Eccentricity;
        existing.EquilibriumTemp = inc.EquilibriumTemp;
        existing.PlanetDensity = inc.PlanetDensity;
        existing.InsolationFlux = inc.InsolationFlux;
        existing.UpdatedUtc = now;
    }
}
```

### Exoplanet.Services/ServicesServiceCollectionExtensions.cs
*(actual path: `Exoplanet.Services/Services/ServicesServiceCollectionExtensions.cs`)*
```csharp
using Exoplanet.DAL;
using Exoplanet.Services.Services;
using Exoplanet.Shared.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Shared.Interfaces;

namespace Exoplanet.Services;

public static class ServicesServiceCollectionExtensions
{
    public static IServiceCollection AddExoplanetServices(
        this IServiceCollection services, string connectionString, string? vectorConnectionString = null)
    {
        services.AddExoplanetDal(connectionString, vectorConnectionString);

        services.AddHttpClient<IExoplanetApiClient, ExoplanetApiClient>(http =>
        {
            http.BaseAddress = new Uri("https://exoplanetarchive.ipac.caltech.edu/");
            http.Timeout = TimeSpan.FromMinutes(10);
        });
        services.AddHttpClient<IChangeReportService, ChangeReportService>();
        services.AddHttpClient<IChangeClassifierService, ChangeClassifierService>(http =>
        {
            http.Timeout = TimeSpan.FromMinutes(5);
        });
        services.AddScoped<IPipelineLogger, PipelineLogger>();
        services.AddScoped<IExoplanetService, ExoplanetService>();
        services.AddScoped<IExoplanetRepository, ExoplanetRepository>();
        services.AddHttpClient<IEvalRunner, EvalRunner>();
        services.AddHttpClient<IRagIngestionService, RagIngestionService>();
        services.AddHttpClient<IRagRetrievalService, RagRetrievalService>();
        services.AddScoped<IAgentBoundary, AgentBoundary>();

        // Agents (Phase 5.1)
        services.AddScoped<ClassifierAgent>();
        services.AddScoped<NarratorAgent>();

        return services;
    }
}
```

### Exoplanet.DAL/ExoplanetDbContext.cs
```csharp
using Microsoft.EntityFrameworkCore;
using Exoplanet.Shared.Entities;

namespace Exoplanet.DAL;

public sealed class ExoplanetDbContext : DbContext
{
    public ExoplanetDbContext(DbContextOptions<ExoplanetDbContext> options)
        : base(options) { }

    // Domain
    public DbSet<SolarSystemEntity> SolarSystems => Set<SolarSystemEntity>();
    public DbSet<StarEntity> Stars => Set<StarEntity>();
    public DbSet<PlanetEntity> Planets => Set<PlanetEntity>();
    public DbSet<PlanetStarEntity> PlanetStars => Set<PlanetStarEntity>();
    public DbSet<AtmosphereEntity> Atmospheres => Set<AtmosphereEntity>();

    // Audit & Evidence
    public DbSet<IngestRunEntity> IngestRuns => Set<IngestRunEntity>();
    public DbSet<ChangeLogEntity> ChangeLogs => Set<ChangeLogEntity>();
    public DbSet<ChangeReportEntity> ChangeReports => Set<ChangeReportEntity>();
    public DbSet<PipelineLogEntity> PipelineLogs => Set<PipelineLogEntity>();
    public DbSet<EvalResultEntity> EvalResults => Set<EvalResultEntity>();

    public DbSet<RetrievalLogEntity> RetrievalLogs => Set<RetrievalLogEntity>();
    public DbSet<AgentHandoff> AgentHandoffs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        const string schema = "exoplanet";

        // ── SolarSystems ───────────────────────────────────────
        modelBuilder.Entity<SolarSystemEntity>(e =>
        {
            e.ToTable("solar_systems", schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.DistanceParsecs).HasColumnName("distance_parsecs");
            e.Property(x => x.NumStars).HasColumnName("num_stars");
            e.Property(x => x.NumPlanets).HasColumnName("num_planets");
            e.Property(x => x.CreatedUtc).HasColumnName("created_utc");
            e.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");
        });

        // ── Stars ──────────────────────────────────────────────
        modelBuilder.Entity<StarEntity>(e =>
        {
            e.ToTable("stars", schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.SolarSystemId).HasColumnName("solar_system_id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(256).IsRequired();
            e.Property(x => x.TemperatureK).HasColumnName("temperature_k");
            e.Property(x => x.RadiusSolar).HasColumnName("radius_solar");
            e.Property(x => x.MassSolar).HasColumnName("mass_solar");
            e.Property(x => x.SpectralType).HasColumnName("spectral_type").HasMaxLength(50);
            e.Property(x => x.CreatedUtc).HasColumnName("created_utc");
            e.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");
            e.HasOne(x => x.SolarSystem).WithMany().HasForeignKey(x => x.SolarSystemId);
        });

        // ── Planets ────────────────────────────────────────────
        modelBuilder.Entity<PlanetEntity>(e =>
        {
            e.ToTable("planets", schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.SolarSystemId).HasColumnName("solar_system_id");
            e.Property(x => x.PlanetName).HasColumnName("planet_name").HasMaxLength(256).IsRequired();
            e.Property(x => x.DiscoveryYear).HasColumnName("discovery_year");
            e.Property(x => x.DiscoveryMethod).HasColumnName("discovery_method").HasMaxLength(100);
            e.Property(x => x.PlanetRadius).HasColumnName("planet_radius");
            e.Property(x => x.PlanetMass).HasColumnName("planet_mass");
            e.Property(x => x.OrbitalPeriod).HasColumnName("orbital_period");
            e.Property(x => x.SemiMajorAxis).HasColumnName("semi_major_axis");
            e.Property(x => x.Eccentricity).HasColumnName("eccentricity");
            e.Property(x => x.EquilibriumTemp).HasColumnName("equilibrium_temp");
            e.Property(x => x.PlanetDensity).HasColumnName("planet_density");
            e.Property(x => x.InsolationFlux).HasColumnName("insolation_flux");
            e.Property(x => x.Classification).HasColumnName("classification").HasMaxLength(50);
            e.Property(x => x.PlavalovaCode).HasColumnName("plavalova_code").HasMaxLength(20);
            e.Property(x => x.HabitabilityScore).HasColumnName("habitability_score").HasMaxLength(50);
            e.Property(x => x.ScientificNote).HasColumnName("scientific_note");
            e.Property(x => x.CreatedUtc).HasColumnName("created_utc");
            e.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");
            e.HasOne(x => x.SolarSystem).WithMany().HasForeignKey(x => x.SolarSystemId);
            e.HasIndex(x => x.PlanetName).IsUnique();
        });

        // ── PlanetStars (join) ─────────────────────────────────
        modelBuilder.Entity<PlanetStarEntity>(e =>
        {
            e.ToTable("planet_stars", schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.PlanetId).HasColumnName("planet_id");
            e.Property(x => x.StarId).HasColumnName("star_id");
            e.HasOne(x => x.Planet).WithMany().HasForeignKey(x => x.PlanetId);
            e.HasOne(x => x.Star).WithMany().HasForeignKey(x => x.StarId);
            e.HasIndex(x => new { x.PlanetId, x.StarId }).IsUnique();
        });

        // ── Atmospheres ────────────────────────────────────────
        modelBuilder.Entity<AtmosphereEntity>(e =>
        {
            e.ToTable("atmospheres", schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.PlanetId).HasColumnName("planet_id");
            e.Property(x => x.IngestRunId).HasColumnName("ingest_run_id");
            e.Property(x => x.Molecule).HasColumnName("molecule").HasMaxLength(100).IsRequired();
            e.Property(x => x.DetectionType).HasColumnName("detection_type").HasMaxLength(100);
            e.Property(x => x.SpectralReference).HasColumnName("spectral_reference");
            e.Property(x => x.HabitabilityScore).HasColumnName("habitability_score");
            e.Property(x => x.HabitabilityReasoning).HasColumnName("habitability_reasoning");
            e.Property(x => x.CreatedUtc).HasColumnName("created_utc");
            e.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");
            e.HasOne(x => x.Planet).WithMany().HasForeignKey(x => x.PlanetId);
            e.HasOne(x => x.IngestRun).WithMany().HasForeignKey(x => x.IngestRunId).IsRequired(false);
        });

        // ── IngestRun ──────────────────────────────────────────
        modelBuilder.Entity<IngestRunEntity>(e =>
        {
            e.ToTable("ingest_run", schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.RunTimestamp).HasColumnName("run_timestamp").HasDefaultValueSql("NOW()");
            e.Property(x => x.Source).HasColumnName("source").HasMaxLength(255).IsRequired();
            e.Property(x => x.SourceUrl).HasColumnName("source_url");
            e.Property(x => x.RowsFetched).HasColumnName("rows_fetched");
            e.Property(x => x.RowsNew).HasColumnName("rows_new");
            e.Property(x => x.RowsUpdated).HasColumnName("rows_updated");
            e.Property(x => x.RowsDeleted).HasColumnName("rows_deleted");
            e.Property(x => x.RowsUnchanged).HasColumnName("rows_unchanged");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(50).HasDefaultValue("RUNNING");
            e.Property(x => x.ErrorMessage).HasColumnName("error_message");
            e.Property(x => x.CompletedAt).HasColumnName("completed_at");
        });

        // ── ChangeLog ──────────────────────────────────────────
        modelBuilder.Entity<ChangeLogEntity>(e =>
        {
            e.ToTable("change_log", schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.IngestRunId).HasColumnName("ingest_run_id");
            e.Property(x => x.PlanetName).HasColumnName("planet_name").HasMaxLength(255).IsRequired();
            e.Property(x => x.ChangeType).HasColumnName("change_type").HasMaxLength(10).IsRequired();
            e.Property(x => x.FieldName).HasColumnName("field_name").HasMaxLength(255);
            e.Property(x => x.OldValue).HasColumnName("old_value");
            e.Property(x => x.NewValue).HasColumnName("new_value");
            e.Property(x => x.AiClassification).HasColumnName("ai_classification").HasMaxLength(20);
            e.Property(x => x.AiReasoning).HasColumnName("ai_reasoning");
            e.Property(x => x.DetectedAt).HasColumnName("detected_at").HasDefaultValueSql("NOW()");
            e.HasOne(x => x.IngestRun).WithMany().HasForeignKey(x => x.IngestRunId);
        });

        // ── ChangeReport ───────────────────────────────────────
        modelBuilder.Entity<ChangeReportEntity>(e =>
        {
            e.ToTable("change_report", schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.IngestRunId).HasColumnName("ingest_run_id");
            e.Property(x => x.ModelUsed).HasColumnName("model_used").HasMaxLength(100).IsRequired();
            e.Property(x => x.PromptSent).HasColumnName("prompt_sent").IsRequired();
            e.Property(x => x.ReportText).HasColumnName("report_text").IsRequired();
            e.Property(x => x.TokensUsed).HasColumnName("tokens_used");
            e.Property(x => x.GeneratedAt).HasColumnName("generated_at").HasDefaultValueSql("NOW()");
            e.HasOne(x => x.IngestRun).WithMany().HasForeignKey(x => x.IngestRunId);
        });

        // ── PipelineLog ────────────────────────────────────────
        modelBuilder.Entity<PipelineLogEntity>(e =>
        {
            e.ToTable("pipeline_log", schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.IngestRunId).HasColumnName("ingest_run_id");
            e.Property(x => x.LogLevel).HasColumnName("log_level").HasMaxLength(20).IsRequired();
            e.Property(x => x.Message).HasColumnName("message").IsRequired();
            e.Property(x => x.Exception).HasColumnName("exception");
            e.Property(x => x.LoggedAt).HasColumnName("logged_at").HasDefaultValueSql("NOW()");
            e.HasOne(x => x.IngestRun).WithMany().HasForeignKey(x => x.IngestRunId).IsRequired(false);
        });

        // ── EvalResult ─────────────────────────────────────────
        modelBuilder.Entity<EvalResultEntity>(e =>
        {
            e.ToTable("eval_result", schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.IngestRunId).HasColumnName("ingest_run_id");
            e.Property(x => x.EvalType).HasColumnName("eval_type").HasMaxLength(50).IsRequired();
            e.Property(x => x.PlanetName).HasColumnName("planet_name").HasMaxLength(255).IsRequired();
            e.Property(x => x.ExpectedValue).HasColumnName("expected_value");
            e.Property(x => x.ActualValue).HasColumnName("actual_value");
            e.Property(x => x.Score).HasColumnName("score");
            e.Property(x => x.Dimension).HasColumnName("dimension").HasMaxLength(50);
            e.Property(x => x.PassFail).HasColumnName("pass_fail").HasMaxLength(10);
            e.Property(x => x.EvaluatedAt).HasColumnName("evaluated_at").HasDefaultValueSql("NOW()");
            e.HasOne(x => x.IngestRun).WithMany().HasForeignKey(x => x.IngestRunId);
        });

        // ── RetrievalLog ───────────────────────────────────────
        modelBuilder.Entity<RetrievalLogEntity>(e =>
        {
            e.ToTable("retrieval_log", schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.IngestRunId).HasColumnName("ingest_run_id");
            e.Property(x => x.PlanetName).HasColumnName("planet_name").HasMaxLength(255).IsRequired();
            e.Property(x => x.ReferenceId).HasColumnName("reference_id");
            e.Property(x => x.ReferenceName).HasColumnName("reference_name");
            e.Property(x => x.SimilarityScore).HasColumnName("similarity_score");
            e.Property(x => x.WasReferenced).HasColumnName("was_referenced");
            e.Property(x => x.RetrievedAt).HasColumnName("retrieved_at").HasDefaultValueSql("NOW()");
            e.HasOne(x => x.IngestRun).WithMany().HasForeignKey(x => x.IngestRunId).IsRequired(false);
        });

        // ── AgentHandoff ───────────────────────────────────────
        modelBuilder.Entity<AgentHandoff>(e =>
        {
            e.ToTable("agent_handoff", schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.IngestId).HasColumnName("ingest_id");
            e.Property(x => x.AgentName).HasColumnName("agent_name").HasMaxLength(50).IsRequired();
            e.Property(x => x.InputSummary).HasColumnName("input_summary").IsRequired();
            e.Property(x => x.OutputSummary).HasColumnName("output_summary").IsRequired();
            e.Property(x => x.HandoffTimestamp).HasColumnName("handoff_timestamp").HasDefaultValueSql("NOW()");
            e.HasIndex(x => new { x.IngestId, x.AgentName });
        });
    }
}
```

---

## 3. Missing Files Check

| File | Exists? |
|------|---------|
| `Shared/Entities/AgentIteration.cs` | **No** |
| `Exoplanet.Services/Services/ReviewerAgent.cs` | **No** |
| Migration or SQL for `agent_iteration` table | **No** — no EF migrations directory exists; the four `.sql` files (`Queries.sql`, `Scripts/CreateDatabaseSchemaTable.sql`, `Scripts/CreateTables.sql`, `VectorRagDbCreate.sql`) contain no reference to `agent_iteration` or `AgentIteration`. |

---

## 4. Build Result

```
Build succeeded.

    4 Warning(s)
    0 Error(s)
```

**Warnings (non-blocking):**
1. `CS8602` — Possible null dereference in `ExoplanetService.cs` line 48 (`x.PlanetName.ToLower()`).
2. `CS8604` — Possible null argument for `connectionString` in `Exoplanet.Functions/Program.cs` line 26.
3–4. `NU1900` — Unable to load vulnerability data from `nuget.telerik.com` (network/config — not a code issue).
