using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MERSEL.Services.GibUserList.Application.Models;

namespace MERSEL.Services.GibUserList.Infrastructure.Webhooks;

/// <summary>
/// Slack Incoming Webhook üzerinden Block Kit formatında bildirim gönderir.
/// </summary>
public sealed class SlackWebhookSender(
    IHttpClientFactory httpClientFactory,
    IOptions<WebhookOptions> options,
    ILogger<SlackWebhookSender> logger)
{
    internal const string HttpClientName = "WebhookSlack";

    public bool IsEnabled => options.Value.Slack.Enabled
                             && !string.IsNullOrWhiteSpace(options.Value.Slack.WebhookUrl);

    public bool ShouldNotify(WebhookEvent webhookEvent)
    {
        if (!IsEnabled) return false;

        var notifyOn = options.Value.Slack.NotifyOn;
        return notifyOn.Count == 0 || notifyOn.Contains(webhookEvent.EventName, StringComparer.OrdinalIgnoreCase);
    }

    public async Task SendAsync(WebhookEvent webhookEvent, CancellationToken ct)
    {
        var webhookOptions = options.Value;
        var slackOptions = webhookOptions.Slack;
        var payload = BuildSlackPayload(webhookEvent, slackOptions, webhookOptions.ServiceName, webhookOptions.Environment);
        var json = JsonSerializer.Serialize(payload);

        using var client = httpClientFactory.CreateClient(HttpClientName);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync(slackOptions.WebhookUrl, content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning(
                "Slack webhook returned {StatusCode}: {Body}",
                (int)response.StatusCode, body);
        }
    }

    private static (int added, int modified, int removed) ExtractCounts(object? obj)
    {
        if (obj is System.Text.Json.JsonElement jsonElement)
        {
            return (
                jsonElement.TryGetProperty("added", out var a) ? a.GetInt32() : 0,
                jsonElement.TryGetProperty("modified", out var m) ? m.GetInt32() : 0,
                jsonElement.TryGetProperty("removed", out var r) ? r.GetInt32() : 0
            );
        }

        // Handle anonymous objects or other types
        if (obj == null) return (0, 0, 0);

        var type = obj.GetType();
        var addedProp = type.GetProperty("Added");
        var modifiedProp = type.GetProperty("Modified");
        var removedProp = type.GetProperty("Removed");

        return (
            addedProp != null ? Convert.ToInt32(addedProp.GetValue(obj)) : 0,
            modifiedProp != null ? Convert.ToInt32(modifiedProp.GetValue(obj)) : 0,
            removedProp != null ? Convert.ToInt32(removedProp.GetValue(obj)) : 0
        );
    }

    private static object BuildSlackPayload(WebhookEvent webhookEvent, SlackWebhookOptions slackOptions, string serviceName, string environment)
    {
        var color = webhookEvent.Severity switch
        {
            WebhookSeverity.Critical => "#dc3545",
            WebhookSeverity.Warning => "#fd7e14",
            _ => "#2EB67D"
        };

        // Türkçe başlık oluştur
        var title = webhookEvent.EventType switch
        {
            WebhookEventType.SyncCompleted => "✅ GİB Senkronizasyon Raporu",
            WebhookEventType.SyncFailed => "❌ GİB Senkronizasyon Hatası",
            WebhookEventType.SyncPartial => "⚠️ GİB Senkronizasyon Kısmi",
            WebhookEventType.RemovalGuardTriggered => "🛡️ Silme Koruması Tetiklendi",
            WebhookEventType.MvRefreshFailed => "🔄 Materialized View Hatası",
            WebhookEventType.HealthStatusChanged => "💚 Sağlık Durumu Değişti",
            _ => webhookEvent.EventName
        };

        var blocks = new List<object>();

        // Header
        blocks.Add(new
        {
            type = "header",
            text = new
            {
                type = "plain_text",
                text = title,
                emoji = true
            }
        });

        // Context - Date, Environment, Duration, Service
        var duration = webhookEvent.Payload.TryGetValue("Duration", out var durObj) ? durObj?.ToString() ?? "-" : "-";
        blocks.Add(new
        {
            type = "context",
            elements = new[]
            {
                new
                {
                    type = "mrkdwn",
                    text = $"📅 *{webhookEvent.Timestamp.ToLocalTime():dd.MM.yyyy HH:mm}* | 📍 *{environment}* | ⏱️ *{duration}* | 🛠️ *Servis:* `{serviceName}`"
                }
            }
        });

        blocks.Add(new { type = "divider" });

        // e-Fatura section
        if (webhookEvent.Payload.TryGetValue("EInvoice", out var einvoiceObj))
        {
            var (added, modified, removed) = ExtractCounts(einvoiceObj);
            var total = 0;
            if (einvoiceObj != null)
            {
                var type = einvoiceObj.GetType();
                var totalProp = type.GetProperty("TotalCount");
                if (totalProp != null)
                    total = Convert.ToInt32(totalProp.GetValue(einvoiceObj));
            }

            blocks.Add(new
            {
                type = "section",
                text = new
                {
                    type = "mrkdwn",
                    text = $"*🧾 e-Fatura Kullanıcıları*\n📊 Toplam: `{total:N0}`  |  ✨ Yeni: {added}  |  🗑️ _Silinen: {removed}_  |  🔄 _Değişen: {modified}_"
                }
            });
            blocks.Add(new { type = "divider" });
        }

        // e-Despatch section
        if (webhookEvent.Payload.TryGetValue("EDespatch", out var edespatchObj))
        {
            var (added, modified, removed) = ExtractCounts(edespatchObj);
            var total = 0;
            if (edespatchObj != null)
            {
                var type = edespatchObj.GetType();
                var totalProp = type.GetProperty("TotalCount");
                if (totalProp != null)
                    total = Convert.ToInt32(totalProp.GetValue(edespatchObj));
            }

            blocks.Add(new
            {
                type = "section",
                text = new
                {
                    type = "mrkdwn",
                    text = $"*📦 e-İrsaliye Kullanıcıları*\n📊 Toplam: `{total:N0}`  |  ✨ _Yeni: {added}_  |  🗑️ _Silinen: {removed}_  |  🔄 _Değişen: {modified}_"
                }
            });
            blocks.Add(new { type = "divider" });
        }

        // Error section (if exists)
        if (webhookEvent.Payload.TryGetValue("Error", out var errorObj) && !string.IsNullOrEmpty(errorObj?.ToString()))
        {
            blocks.Add(new
            {
                type = "section",
                text = new
                {
                    type = "mrkdwn",
                    text = $"*❌ Hata:*\n```{errorObj}```"
                }
            });
            blocks.Add(new { type = "divider" });
        }

        var payload = new Dictionary<string, object>
        {
            ["attachments"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["color"] = color,
                    ["blocks"] = blocks.ToArray()
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(slackOptions.Channel))
            payload["channel"] = slackOptions.Channel;

        if (!string.IsNullOrWhiteSpace(slackOptions.Username))
            payload["username"] = slackOptions.Username;

        return payload;
    }
}
