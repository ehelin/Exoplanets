using Exoplanet.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureAppConfiguration((ctx, cfg) =>
    {
        var env =
            Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT") ??
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ??
            ctx.HostingEnvironment.EnvironmentName ??
            "Production";

        cfg.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
           .AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: true)
           .AddEnvironmentVariables();
    })
    .ConfigureServices((ctx, services) =>
    {
        var cs = ctx.Configuration.GetConnectionString("DefaultConnection");
        cs ??= ctx.Configuration["POSTGRES_CONNECTION_STRING"];
        services.AddExoplanetServices(cs, ctx.Configuration.GetConnectionString("VectorConnection"));
    })
    .Build();

host.Run();