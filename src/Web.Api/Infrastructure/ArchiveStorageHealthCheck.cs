using Microsoft.Extensions.Diagnostics.HealthChecks;
using MERSEL.Services.GibUserList.Application.Interfaces;

namespace MERSEL.Services.GibUserList.Web.Infrastructure;

/// <summary>
/// Arşiv depolama sağlayıcısına erişimi doğrular.
/// </summary>
public sealed class ArchiveStorageHealthCheck(IArchiveStorage archiveStorage) : IHealthCheck
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            probeCts.CancelAfter(ProbeTimeout);
            _ = await archiveStorage.ListAsync(prefix: "einvoice/", ct: probeCts.Token);
            return HealthCheckResult.Healthy("Archive storage is reachable.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy($"Archive storage health probe timed out after {ProbeTimeout.TotalSeconds:0}s.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Archive storage is not reachable.", ex);
        }
    }
}
