using MERSEL.Services.GibUserList.Infrastructure;
using MERSEL.Services.GibUserList.Infrastructure.Caching;
using MERSEL.Services.GibUserList.Infrastructure.Services;
using MERSEL.Services.GibUserList.Worker;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting GIB User List Updater...");

    var builder = Host.CreateApplicationBuilder(args);

    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Log.Fatal("ConnectionStrings:DefaultConnection is not configured. Aborting startup.");
        Environment.ExitCode = 1;
        return;
    }

    builder.Services.AddSerilog(config => config
        .ReadFrom.Configuration(builder.Configuration)
        .WriteTo.Console());

    // Mediator (source generator) - handler'ları derleme zamanında register eder
    builder.Services.AddMediator();

    builder.Services.AddOptions<GibEndpointOptions>()
        .Bind(builder.Configuration.GetSection(GibEndpointOptions.SectionName))
        .ValidateOnStart();
    builder.Services.AddOptions<ArchiveStorageOptions>()
        .Bind(builder.Configuration.GetSection(ArchiveStorageOptions.SectionName))
        .ValidateOnStart();
    builder.Services.AddOptions<CachingOptions>()
        .Bind(builder.Configuration.GetSection(CachingOptions.SectionName))
        .ValidateOnStart();

    builder.Services.AddInfrastructureServices(builder.Configuration);
    builder.Services.AddHostedService<GibUserSyncHostedService>();

    // Windows Servisi desteği
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "GIB User List Updater";
    });

    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "GIB User List Updater terminated unexpectedly.");
    Environment.ExitCode = 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
