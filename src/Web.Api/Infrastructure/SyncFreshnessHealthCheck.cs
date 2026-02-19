using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MERSEL.Services.GibUserList.Domain.Entities;
using MERSEL.Services.GibUserList.Infrastructure.Data;

namespace MERSEL.Services.GibUserList.Web.Infrastructure;

/// <summary>
/// Son sync zamanını kontrol ederek veri tazeliğini doğrular.
/// </summary>
public sealed class SyncFreshnessHealthCheck(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var maxStalenessHours = configuration.GetValue<int?>("HealthChecks:MaxSyncStalenessHours") ?? 24;
        if (maxStalenessHours <= 0)
            maxStalenessHours = 24;

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GibUserListDbContext>();

        SyncMetadata? metadata;
        try
        {
            metadata = await dbContext.SyncMetadata
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Key == SyncMetadata.SingletonKey, cancellationToken);
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState is "42P01" or "42703")
        {
            return HealthCheckResult.Degraded(
                "Database schema not ready yet. Waiting for migration to be applied by the worker.");
        }

        if (metadata is null)
            return HealthCheckResult.Degraded("Sync metadata was not found yet.");

        if (string.Equals(metadata.LastSyncStatus, SyncRunStatus.Failed, StringComparison.OrdinalIgnoreCase))
        {
            return HealthCheckResult.Degraded(
                $"Last sync attempt failed at {metadata.LastFailureAt:g}. Error: {metadata.LastSyncError ?? "unknown"}");
        }

        if (!metadata.LastSyncAt.HasValue)
            return HealthCheckResult.Degraded("No successful sync has been completed yet.");

        var staleness = DateTime.Now - metadata.LastSyncAt.Value;
        if (staleness <= TimeSpan.FromHours(maxStalenessHours))
            return HealthCheckResult.Healthy($"Last sync age: {staleness:g}.");

        return HealthCheckResult.Degraded(
            $"Last sync is stale. Age: {staleness:g}, threshold: {maxStalenessHours}h.");
    }
}
