using MERSEL.Services.GibUserList.Domain.Entities;

namespace MERSEL.Services.GibUserList.Application.Models;

/// <summary>
/// Durum uç noktası tarafından döndürülen senkronizasyon durumu bilgisi.
/// </summary>
public sealed record SyncStatusDto
{
    /// <summary>Son başarılı senkronizasyon zamanı (UTC)</summary>
    public DateTime? LastSyncAt { get; init; }

    /// <summary>Toplam e-Fatura mükellef sayısı</summary>
    public int EInvoiceUserCount { get; init; }

    /// <summary>Toplam e-İrsaliye mükellef sayısı</summary>
    public int EDespatchUserCount { get; init; }

    /// <summary>Son senkronizasyon işleminin süresi</summary>
    public TimeSpan? LastSyncDuration { get; init; }

    /// <summary>Son senkronizasyon çalışma durumu: success, partial veya failed</summary>
    public string LastSyncStatus { get; init; } = SyncRunStatus.Success;

    /// <summary>Son başarısız veya kısmi senkronizasyonun hata özeti</summary>
    public string? LastSyncError { get; init; }

    /// <summary>Son senkronizasyon denemesinin zamanı</summary>
    public DateTime? LastAttemptAt { get; init; }

    /// <summary>Son başarısız senkronizasyon zamanı</summary>
    public DateTime? LastFailureAt { get; init; }
}
