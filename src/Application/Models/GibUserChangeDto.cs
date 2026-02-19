namespace MERSEL.Services.GibUserList.Application.Models;

/// <summary>
/// Changelog'dan dönen tek bir değişiklik kaydı.
/// </summary>
public sealed record GibUserChangeDto
{
    /// <summary>Vergi kimlik numarası (VKN/TCKN)</summary>
    public required string Identifier { get; init; }

    /// <summary>Değişiklik türü: "added", "modified", "removed"</summary>
    public required string ChangeType { get; init; }

    /// <summary>Değişikliğin gerçekleştiği zaman</summary>
    public DateTime ChangedAt { get; init; }

    /// <summary>Ünvan (removed ise null)</summary>
    public string? Title { get; init; }

    /// <summary>Hesap türü (removed ise null)</summary>
    public string? AccountType { get; init; }

    /// <summary>Kullanıcı türü (removed ise null)</summary>
    public string? Type { get; init; }

    /// <summary>İlk oluşturulma zamanı (removed ise null)</summary>
    public DateTime? FirstCreationTime { get; init; }

    /// <summary>Alias listesi (removed ise null)</summary>
    public IReadOnlyList<GibUserAliasDto>? Aliases { get; init; }
}
