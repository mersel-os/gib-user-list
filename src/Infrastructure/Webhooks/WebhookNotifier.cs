using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MERSEL.Services.GibUserList.Application.Interfaces;
using MERSEL.Services.GibUserList.Application.Models;

namespace MERSEL.Services.GibUserList.Infrastructure.Webhooks;

/// <summary>
/// Etkin webhook kanallarına (Slack, HTTP) paralel bildirim gönderen orkestratör.
/// Gönderim hataları yutulur ve loglanır — hiçbir zaman ana iş akışını engellemez.
/// </summary>
public sealed class WebhookNotifier(
    SlackWebhookSender slackSender,
    HttpWebhookSender httpSender,
    IOptions<WebhookOptions> options,
    ILogger<WebhookNotifier> logger) : IWebhookNotifier
{
    public async Task NotifyAsync(WebhookEvent webhookEvent, CancellationToken cancellationToken = default)
    {
        var enrichedEvent = EnrichEvent(webhookEvent);
        var tasks = new List<Task>(2);

        if (slackSender.ShouldNotify(enrichedEvent))
            tasks.Add(SendSafeAsync("Slack", () => slackSender.SendAsync(enrichedEvent, cancellationToken)));

        if (httpSender.ShouldNotify(enrichedEvent))
            tasks.Add(SendSafeAsync("HTTP", () => httpSender.SendAsync(enrichedEvent, cancellationToken)));

        if (tasks.Count > 0)
            await Task.WhenAll(tasks);
    }

    private WebhookEvent EnrichEvent(WebhookEvent webhookEvent)
    {
        var webhookOptions = options.Value;

        var enrichedPayload = new Dictionary<string, object>(webhookEvent.Payload)
        {
            ["serviceName"] = webhookOptions.ServiceName,
            ["environment"] = webhookOptions.Environment
        };

        return webhookEvent with { Payload = enrichedPayload };
    }

    private async Task SendSafeAsync(string channel, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Webhook notification failed for {Channel} channel.", channel);
        }
    }
}

/// <summary>
/// Webhook sistemi devre dışıyken kullanılan boş implementasyon.
/// </summary>
public sealed class NullWebhookNotifier : IWebhookNotifier
{
    public Task NotifyAsync(WebhookEvent webhookEvent, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
