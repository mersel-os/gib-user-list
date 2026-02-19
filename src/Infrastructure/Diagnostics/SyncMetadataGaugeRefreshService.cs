using MERSEL.Services.GibUserList.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MERSEL.Services.GibUserList.Infrastructure.Diagnostics;

/// <summary>
/// SyncMetadata tablosundan periyodik olarak okunan gauge değerlerini GibUserListMetrics'e yazar.
/// Worker ayrı process'te çalıştığından, API tarafında gauge'lar bu servis üzerinden güncellenir.
/// </summary>
public sealed class SyncMetadataGaugeRefreshService(
    IServiceScopeFactory scopeFactory,
    GibUserListMetrics metrics,
    ILogger<SyncMetadataGaugeRefreshService> logger) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RefreshInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<Data.GibUserListDbContext>();

                var metadata = await dbContext.SyncMetadata
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.Key == SyncMetadata.SingletonKey, stoppingToken);

                if (metadata is not null)
                {
                    Volatile.Write(ref metrics.EInvoiceUserCount, metadata.EInvoiceUserCount);
                    Volatile.Write(ref metrics.EDespatchUserCount, metadata.EDespatchUserCount);
                    Volatile.Write(ref metrics.LastSyncDurationSeconds, metadata.LastSyncDuration.TotalSeconds);
                    Volatile.Write(ref metrics.LastSyncAtUnixSeconds, metadata.LastSyncAt.HasValue
                        ? new DateTimeOffset(
                            metadata.LastSyncAt.Value,
                            TimeZoneInfo.Local.GetUtcOffset(metadata.LastSyncAt.Value))
                            .ToUnixTimeSeconds()
                        : 0);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "SyncMetadata gauge refresh failed — gauge values may be stale.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
