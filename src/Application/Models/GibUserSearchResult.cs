namespace MERSEL.Services.GibUserList.Application.Models;

/// <summary>
/// Mükellef sorguları için sayfalanmış arama sonucu.
/// </summary>
public sealed record GibUserSearchResult
{
    /// <summary>Mevcut sayfadaki eşleşen mükellefler</summary>
    public required IReadOnlyList<GibUserDto> Items { get; init; }

    /// <summary>Eşleşen kayıtların toplam sayısı</summary>
    public required int TotalCount { get; init; }

    /// <summary>Mevcut sayfa numarası (1 tabanlı)</summary>
    public required int Page { get; init; }

    /// <summary>Sayfa boyutu</summary>
    public required int PageSize { get; init; }

    /// <summary>Toplam sayfa sayısı</summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
}
