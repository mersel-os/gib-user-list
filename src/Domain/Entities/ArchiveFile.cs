namespace MERSEL.Services.GibUserList.Domain.Entities;

/// <summary>
/// Her sync sonrası üretilen arşiv dosyasının metadata kaydı.
/// Dosya içeriği storage'da (FS/S3) saklanır; bu entity sadece listeleme ve temizlik için kullanılır.
/// </summary>
public sealed class ArchiveFile
{
    public Guid Id { get; init; }
    public GibDocumentType DocumentType { get; set; }
    public string FileName { get; set; } = default!;
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
    public int UserCount { get; set; }
}
