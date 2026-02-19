namespace MERSEL.Services.GibUserList.Infrastructure.Services;

/// <summary>
/// Arşiv depolama yapılandırma seçenekleri.
/// FileSystem veya S3 arasında Provider değerine göre seçim yapılır.
/// </summary>
public sealed class ArchiveStorageOptions
{
    public const string SectionName = "ArchiveStorage";

    /// <summary>"FileSystem" (varsayılan) veya "S3"</summary>
    public string Provider { get; set; } = "FileSystem";

    /// <summary>FileSystem: dosyaların saklanacağı dizin yolu</summary>
    public string BasePath { get; set; } = "/data/gib-archives";

    /// <summary>Her iki provider için arşiv saklama süresi (gün). Eski dosyalar otomatik silinir.</summary>
    public int RetentionDays { get; set; } = 7;

    /// <summary>S3: bucket adı</summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>S3: AWS bölgesi</summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>S3: MinIO gibi self-hosted S3 uyumlu servisler için özel endpoint URL'si</summary>
    public string? ServiceUrl { get; set; }

    /// <summary>
    /// S3: Erişim anahtarı. Boş bırakılırsa AWS SDK varsayılan credential zinciri kullanılır
    /// (ortam değişkenleri AWS_ACCESS_KEY_ID/AWS_SECRET_ACCESS_KEY, IAM role, ~/.aws/credentials).
    /// MinIO gibi self-hosted çözümler için zorunludur.
    /// </summary>
    public string? AccessKey { get; set; }

    /// <summary>S3: Gizli anahtar. AccessKey ile birlikte kullanılır.</summary>
    public string? SecretKey { get; set; }
}
