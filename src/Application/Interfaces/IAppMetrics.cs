namespace MERSEL.Services.GibUserList.Application.Interfaces;

/// <summary>
/// Uygulama katmanı metrik arayüzü.
/// Infrastructure katmanında GibUserListMetrics tarafından uygulanır.
/// </summary>
public interface IAppMetrics
{
    /// <summary>Başarılı sorguyu kaydeder.</summary>
    void RecordQuery(string type, string documentType, double durationMs);

    /// <summary>Başarısız sorguyu kaydeder.</summary>
    void RecordQueryError(string type, string documentType);

    /// <summary>Önbellek isabetini kaydeder.</summary>
    void RecordCacheHit();

    /// <summary>Önbellek ıskalamasını kaydeder.</summary>
    void RecordCacheMiss();
}
