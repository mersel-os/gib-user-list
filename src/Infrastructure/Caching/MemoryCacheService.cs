using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MERSEL.Services.GibUserList.Application.Interfaces;

namespace MERSEL.Services.GibUserList.Infrastructure.Caching;

/// <summary>
/// IMemoryCache kullanan bellek içi önbellek uygulaması.
/// Tek örnek dağıtımları için uygundur.
/// </summary>
public sealed class MemoryCacheService(
    IMemoryCache memoryCache,
    IOptions<CachingOptions> options) : ICacheService
{
    private readonly TimeSpan _defaultTtl = TimeSpan.FromMinutes(options.Value.DefaultTtlMinutes);
    private static readonly HashSet<string> TrackedKeys = [];
    private static readonly Lock KeysLock = new();

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        var value = memoryCache.Get<T>(key);
        return Task.FromResult(value);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default) where T : class
    {
        var expiration = ttl ?? _defaultTtl;
        var entryOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration
        };
        entryOptions.RegisterPostEvictionCallback((evictedKey, _, _, _) =>
        {
            lock (KeysLock)
            {
                TrackedKeys.Remove((string)evictedKey);
            }
        });
        memoryCache.Set(key, value, entryOptions);

        lock (KeysLock)
        {
            TrackedKeys.Add(key);
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        memoryCache.Remove(key);

        lock (KeysLock)
        {
            TrackedKeys.Remove(key);
        }

        return Task.CompletedTask;
    }

    public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        List<string> keysToRemove;
        lock (KeysLock)
        {
            keysToRemove = TrackedKeys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        }

        foreach (var key in keysToRemove)
        {
            memoryCache.Remove(key);
        }

        lock (KeysLock)
        {
            foreach (var key in keysToRemove)
                TrackedKeys.Remove(key);
        }

        return Task.CompletedTask;
    }
}
