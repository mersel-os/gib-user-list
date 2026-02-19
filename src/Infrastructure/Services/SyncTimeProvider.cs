using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MERSEL.Services.GibUserList.Application.Interfaces;
using MERSEL.Services.GibUserList.Domain.Entities;
using MERSEL.Services.GibUserList.Infrastructure.Data;

namespace MERSEL.Services.GibUserList.Infrastructure.Services;

/// <summary>
/// Son senkronizasyon zamanını memory'de cache'leyerek sunar.
/// Her sorgu isteğinde DB'ye gitmek yerine 5 dk'da bir yeniler.
/// Sync tamamlandığında Invalidate() çağrılarak anında güncellenir.
/// Thread safety: tek bir immutable referans üzerinden atomic swap.
/// </summary>
public sealed class SyncTimeProvider(IServiceScopeFactory scopeFactory) : ISyncTimeProvider
{
    private sealed record CachedSyncTime(DateTime? Value, DateTime FetchedAt);

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private volatile CachedSyncTime? _cached;

    public async Task<DateTime?> GetLastSyncAtAsync(CancellationToken ct = default)
    {
        var snapshot = _cached;
        if (snapshot is not null && DateTime.Now - snapshot.FetchedAt < CacheDuration)
            return snapshot.Value;

        await _lock.WaitAsync(ct);
        try
        {
            snapshot = _cached;
            if (snapshot is not null && DateTime.Now - snapshot.FetchedAt < CacheDuration)
                return snapshot.Value;

            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<GibUserListDbContext>();

            var metadata = await dbContext.SyncMetadata
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Key == SyncMetadata.SingletonKey, ct);

            _cached = new CachedSyncTime(metadata?.LastSyncAt, DateTime.Now);
            return _cached.Value;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Invalidate() => _cached = null;
}
