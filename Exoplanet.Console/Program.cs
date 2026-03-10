using Exoplanet.Services;
using Exoplanet.Shared.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.Production.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(config);
services.AddLogging(builder => builder.AddConsole());
services.AddExoplanetServices(config.GetConnectionString("DefaultConnection") ?? "");

var provider = services.BuildServiceProvider();
var svc = provider.GetRequiredService<IExoplanetService>();
var logger = provider.GetRequiredService<ILogger<Program>>();

logger.LogInformation("Starting ExoplanetService...");
var result = await svc.RunAsync();
logger.LogInformation(
    "Done. Fetched={Fetched}, Inserted={Inserted}, Updated={Updated}, Skipped={Skipped}",
    result.Fetched, result.Inserted, result.Updated, result.Skipped);