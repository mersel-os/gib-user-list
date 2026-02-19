using MERSEL.Services.GibUserList.Client.Models;

namespace MERSEL.Services.GibUserList.Client.Interfaces;

/// <summary>
/// GIB Mükellef Sicil API'si için HTTP istemcisi.
/// Tüm sorgu metotları <see cref="GibUserApiResponse{T}"/> döner;
/// <c>Data</c> asıl yanıt gövdesini, <c>LastSyncAt</c> son senkronizasyon zamanını içerir.
///
/// Tüketici Protokolü:
/// 1. İlk bootstrap: GetLatestEInvoiceArchiveAsync() ile tam listeyi indir
/// 2. Delta takibi: GetEInvoiceChangesAsync(since) ile değişiklikleri al
/// 3. 410 Gone: Retention süresi dolmuş → tekrar bootstrap yap
/// </summary>
public interface IGibUserListClient
{
    // ── e-Fatura Sorgu ──

    /// <summary>VKN/TCKN ile e-Fatura mükellefini getirir</summary>
    Task<GibUserApiResponse<GibUserResponse?>> GetEInvoiceGibUserAsync(
        string identifier, DateTime? firstCreationTime = null, CancellationToken ct = default);

    /// <summary>Başlığa göre e-Fatura mükelleflerini arar</summary>
    Task<GibUserApiResponse<GibUserSearchResponse>> SearchEInvoiceGibUsersAsync(
        string search, int page = 1, int pageSize = 20, CancellationToken ct = default);

    /// <summary>Birden fazla VKN/TCKN ile toplu e-Fatura mükellefi sorgular (maks 100)</summary>
    Task<GibUserApiResponse<GibUserBatchResponse>> BatchGetEInvoiceGibUsersAsync(
        IEnumerable<string> identifiers, CancellationToken ct = default);

    // ── e-İrsaliye Sorgu ──

    /// <summary>VKN/TCKN ile e-İrsaliye mükellefini getirir</summary>
    Task<GibUserApiResponse<GibUserResponse?>> GetEDespatchGibUserAsync(
        string identifier, DateTime? firstCreationTime = null, CancellationToken ct = default);

    /// <summary>Başlığa göre e-İrsaliye mükelleflerini arar</summary>
    Task<GibUserApiResponse<GibUserSearchResponse>> SearchEDespatchGibUsersAsync(
        string search, int page = 1, int pageSize = 20, CancellationToken ct = default);

    /// <summary>Birden fazla VKN/TCKN ile toplu e-İrsaliye mükellefi sorgular (maks 100)</summary>
    Task<GibUserApiResponse<GibUserBatchResponse>> BatchGetEDespatchGibUsersAsync(
        IEnumerable<string> identifiers, CancellationToken ct = default);

    // ── Delta (Changes) ──

    /// <summary>Belirtilen tarihten sonraki e-Fatura mükellef değişikliklerini getirir</summary>
    Task<GibUserApiResponse<GibUserChangesResponse>> GetEInvoiceChangesAsync(
        DateTime since, int page = 1, int pageSize = 100, CancellationToken ct = default);

    /// <summary>Belirtilen tarihten sonraki e-İrsaliye mükellef değişikliklerini getirir</summary>
    Task<GibUserApiResponse<GibUserChangesResponse>> GetEDespatchChangesAsync(
        DateTime since, int page = 1, int pageSize = 100, CancellationToken ct = default);

    // ── Arşiv (Tam Liste) ──

    /// <summary>e-Fatura mükellef listesi arşiv dosyalarını listeler</summary>
    Task<IReadOnlyList<ArchiveFileResponse>> ListEInvoiceArchivesAsync(CancellationToken ct = default);

    /// <summary>En son e-Fatura tam liste arşivini stream olarak indirir (bootstrap için)</summary>
    Task<GibUserApiResponse<Stream?>> GetLatestEInvoiceArchiveAsync(CancellationToken ct = default);

    /// <summary>Belirtilen e-Fatura arşiv dosyasını stream olarak indirir</summary>
    Task<Stream?> DownloadEInvoiceArchiveAsync(string fileName, CancellationToken ct = default);

    /// <summary>e-İrsaliye mükellef listesi arşiv dosyalarını listeler</summary>
    Task<IReadOnlyList<ArchiveFileResponse>> ListEDespatchArchivesAsync(CancellationToken ct = default);

    /// <summary>En son e-İrsaliye tam liste arşivini stream olarak indirir (bootstrap için)</summary>
    Task<GibUserApiResponse<Stream?>> GetLatestEDespatchArchiveAsync(CancellationToken ct = default);

    /// <summary>Belirtilen e-İrsaliye arşiv dosyasını stream olarak indirir</summary>
    Task<Stream?> DownloadEDespatchArchiveAsync(string fileName, CancellationToken ct = default);

    // ── Durum ──

    /// <summary>Senkronizasyon durumunu getirir</summary>
    Task<SyncStatusResponse> GetSyncStatusAsync(CancellationToken ct = default);
}
