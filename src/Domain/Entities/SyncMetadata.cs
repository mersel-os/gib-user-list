namespace MERSEL.Services.GibUserList.Domain.Entities;

/// <summary>
/// Son senkronizasyon meta verilerini takip eder.
/// Her başarılı GİB listesi senkronizasyonundan sonra güncellenen tek satırlık tablo.
/// </summary>
public sealed class SyncMetadata
{
    public const string SingletonKey = "gib-gibuser-sync";

    public string Key { get; set; } = SingletonKey;

    /// <summary>Son başarılı senkronizasyon zamanı (UTC)</summary>
    public DateTime? LastSyncAt { get; set; }

    /// <summary>Son senkronizasyondan sonra toplam e-Fatura mükellef sayısı</summary>
    public int EInvoiceUserCount { get; set; }

    /// <summary>Son senkronizasyondan sonra toplam e-İrsaliye mükellef sayısı</summary>
    public int EDespatchUserCount { get; set; }

    /// <summary>Son senkronizasyon işleminin süresi</summary>
    public TimeSpan LastSyncDuration { get; set; }

    /// <summary>Son senkronizasyon çalışma durumu: success, partial veya failed</summary>
    public string LastSyncStatus { get; set; } = SyncRunStatus.Success;

    /// <summary>Son başarısız veya kısmi senkronizasyonun hata özeti</summary>
    public string? LastSyncError { get; set; }

    /// <summary>Son senkronizasyon denemesinin başladığı zaman</summary>
    public DateTime LastAttemptAt { get; set; }

    /// <summary>Son başarısız senkronizasyonun zamanı</summary>
    public DateTime? LastFailureAt { get; set; }
}
