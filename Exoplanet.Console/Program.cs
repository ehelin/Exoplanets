using Exoplanet.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var services = new ServiceCollection();

// Add logging so you can see output
services.AddLogging(builder => builder.AddConsole());

// Add your services (pass empty connection string for now)
services.AddExoplanetServices(connectionString: "");

var provider = services.BuildServiceProvider();

var svc = provider.GetRequiredService<IExoplanetService>();
var logger = provider.GetRequiredService<ILogger<Program>>();

logger.LogInformation("Starting ExoplanetService...");

var result = await svc.RunAsync(CancellationToken.None);

logger.LogInformation(
    "Done. Fetched={Fetched}, Inserted={Inserted}, Updated={Updated}, Skipped={Skipped}",
    result.Fetched, result.Inserted, result.Updated, result.Skipped);
