using Exoplanet.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureAppConfiguration((ctx, cfg) =>
    {
        // Keep whatever the host already set up
        // Then add our local, gitignored config files with overloads
        cfg.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
           .AddJsonFile($"appsettings.{ctx.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true);

        // Final override layer (Azure App Settings, local env vars, etc.)
        cfg.AddEnvironmentVariables();
    })
    .ConfigureServices((ctx, services) =>
    {
        // Preferred: classic ConnectionStrings section (supports overload files)
        var cs = ctx.Configuration.GetConnectionString("DefaultConnection");

        // Back-compat fallback: your existing env var name
        cs ??= ctx.Configuration["POSTGRES_CONNECTION_STRING"];

        // If you want to enforce it, uncomment:
        // if (string.IsNullOrWhiteSpace(cs))
        //     throw new InvalidOperationException("Missing connection string. Set ConnectionStrings:DefaultConnection (appsettings*) or POSTGRES_CONNECTION_STRING (env var).");

        services.AddExoplanetServices(cs);
    })
    .Build();

host.Run();
