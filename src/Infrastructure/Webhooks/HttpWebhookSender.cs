using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MERSEL.Services.GibUserList.Application.Models;

namespace MERSEL.Services.GibUserList.Infrastructure.Webhooks;

/// <summary>
/// Genel amaçlı HTTP webhook gönderici.
/// İstek gövdesini JSON olarak POST eder; yapılandırılmışsa HMAC-SHA256 imzası ekler.
/// </summary>
public sealed class HttpWebhookSender(
    IHttpClientFactory httpClientFactory,
    IOptions<WebhookOptions> options,
    ILogger<HttpWebhookSender> logger)
{
    internal const string HttpClientName = "WebhookHttp";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public bool IsEnabled => options.Value.Http.Enabled
                             && !string.IsNullOrWhiteSpace(options.Value.Http.Url);

    public bool ShouldNotify(WebhookEvent webhookEvent)
    {
        if (!IsEnabled) return false;

        var notifyOn = options.Value.Http.NotifyOn;
        return notifyOn.Count == 0 || notifyOn.Contains(webhookEvent.EventName, StringComparer.OrdinalIgnoreCase);
    }

    public async Task SendAsync(WebhookEvent webhookEvent, CancellationToken ct)
    {
        var httpOptions = options.Value.Http;
        var body = JsonSerializer.Serialize(webhookEvent, JsonOptions);

        using var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, httpOptions.Url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        request.Headers.Add("X-Webhook-Event", webhookEvent.EventName);
        request.Headers.Add("X-Webhook-Timestamp", webhookEvent.Timestamp.ToString("O"));
        request.Headers.Add("X-Webhook-ServiceName", options.Value.ServiceName);
        request.Headers.Add("X-Webhook-Environment", options.Value.Environment);

        if (!string.IsNullOrWhiteSpace(httpOptions.Secret))
        {
            var signature = ComputeHmacSha256(body, httpOptions.Secret);
            request.Headers.Add("X-Webhook-Signature", $"sha256={signature}");
        }

        var response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning(
                "HTTP webhook returned {StatusCode}: {Body}",
                (int)response.StatusCode, responseBody);
        }
    }

    private static string ComputeHmacSha256(string payload, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(keyBytes, payloadBytes);
        return Convert.ToHexStringLower(hash);
    }
}
