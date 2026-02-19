using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MERSEL.Services.GibUserList.Application;
using MERSEL.Services.GibUserList.Application.Interfaces;
using MERSEL.Services.GibUserList.Infrastructure.Caching;
using MERSEL.Services.GibUserList.Infrastructure.Data;
using MERSEL.Services.GibUserList.Infrastructure.Diagnostics;
using MERSEL.Services.GibUserList.Infrastructure.Services;
using MERSEL.Services.GibUserList.Infrastructure.Webhooks;
using StackExchange.Redis;

namespace MERSEL.Services.GibUserList.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Uygulama katmanı servisleri
        services.AddApplicationServices();

        services.AddSingleton<GibUserListMetrics>();
        services.AddSingleton<IAppMetrics>(sp => sp.GetRequiredService<GibUserListMetrics>());
        services.AddHostedService<SyncMetadataGaugeRefreshService>();

        // Veritabanı — EnableRetryOnFailure ile geçici hata dayanıklılığı
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        services.AddDbContext<GibUserListDbContext>(options =>
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorCodesToAdd: null);
                npgsqlOptions.CommandTimeout(60);
            })
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IGibUserListReadDbContext>(sp => sp.GetRequiredService<GibUserListDbContext>());

        // GİB uç nokta yapılandırması
        services.AddOptions<GibEndpointOptions>()
            .Bind(configuration.GetSection(GibEndpointOptions.SectionName))
            .Validate(options =>
                Uri.TryCreate(options.PkListUrl, UriKind.Absolute, out var pkUri) &&
                Uri.TryCreate(options.GbListUrl, UriKind.Absolute, out var gbUri) &&
                (pkUri.Scheme is "http" or "https") &&
                (gbUri.Scheme is "http" or "https") &&
                options.BatchSize > 0 &&
                options.BatchSize <= 100_000 &&
                options.DownloadTimeout > TimeSpan.Zero &&
                options.DownloadTimeout <= TimeSpan.FromHours(1) &&
                options.ChangeRetentionDays > 0 &&
                options.ChangeRetentionDays <= 365 &&
                options.MaxAllowedRemovalPercent is >= 0 and <= 100,
                "GibEndpoints configuration is invalid.")
            .ValidateOnStart();

        // Arşiv depolama yapılandırması (FileSystem veya S3)
        services.AddOptions<ArchiveStorageOptions>()
            .Bind(configuration.GetSection(ArchiveStorageOptions.SectionName))
            .Validate(options =>
            {
                var provider = options.Provider.Trim();
                if (provider.Equals("FileSystem", StringComparison.OrdinalIgnoreCase))
                {
                    return !string.IsNullOrWhiteSpace(options.BasePath) &&
                           options.RetentionDays > 0 &&
                           options.RetentionDays <= 365;
                }

                if (provider.Equals("S3", StringComparison.OrdinalIgnoreCase))
                {
                    return !string.IsNullOrWhiteSpace(options.BucketName) &&
                           options.RetentionDays > 0 &&
                           options.RetentionDays <= 365;
                }

                return false;
            }, "ArchiveStorage configuration is invalid.")
            .ValidateOnStart();
        var archiveProvider = configuration[$"{ArchiveStorageOptions.SectionName}:Provider"] ?? "FileSystem";
        if (string.Equals(archiveProvider, "S3", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IArchiveStorage, S3ArchiveStorage>();
        else
            services.AddSingleton<IArchiveStorage, FileSystemArchiveStorage>();

        // GİB indirmeleri için yeniden deneme destekli HTTP istemcisi
        services.AddHttpClient("GibDownloader")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip
                    | System.Net.DecompressionMethods.Deflate
            })
            .AddHttpMessageHandler<GibDownloadRetryHandler>();
        services.AddTransient<GibDownloadRetryHandler>();

        // GİB servisleri
        services.AddScoped<IGibListDownloader, GibListDownloader>();
        services.AddScoped<GibXmlStreamParser>();
        services.AddScoped<GibBulkCopyWriter>();
        services.AddScoped<GibDiffEngine>();
        services.AddScoped<GibMaterializedViewRefresher>();
        services.AddScoped<GibCacheInvalidationService>();
        services.AddScoped<ISyncMetadataService, SyncMetadataService>();
        services.AddScoped<IArchiveService, GibArchiveService>();
        services.AddScoped<ITransactionalSyncProcessor, GibTransactionalSyncProcessor>();
        services.AddScoped<IGibUserSyncService, GibUserListSyncService>();

        // Son senkronizasyon zamanı sağlayıcısı (memory cache, 5 dk TTL)
        services.AddSingleton<ISyncTimeProvider, SyncTimeProvider>();

        // Önbellekleme
        services.AddOptions<CachingOptions>()
            .Bind(configuration.GetSection(CachingOptions.SectionName))
            .Validate(options =>
            {
                if (options.DefaultTtlMinutes <= 0 || options.DefaultTtlMinutes > 1_440)
                    return false;

                var provider = options.Provider.Trim();
                if (provider.Equals("Memory", StringComparison.OrdinalIgnoreCase))
                    return true;

                if (provider.Equals("Redis", StringComparison.OrdinalIgnoreCase))
                    return !string.IsNullOrWhiteSpace(options.RedisConnectionString) &&
                           !string.IsNullOrWhiteSpace(options.RedisInstanceName);

                return false;
            }, "Caching configuration is invalid.")
            .ValidateOnStart();

        var cachingOptions = new CachingOptions();
        configuration.GetSection(CachingOptions.SectionName).Bind(cachingOptions);

        if (string.Equals(cachingOptions.Provider, "Redis", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(cachingOptions.RedisConnectionString))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = cachingOptions.RedisConnectionString;
                options.InstanceName = cachingOptions.RedisInstanceName;
            });
            services.AddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(cachingOptions.RedisConnectionString!));
            services.AddSingleton<ICacheService, RedisCacheService>();
        }
        else
        {
            services.AddMemoryCache();
            services.AddSingleton<ICacheService, MemoryCacheService>();
        }

        // Webhook bildirimleri
        services.AddOptions<WebhookOptions>()
            .Bind(configuration.GetSection(WebhookOptions.SectionName))
            .Validate(options =>
            {
                if (!options.Enabled) return true;

                if (options.Slack.Enabled && string.IsNullOrWhiteSpace(options.Slack.WebhookUrl))
                    return false;

                if (options.Http.Enabled && string.IsNullOrWhiteSpace(options.Http.Url))
                    return false;

                if (options.Http.Enabled && options.Http.TimeoutSeconds <= 0)
                    return false;

                return true;
            }, "Webhooks configuration is invalid. Enabled channels must have valid URLs configured.")
            .ValidateOnStart();

        // Environment ve ServiceName'i otomatik doldur (config'te boşsa)
        services.PostConfigure<WebhookOptions>(options =>
        {
            var envValue = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            var serviceNameValue = Environment.GetEnvironmentVariable("ASPNETCORE_APPLICATIONNAME");

            if (string.IsNullOrWhiteSpace(options.Environment))
                options.Environment = string.IsNullOrWhiteSpace(envValue) ? "Production" : envValue;

            if (string.IsNullOrWhiteSpace(options.ServiceName))
                options.ServiceName = string.IsNullOrWhiteSpace(serviceNameValue) ? "GibUserListService" : serviceNameValue;
        });

        var webhookOptions = new WebhookOptions();
        configuration.GetSection(WebhookOptions.SectionName).Bind(webhookOptions);

        if (webhookOptions.Enabled)
        {
            services.AddHttpClient(SlackWebhookSender.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.Add("User-Agent", "MERSEL-GibUserList-Webhook/1.0");
            });

            services.AddHttpClient(HttpWebhookSender.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(
                    webhookOptions.Http.TimeoutSeconds > 0 ? webhookOptions.Http.TimeoutSeconds : 10);
                client.DefaultRequestHeaders.Add("User-Agent", "MERSEL-GibUserList-Webhook/1.0");
            });

            services.AddSingleton<SlackWebhookSender>();
            services.AddSingleton<HttpWebhookSender>();
            services.AddSingleton<IWebhookNotifier, WebhookNotifier>();
        }
        else
        {
            services.AddSingleton<IWebhookNotifier, NullWebhookNotifier>();
        }

        return services;
    }
}
