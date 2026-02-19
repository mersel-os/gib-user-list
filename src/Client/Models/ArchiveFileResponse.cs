namespace MERSEL.Services.GibUserList.Client.Models;

/// <summary>
/// Arşiv dosyası bilgisi.
/// </summary>
public sealed record ArchiveFileResponse
{
    /// <summary>Dosya adı</summary>
    public string FileName { get; init; } = default!;

    /// <summary>Dosya boyutu (byte)</summary>
    public long SizeBytes { get; init; }

    /// <summary>Oluşturulma zamanı</summary>
    public DateTime CreatedAt { get; init; }
}
