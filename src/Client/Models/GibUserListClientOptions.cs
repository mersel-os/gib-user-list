using System.ComponentModel.DataAnnotations;

namespace MERSEL.Services.GibUserList.Client.Models;

/// <summary>
/// GIB Mükellef API istemcisi için yapılandırma seçenekleri.
/// </summary>
public sealed class GibUserListClientOptions
{
    public const string SectionName = "GibUserListClient";

    /// <summary>GIB Mükellef API'sinin temel URL'si (örn. https://gib-gibuser.example.com)</summary>
    [Required(AllowEmptyStrings = false)]
    [Url]
    public string BaseUrl { get; set; } = default!;

    /// <summary>
    /// HMAC kimlik doğrulaması için erişim anahtarı (public identifier).
    /// Boş bırakılırsa HMAC imzalama devre dışı kalır.
    /// </summary>
    public string? AccessKey { get; set; }

    /// <summary>
    /// HMAC kimlik doğrulaması için gizli anahtar.
    /// AccessKey ile birlikte kullanılmalıdır.
    /// </summary>
    public string? SecretKey { get; set; }

    /// <summary>HTTP istek zaman aşımı</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:05:00")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>HMAC kimlik doğrulaması etkin mi?</summary>
    internal bool IsHmacEnabled => !string.IsNullOrEmpty(AccessKey) && !string.IsNullOrEmpty(SecretKey);
}
