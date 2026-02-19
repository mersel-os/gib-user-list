namespace MERSEL.Services.GibUserList.Client.Models;

/// <summary>
/// Tek bir mükellef değişiklik kaydı.
/// </summary>
public sealed record GibUserChangeResponse
{
    /// <summary>VKN/TCKN</summary>
    public string Identifier { get; init; } = default!;

    /// <summary>"added", "modified" veya "removed"</summary>
    public string ChangeType { get; init; } = default!;

    /// <summary>Değişiklik zamanı</summary>
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
    public List<GibUserAliasModel>? Aliases { get; init; }
}
