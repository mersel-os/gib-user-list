namespace MERSEL.Services.GibUserList.Application.Interfaces;

/// <summary>
/// Hem bellek hem de dağıtık (Redis) backend'lerini destekleyen önbellek soyutlaması.
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Anahtara göre önbelleğe alınmış değeri getirir. Bulunamazsa veya süresi dolmuşsa null döner.
    /// </summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Belirtilen TTL ile önbelleğe bir değer yazar.
    /// </summary>
    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Önbellekten bir değeri kaldırır.
    /// </summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Belirtilen öneki eşleşen tüm önbellek kayıtlarını kaldırır.
    /// </summary>
    Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default);
}
