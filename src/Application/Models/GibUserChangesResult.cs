namespace MERSEL.Services.GibUserList.Application.Models;

/// <summary>
/// Değişiklik sorgusu sayfalanmış sonucu.
/// </summary>
public sealed record GibUserChangesResult
{
    /// <summary>Değişiklik kayıtları</summary>
    public required IReadOnlyList<GibUserChangeDto> Changes { get; init; }

    /// <summary>Toplam kayıt sayısı</summary>
    public required int TotalCount { get; init; }

    /// <summary>Mevcut sayfa numarası (1 tabanlı)</summary>
    public required int Page { get; init; }

    /// <summary>Sayfa boyutu</summary>
    public required int PageSize { get; init; }

    /// <summary>Sorgu penceresi üst sınırı. Sonraki çağrıda since olarak kullanılmalıdır.</summary>
    public required DateTime Until { get; init; }

    /// <summary>Toplam sayfa sayısı</summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
}
