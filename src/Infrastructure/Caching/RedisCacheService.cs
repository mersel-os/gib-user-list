using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MERSEL.Services.GibUserList.Application.Interfaces;
using StackExchange.Redis;

namespace MERSEL.Services.GibUserList.Infrastructure.Caching;

/// <summary>
/// Redis tabanlı dağıtık önbellek uygulaması.
/// Çoklu örnek / ölçeklenebilir dağıtımlar için uygundur.
/// </summary>
public sealed class RedisCacheService(
    IDistributedCache cache,
    IConnectionMultiplexer multiplexer,
    IOptions<CachingOptions> options,
    ILogger<RedisCacheService> logger) : ICacheService
{
    private readonly TimeSpan _defaultTtl = TimeSpan.FromMinutes(options.Value.DefaultTtlMinutes);
    private readonly string _instanceName = options.Value.RedisInstanceName;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            var bytes = await cache.GetAsync(key, cancellationToken);
            if (bytes is null || bytes.Length == 0) return null;

            return JsonSerializer.Deserialize<T>(bytes, JsonOptions);
        }
        catch (Exception ex) when (IsRedisTransient(ex))
        {
            logger.LogWarning(ex, "Redis GET failed for key {Key}. Falling back to database.", key);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
            var entryOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl ?? _defaultTtl
            };

            await cache.SetAsync(key, bytes, entryOptions, cancellationToken);
        }
        catch (Exception ex) when (IsRedisTransient(ex))
        {
            logger.LogWarning(ex, "Redis SET failed for key {Key}. Value not cached.", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await cache.RemoveAsync(key, cancellationToken);
        }
        catch (Exception ex) when (IsRedisTransient(ex))
        {
            logger.LogWarning(ex, "Redis REMOVE failed for key {Key}.", key);
        }
    }

    public async Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = multiplexer.GetDatabase();
            var pattern = $"{_instanceName}{prefix}*";
            var deleted = 0L;

            foreach (var endpoint in multiplexer.GetEndPoints())
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var server = multiplexer.GetServer(endpoint);
                if (!server.IsConnected || server.IsReplica)
                    continue;

                var keys = server.Keys(
                        database: db.Database,
                        pattern: pattern,
                        pageSize: 500)
                    .ToArray();

                if (keys.Length == 0)
                    continue;

                deleted += await db.KeyDeleteAsync(keys);
            }

            logger.LogInformation("RemoveByPrefixAsync removed {Deleted} keys for prefix {Prefix}.", deleted, prefix);
        }
        catch (Exception ex) when (IsRedisTransient(ex))
        {
            logger.LogWarning(ex, "Redis RemoveByPrefix failed for prefix {Prefix}.", prefix);
        }
    }

    private static bool IsRedisTransient(Exception ex) =>
        ex is RedisConnectionException
            or RedisTimeoutException
            or RedisServerException
            or RedisException
            or ObjectDisposedException;
}
