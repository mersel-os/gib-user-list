namespace MERSEL.Services.GibUserList.Domain.Entities;

/// <summary>
/// GIB mükellef listesindeki değişiklikleri (ekleme/güncelleme/silme) event olarak kaydeden changelog entity'si.
/// Ayrı bir tablo olarak saklanır; ana tablolar her zaman temiz kalır.
/// İstemciler "since T" sorgusu ile delta değişiklikleri bu tablodan okur.
/// </summary>
public sealed class GibUserChangeLog
{
    public Guid Id { get; init; }

    /// <summary>Belge türü (1: e-Fatura, 2: e-İrsaliye)</summary>
    public GibDocumentType DocumentType { get; set; }

    /// <summary>Vergi kimlik numarası (VKN/TCKN)</summary>
    public string Identifier { get; set; } = default!;

    /// <summary>Değişiklik türü (1: eklendi, 2: güncellendi, 3: silindi)</summary>
    public GibChangeType ChangeType { get; set; }

    /// <summary>Değişikliğin gerçekleştiği zaman</summary>
    public DateTime ChangedAt { get; set; }

    /// <summary>Değişiklik anındaki snapshot — silmelerde null</summary>
    public string? Title { get; set; }

    /// <summary>Değişiklik anındaki hesap türü — silmelerde null</summary>
    public string? AccountType { get; set; }

    /// <summary>Değişiklik anındaki kullanıcı türü — silmelerde null</summary>
    public string? Type { get; set; }

    /// <summary>Değişiklik anındaki ilk oluşturulma zamanı — silmelerde null</summary>
    public DateTime? FirstCreationTime { get; set; }

    /// <summary>Değişiklik anındaki alias JSON snapshot'ı — silmelerde null</summary>
    public string? AliasesJson { get; set; }
}
