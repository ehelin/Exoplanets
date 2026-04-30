using Exoplanet.Services;
using Exoplanet.Shared.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.Production.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(config);
services.AddLogging(builder => builder.AddConsole());
services.AddExoplanetServices(
    config.GetConnectionString("DefaultConnection") ?? "",
    config.GetConnectionString("VectorConnection"));

var provider = services.BuildServiceProvider();
var ragService = provider.GetRequiredService<IRagIngestionService>();
var exoPlanetService = provider.GetRequiredService<IExoplanetService>();
var logger = provider.GetRequiredService<ILogger<Program>>();

logger.LogInformation("Starting ExoplanetService...");

await ragService.IngestReferencesAsync();
var result = await exoPlanetService.RunAsync();

logger.LogInformation(
    "Done. Fetched={Fetched}, Inserted={Inserted}, Updated={Updated}, Skipped={Skipped}",
    result.Fetched, result.Inserted, result.Updated, result.Skipped);