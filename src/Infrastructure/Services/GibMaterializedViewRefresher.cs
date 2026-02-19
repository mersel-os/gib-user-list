using MERSEL.Services.GibUserList.Application.Interfaces;
using MERSEL.Services.GibUserList.Application.Models;
using MERSEL.Services.GibUserList.Infrastructure.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MERSEL.Services.GibUserList.Infrastructure.Services;

public sealed class GibMaterializedViewRefresher(
    GibUserListMetrics metrics,
    IWebhookNotifier webhookNotifier,
    ILogger<GibMaterializedViewRefresher> logger)
{
    public async Task RefreshWithRetryAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        const int maxRetries = 2;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                logger.LogInformation("Refreshing materialized views (attempt {Attempt}/{MaxRetries})...",
                    attempt + 1, maxRetries + 1);

                await using var refreshCmd = new NpgsqlCommand(
                    "REFRESH MATERIALIZED VIEW CONCURRENTLY mv_e_invoice_gib_users; " +
                    "REFRESH MATERIALIZED VIEW CONCURRENTLY mv_e_despatch_gib_users;",
                    connection);
                refreshCmd.CommandTimeout = 300;

                await refreshCmd.ExecuteNonQueryAsync(cancellationToken);

                sw.Stop();
                metrics.RecordMvRefreshDuration(sw.Elapsed.TotalSeconds);
                logger.LogInformation("Materialized views refreshed successfully in {Duration}.", sw.Elapsed);
                return;
            }
            catch (Exception ex) when (attempt < maxRetries && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex,
                    "Materialized view refresh attempt {Attempt}/{MaxRetries} failed. Retrying in 5 seconds...",
                    attempt + 1, maxRetries + 1);
                metrics.RecordMvRefreshError();
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (Exception ex)
            {
                sw.Stop();
                metrics.RecordMvRefreshError();
                logger.LogError(ex,
                    "Materialized view refresh failed after {Attempts} attempts. " +
                    "Data is committed but API will serve stale results until next successful refresh.",
                    attempt + 1);

                await webhookNotifier.NotifyAsync(new WebhookEvent
                {
                    EventType = WebhookEventType.MvRefreshFailed,
                    Severity = WebhookSeverity.Critical,
                    Summary = $"Materialized view refresh FAILED after {attempt + 1} attempts. API will serve stale data.",
                    Payload = new Dictionary<string, object>
                    {
                        ["Attempts"] = attempt + 1,
                        ["MaxRetries"] = maxRetries + 1,
                        ["Error"] = ex.Message,
                        ["Duration"] = sw.Elapsed.ToString()
                    }
                }, cancellationToken);

                throw new InvalidOperationException(
                    "Materialized view refresh failed after all retries. Sync aborted to prevent silent stale reads.",
                    ex);
            }
        }
    }
}
