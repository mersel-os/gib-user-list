namespace MERSEL.Services.GibUserList.Application.Models;

/// <summary>
/// Webhook bildirimlerinde kullanılan olay tipleri.
/// </summary>
public enum WebhookEventType
{
    /// <summary>Senkronizasyon başarıyla tamamlandı</summary>
    SyncCompleted,

    /// <summary>Senkronizasyon başarısız oldu</summary>
    SyncFailed,

    /// <summary>Senkronizasyon kısmi tamamlandı (advisory lock veya arşiv uyarıları)</summary>
    SyncPartial,

    /// <summary>Güvenli silme koruyucusu tetiklendi, toplu silme engellendi</summary>
    RemovalGuardTriggered,

    /// <summary>Materialized view yenileme tüm denemelerde başarısız oldu</summary>
    MvRefreshFailed,

    /// <summary>Sağlık kontrolü durumu değişti (Healthy ↔ Degraded ↔ Unhealthy)</summary>
    HealthStatusChanged
}

/// <summary>
/// Webhook olaylarının önem dereceleri.
/// </summary>
public enum WebhookSeverity
{
    Info,
    Warning,
    Critical
}

/// <summary>
/// Webhook kanallarına gönderilen olay verisi.
/// </summary>
public sealed record WebhookEvent
{
    public required WebhookEventType EventType { get; init; }
    public required WebhookSeverity Severity { get; init; }
    public required string Summary { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public Dictionary<string, object> Payload { get; init; } = [];

    public string EventName => EventType.ToString();
}
