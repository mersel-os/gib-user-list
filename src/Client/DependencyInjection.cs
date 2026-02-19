using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MERSEL.Services.GibUserList.Client.Infrastructure;
using MERSEL.Services.GibUserList.Client.Interfaces;
using MERSEL.Services.GibUserList.Client.Models;

namespace MERSEL.Services.GibUserList.Client;

public static class DependencyInjection
{
    /// <summary>
    /// GIB Mükellef API istemcisini DI konteynerına kaydeder.
    /// HMAC kimlik doğrulaması AccessKey ve SecretKey verildiğinde otomatik olarak etkinleşir.
    /// </summary>
    /// <example>
    /// services.AddGibUserListClient(options =>
    /// {
    ///     options.BaseUrl = "https://gib-gibuser.example.com";
    ///     options.AccessKey = "my-access-key";
    ///     options.SecretKey = "my-secret-key";
    /// });
    /// </example>
    public static IServiceCollection AddGibUserListClient(
        this IServiceCollection services,
        Action<GibUserListClientOptions> configureOptions)
    {
        var opts = new GibUserListClientOptions();
        configureOptions(opts);

        services.Configure(configureOptions);

        var httpBuilder = services.AddHttpClient<IGibUserListClient, GibUserListClient>(client =>
        {
            client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/'));
            client.Timeout = opts.Timeout;
        });

        if (opts.IsHmacEnabled)
        {
            httpBuilder.AddHttpMessageHandler(() =>
                new HmacSigningHandler(opts.AccessKey!, opts.SecretKey!));
        }

        return services;
    }

    /// <summary>
    /// IConfiguration bağlaması ile GIB Mükellef API istemcisini kaydeder.
    /// HMAC kimlik doğrulaması AccessKey ve SecretKey verildiğinde otomatik olarak etkinleşir.
    /// </summary>
    /// <example>
    /// // appsettings.json:
    /// // "GibUserListClient": {
    /// //   "BaseUrl": "https://...",
    /// //   "AccessKey": "my-access-key",
    /// //   "SecretKey": "my-secret-key"
    /// // }
    /// services.AddGibUserListClient(configuration);
    /// </example>
    public static IServiceCollection AddGibUserListClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(GibUserListClientOptions.SectionName);
        var opts = new GibUserListClientOptions();
        section.Bind(opts);

        services.Configure<GibUserListClientOptions>(section);

        var httpBuilder = services.AddHttpClient<IGibUserListClient, GibUserListClient>(client =>
        {
            client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/'));
            client.Timeout = opts.Timeout;
        });

        if (opts.IsHmacEnabled)
        {
            httpBuilder.AddHttpMessageHandler(() =>
                new HmacSigningHandler(opts.AccessKey!, opts.SecretKey!));
        }

        return services;
    }
}
