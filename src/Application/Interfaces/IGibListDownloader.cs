namespace MERSEL.Services.GibUserList.Application.Interfaces;

/// <summary>
/// Yapılandırılmış uç noktalardan GIB kullanıcı listesi ZIP dosyalarını indirir.
/// </summary>
public interface IGibListDownloader
{
    /// <summary>
    /// Belirtilen yola PK (Posta Kutusu) kullanıcı listesi ZIP dosyasını indirir.
    /// </summary>
    Task DownloadPkListAsync(string outputPath, CancellationToken cancellationToken);

    /// <summary>
    /// Belirtilen yola GB (Gönderici Birim) kullanıcı listesi ZIP dosyasını indirir.
    /// </summary>
    Task DownloadGbListAsync(string outputPath, CancellationToken cancellationToken);
}
