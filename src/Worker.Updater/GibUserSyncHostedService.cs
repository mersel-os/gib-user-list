using MERSEL.Services.GibUserList.Application.Interfaces;

namespace MERSEL.Services.GibUserList.Worker;

/// <summary>
/// GIB mükellef listelerini senkronize eden ve çıkan bağımsız barındırılmış servis.
/// K8s CronJob, Windows Görev Zamanlayıcı veya crontab yürütmesi için tasarlanmıştır.
/// </summary>
public sealed class GibUserSyncHostedService(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<GibUserSyncHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            logger.LogInformation("GIB User List sync job started.");

            using var scope = scopeFactory.CreateScope();
            var syncService = scope.ServiceProvider.GetRequiredService<IGibUserSyncService>();

            await syncService.EnsureDatabaseSchemaAsync(stoppingToken);
            await syncService.SyncGibUserListsAsync(stoppingToken);

            logger.LogInformation("GIB User List sync job completed successfully. Exiting.");
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("GIB User List sync job was cancelled.");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "GIB User List sync job failed.");
            Environment.ExitCode = 1;
        }
        finally
        {
            lifetime.StopApplication();
        }
    }
}
