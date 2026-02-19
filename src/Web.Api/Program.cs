using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using Scalar.AspNetCore;
using Serilog;
using MERSEL.Services.GibUserList.Infrastructure;
using MERSEL.Services.GibUserList.Infrastructure.Caching;
using MERSEL.Services.GibUserList.Infrastructure.Diagnostics;
using MERSEL.Services.GibUserList.Infrastructure.Services;
using MERSEL.Services.GibUserList.Infrastructure.Webhooks;
using MERSEL.Services.GibUserList.Web.Infrastructure;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting GIB User List API...");

    var builder = WebApplication.CreateBuilder(args);

    // ── Başlangıç yapılandırma doğrulaması ──
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Log.Fatal("ConnectionStrings:DefaultConnection is not configured. Aborting startup.");
        Environment.ExitCode = 1;
        return;
    }

    // Serilog (günlükleme) — DataProtection uyarıları susturuluyor (stateless servis, key kalıcılığı gerekmez)
    builder.Services.AddSerilog(config => config
        .ReadFrom.Configuration(builder.Configuration)
        .MinimumLevel.Override("Microsoft.AspNetCore.DataProtection", Serilog.Events.LogEventLevel.Error)
        .WriteTo.Console());

    // Mediator (source generator) - handler'ları derleme zamanında register eder
    builder.Services.AddMediator();

    // Altyapı (veritabanı, önbellekleme, servisler)
    builder.Services.AddInfrastructureServices(builder.Configuration);

    // OpenTelemetry metrikler — Prometheus exporter ile /metrics endpoint'i
    builder.Services.AddOpenTelemetry()
        .WithMetrics(metrics =>
        {
            metrics.AddAspNetCoreInstrumentation();
            metrics.AddRuntimeInstrumentation();
            metrics.AddMeter(GibUserListMetrics.MeterName);
            metrics.AddPrometheusExporter();
        });

    // OpenAPI dokümantasyonu
    builder.Services.AddOpenApi();

    // Kimlik doğrulama (HMAC-SHA256 ile access_key + secret, devre dışı bırakılabilir)
    builder.Services.AddAuthentication(HmacAuthHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, HmacAuthHandler>(
            HmacAuthHandler.SchemeName, null);
    builder.Services.AddAuthorization();

    builder.Services.AddOptions<MERSEL.Services.GibUserList.Web.Infrastructure.AuthenticationOptions>()
        .Bind(builder.Configuration.GetSection(MERSEL.Services.GibUserList.Web.Infrastructure.AuthenticationOptions.SectionName))
        .Validate(o => o.TimestampToleranceSeconds is > 0 and <= 3_600, "Authentication:TimestampToleranceSeconds must be between 1 and 3600.")
        .Validate(o =>
            !o.Enabled ||
            (o.Clients.Count > 0 && o.Clients.All(c =>
                !string.IsNullOrWhiteSpace(c.AccessKey) &&
                !string.IsNullOrWhiteSpace(c.SecretKey))),
            "Authentication is enabled but no valid HMAC clients are configured.")
        .ValidateOnStart();

    builder.Services.AddOptions<GibEndpointOptions>()
        .Bind(builder.Configuration.GetSection(GibEndpointOptions.SectionName))
        .ValidateOnStart();

    builder.Services.AddOptions<ArchiveStorageOptions>()
        .Bind(builder.Configuration.GetSection(ArchiveStorageOptions.SectionName))
        .ValidateOnStart();

    builder.Services.AddOptions<CachingOptions>()
        .Bind(builder.Configuration.GetSection(CachingOptions.SectionName))
        .ValidateOnStart();

    // Özel durum işleme
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddProblemDetails();

    // Sağlık kontrolleri ve veritabanı sondası
    var healthBuilder = builder.Services.AddHealthChecks();
    healthBuilder.AddNpgSql(connectionString, name: "postgresql");
    healthBuilder.AddCheck<SyncFreshnessHealthCheck>("sync_freshness");
    healthBuilder.AddCheck<ArchiveStorageHealthCheck>("archive_storage");

    // Sağlık kontrolü durum geçişlerini webhook ile bildirir
    builder.Services.AddSingleton<IHealthCheckPublisher, WebhookHealthCheckPublisher>();
    builder.Services.Configure<HealthCheckPublisherOptions>(options =>
    {
        options.Delay = TimeSpan.FromSeconds(15);
        options.Period = TimeSpan.FromSeconds(60);
    });

    var app = builder.Build();

    // Middleware zinciri
    app.UseSecurityHeaders();
    app.UseExceptionHandler();
    app.UseSerilogRequestLogging();

    app.UseAuthentication();
    app.UseAuthorization();

    // OpenAPI + Scalar UI (tüm ortamlarda aktif — internal servis, dışa kapalı)
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("GIB User List Registry API");
    });

    // Anasayfa → Scalar UI'a yönlendir
    app.MapGet("/", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription();

    // Prometheus metrik endpoint'i (kimlik doğrulama gerekmez)
    app.MapPrometheusScrapingEndpoint();

    // Sağlık kontrolü (kimlik doğrulama gerekmez)
    app.MapHealthChecks("/health").AllowAnonymous();

    // Uç noktalar
    app.MapEndpoints();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "GIB User List API terminated unexpectedly.");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
