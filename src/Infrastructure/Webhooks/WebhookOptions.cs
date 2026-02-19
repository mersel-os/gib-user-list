namespace MERSEL.Services.GibUserList.Infrastructure.Webhooks;

/// <summary>
/// Webhook bildirim sistemi ana yapılandırması.
/// </summary>
public sealed class WebhookOptions
{
    public const string SectionName = "Webhooks";

    /// <summary>Webhook sisteminin genel açma/kapama anahtarı</summary>
    public bool Enabled { get; set; }

    /// <summary>Servis adı (ASPNETCORE_APPLICATIONNAME veya config)</summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>Çalışma ortamı (ASPNETCORE_ENVIRONMENT: Development, Staging, Production)</summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>Slack webhook yapılandırması</summary>
    public SlackWebhookOptions Slack { get; set; } = new();

    /// <summary>HTTP webhook yapılandırması</summary>
    public HttpWebhookOptions Http { get; set; } = new();
}

/// <summary>
/// Slack Incoming Webhook yapılandırması.
/// </summary>
public sealed class SlackWebhookOptions
{
    /// <summary>Slack kanalına bildirim göndermeyi etkinleştirir</summary>
    public bool Enabled { get; set; }

    /// <summary>Slack Incoming Webhook URL'si</summary>
    public string WebhookUrl { get; set; } = string.Empty;

    /// <summary>Mesajların gönderileceği kanal (override, opsiyonel)</summary>
    public string? Channel { get; set; }

    /// <summary>Bot kullanıcı adı</summary>
    public string Username { get; set; } = "GIB Sync Bot";

    /// <summary>Bildirim gönderilecek olay tipleri. Boş ise tüm olaylara bildirim gider.</summary>
    public List<string> NotifyOn { get; set; } = [];
}

/// <summary>
/// Genel amaçlı HTTP webhook yapılandırması.
/// </summary>
public sealed class HttpWebhookOptions
{
    /// <summary>HTTP webhook gönderimini etkinleştirir</summary>
    public bool Enabled { get; set; }

    /// <summary>Webhook POST isteğinin gönderileceği URL</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>HMAC-SHA256 imza üretimi için gizli anahtar (opsiyonel)</summary>
    public string? Secret { get; set; }

    /// <summary>HTTP istek zaman aşımı (saniye)</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Bildirim gönderilecek olay tipleri. Boş ise tüm olaylara bildirim gider.</summary>
    public List<string> NotifyOn { get; set; } = [];
}
