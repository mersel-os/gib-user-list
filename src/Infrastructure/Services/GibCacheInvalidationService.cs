using MERSEL.Services.GibUserList.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace MERSEL.Services.GibUserList.Infrastructure.Services;

public sealed class GibCacheInvalidationService(
    ICacheService cacheService,
    ILogger<GibCacheInvalidationService> logger)
{
    private const int MaxTargetedInvalidationKeys = 10_000;

    public async Task InvalidateChangedCacheEntriesAsync(
        List<SyncDiffResult> diffResults,
        CancellationToken ct)
    {
        var totalKeys = diffResults.Sum(d => d.InvalidationCount);
        if (totalKeys == 0)
        {
            logger.LogDebug("No cache entries to invalidate — no modified or removed identifiers.");
            return;
        }

        if (totalKeys > MaxTargetedInvalidationKeys)
        {
            logger.LogWarning(
                "Cache invalidation skipped: {Count} keys exceeds threshold ({Max}). " +
                "Falling back to prefix invalidation. This typically occurs on first sync or major data migration.",
                totalKeys, MaxTargetedInvalidationKeys);

            foreach (var prefix in diffResults.Select(d => $"{d.CacheKeyPrefix}:id:").Distinct())
                await cacheService.RemoveByPrefixAsync(prefix, ct);
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();

        var keys = diffResults
            .SelectMany(d => d.ModifiedIdentifiers
                .Concat(d.RemovedIdentifiers)
                .Select(id => $"{d.CacheKeyPrefix}:id:{id}"))
            .ToList();

        const int batchSize = 100;
        foreach (var batch in keys.Chunk(batchSize))
        {
            await Task.WhenAll(batch.Select(key => cacheService.RemoveAsync(key, ct)));
        }

        sw.Stop();
        logger.LogInformation(
            "Cache invalidation completed: {Count} keys removed in {Duration}ms.",
            keys.Count, sw.ElapsedMilliseconds);
    }
}
