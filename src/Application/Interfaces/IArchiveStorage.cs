namespace MERSEL.Services.GibUserList.Application.Interfaces;

/// <summary>
/// Belge türüne göre ayrıştırılmış GIB kullanıcı listesi arşivleri için depolama soyutlaması.
/// Her sync sonrası e-Fatura ve e-İrsaliye için ayrı XML.GZ arşiv dosyaları üretilir.
/// FileSystem veya S3 implementasyonu konfigürasyona göre otomatik seçilir.
/// </summary>
public interface IArchiveStorage
{
    /// <summary>Dosyayı arşive kaydeder.</summary>
    Task SaveAsync(string fileName, Stream content, CancellationToken ct);

    /// <summary>Arşivdeki dosyayı stream olarak döner. Bulunamazsa null.</summary>
    Task<Stream?> GetAsync(string fileName, CancellationToken ct);

    /// <summary>Belirtilen prefix'e göre arşiv dosyalarını listeler. Prefix null ise tümünü döner.</summary>
    Task<IReadOnlyList<ArchiveFileInfo>> ListAsync(string? prefix = null, CancellationToken ct = default);

    /// <summary>Belirtilen dosyayı arşivden siler.</summary>
    Task DeleteAsync(string fileName, CancellationToken ct);
}

/// <summary>
/// Arşivlenmiş dosya bilgisi.
/// </summary>
public sealed record ArchiveFileInfo(string FileName, long SizeBytes, DateTime CreatedAt);
