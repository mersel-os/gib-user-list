namespace MERSEL.Services.GibUserList.Application.Interfaces;

/// <summary>
/// Son senkronizasyon zamanını sağlayan servis.
/// Sorgu endpoint'lerinde X-Last-Sync-At header'ı döndürmek için kullanılır.
/// Sık çağrılacağı için implementasyon memory cache kullanmalıdır.
/// </summary>
public interface ISyncTimeProvider
{
    /// <summary>Son başarılı senkronizasyon zamanını döner. Hiç sync yapılmadıysa null döner.</summary>
    Task<DateTime?> GetLastSyncAtAsync(CancellationToken ct = default);

    /// <summary>Cache'i geçersiz kılar (sync tamamlandığında çağrılır).</summary>
    void Invalidate();
}
