using System.Collections.Concurrent;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using MERSEL.Services.GibUserList.Application.Interfaces;
using MERSEL.Services.GibUserList.Application.Models;

namespace MERSEL.Services.GibUserList.Infrastructure.Webhooks;

/// <summary>
/// Sağlık kontrolü durum geçişlerini izler ve değişikliklerde webhook bildirimi gönderir.
/// Yalnızca geçişlerde bildirim tetiklenir (Healthy → Degraded, Degraded → Healthy vb.),
/// her yoklama döngüsünde değil.
/// </summary>
public sealed class WebhookHealthCheckPublisher(
    IWebhookNotifier webhookNotifier,
    ILogger<WebhookHealthCheckPublisher> logger) : IHealthCheckPublisher
{
    private readonly ConcurrentDictionary<string, HealthStatus> _previousStatuses = new();

    public async Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        foreach (var (checkName, entry) in report.Entries)
        {
            var newStatus = entry.Status;
            var previousStatus = _previousStatuses.GetOrAdd(checkName, newStatus);

            if (previousStatus == newStatus)
                continue;

            _previousStatuses[checkName] = newStatus;

            var severity = newStatus switch
            {
                HealthStatus.Unhealthy => WebhookSeverity.Critical,
                HealthStatus.Degraded => WebhookSeverity.Warning,
                _ => WebhookSeverity.Info
            };

            var direction = newStatus > previousStatus ? "recovered" : "degraded";

            logger.LogInformation(
                "Health check '{CheckName}' {Direction}: {PreviousStatus} → {NewStatus}",
                checkName, direction, previousStatus, newStatus);

            try
            {
                await webhookNotifier.NotifyAsync(new WebhookEvent
                {
                    EventType = WebhookEventType.HealthStatusChanged,
                    Severity = severity,
                    Summary = $"Health check '{checkName}' {direction}: {previousStatus} → {newStatus}.",
                    Payload = new Dictionary<string, object>
                    {
                        ["CheckName"] = checkName,
                        ["PreviousStatus"] = previousStatus.ToString(),
                        ["NewStatus"] = newStatus.ToString(),
                        ["Description"] = entry.Description ?? "-",
                        ["Duration"] = entry.Duration.ToString()
                    }
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to send health status change webhook for '{CheckName}'.",
                    checkName);
            }
        }
    }
}
