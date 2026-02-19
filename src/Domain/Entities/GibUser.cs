namespace MERSEL.Services.GibUserList.Domain.Entities;

/// <summary>
/// GİB'e kayıtlı mükellef için temel entity.
/// e-Fatura ve e-İrsaliye mükellef listeleri arasında paylaşılan ortak özellikleri içerir.
/// </summary>
public abstract class GibUser
{
    public Guid Id { get; init; }

    /// <summary>Vergi kimlik numarası (VKN/TCKN)</summary>
    public string Identifier { get; set; } = default!;

    /// <summary>Şirket/kişi ünvanı</summary>
    public string Title { get; set; } = default!;

    /// <summary>Büyük/küçük harf duyarsız arama için küçük harfe çevrilmiş ünvan (Türkçe kültür)</summary>
    public string TitleLower { get; set; } = default!;

    /// <summary>GİB'den gelen hesap tipi</summary>
    public string? AccountType { get; set; }

    /// <summary>GİB'den gelen kullanıcı tipi</summary>
    public string? Type { get; set; }

    /// <summary>GİB sisteminde ilk oluşturulma zamanı</summary>
    public DateTime FirstCreationTime { get; set; }

    /// <summary>Takma ad detaylarını içeren JSONB kolonu</summary>
    public string? AliasesJson { get; set; }

    /// <summary>Deterministik içerik hash'i (MD5, 32 hex char). Diff mekanizması tarafından kullanılır.</summary>
    public string? ContentHash { get; set; }
}
