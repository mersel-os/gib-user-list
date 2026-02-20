namespace MERSEL.Services.GibUserList.Application.Configuration;

/// <summary>
/// GIB liste indirme uç noktaları için yapılandırma seçenekleri.
/// </summary>
public sealed class GibEndpointOptions
{
    public const string SectionName = "GibEndpoints";

    /// <summary>PK (Posta Kutusu) kullanıcı listesi ZIP dosyası URL'si</summary>
    public string PkListUrl { get; set; } =
        "https://merkeztest.gib.gov.tr/EFaturaMerkez/newUserPkListxml.zip";

    /// <summary>GB (Gönderici Birim) kullanıcı listesi ZIP dosyası URL'si</summary>
    public string GbListUrl { get; set; } =
        "https://merkeztest.gib.gov.tr/EFaturaMerkez/newUserGbListxml.zip";

    /// <summary>PostgreSQL COPY toplu ekleme için batch boyutu</summary>
    public int BatchSize { get; set; } = 25_000;

    /// <summary>HTTP indirme zaman aşımı</summary>
    public TimeSpan DownloadTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Changelog kayıtlarının tutulma süresi (gün). Varsayılan 30 gün.</summary>
    public int ChangeRetentionDays { get; set; } = 30;

    /// <summary>
    /// Güvenli silme eşiği (yüzde). Silinecek kayıt oranı bu değeri aşarsa silme atlanır.
    /// Bozuk/eksik XML indirmelerinde toplu yanlış silmeyi önler.
    /// </summary>
    public double MaxAllowedRemovalPercent { get; set; } = 10;
}
