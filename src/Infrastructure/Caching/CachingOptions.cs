namespace MERSEL.Services.GibUserList.Infrastructure.Caching;

/// <summary>
/// Önbellek katmanı için yapılandırma seçenekleri.
/// </summary>
public sealed class CachingOptions
{
    public const string SectionName = "Caching";

    /// <summary>"Memory" (varsayılan) veya "Redis"</summary>
    public string Provider { get; set; } = "Memory";

    /// <summary>Varsayılan önbellek TTL süresi (dakika)</summary>
    public int DefaultTtlMinutes { get; set; } = 60;

    /// <summary>Redis bağlantı dizesi (yalnızca Provider = "Redis" iken kullanılır)</summary>
    public string? RedisConnectionString { get; set; }

    /// <summary>Redis key öneki (InstanceName)</summary>
    public string RedisInstanceName { get; set; } = "gibuserlist:";
}
