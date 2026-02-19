namespace MERSEL.Services.GibUserList.Application.Models;

/// <summary>
/// API tarafından döndürülen mükellef bilgisi.
/// </summary>
public sealed record GibUserDto
{
    /// <summary>Vergi kimlik numarası (VKN/TCKN)</summary>
    public required string Identifier { get; init; }

    /// <summary>Şirket/kişi unvanı</summary>
    public required string Title { get; init; }

    /// <summary>GIB hesap türü</summary>
    public string? AccountType { get; init; }

    /// <summary>GIB kullanıcı türü</summary>
    public string? Type { get; init; }

    /// <summary>GIB sisteminde ilk oluşturulma zamanı</summary>
    public DateTime FirstCreationTime { get; init; }

    /// <summary>Mükellef takma adları (posta kutuları)</summary>
    public IReadOnlyList<GibUserAliasDto> Aliases { get; init; } = [];
}

/// <summary>
/// Mükellef takma adı (posta kutusu) detayı.
/// </summary>
public sealed record GibUserAliasDto
{
    /// <summary>Takma ad (örn. urn:mail:defaultpk)</summary>
    public required string Name { get; init; }

    /// <summary>Takma ad türü: PK veya GB</summary>
    public required string Type { get; init; }

    /// <summary>GIB sisteminde oluşturulma zamanı (UTC)</summary>
    public DateTime CreationTime { get; init; }
}
