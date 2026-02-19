namespace MERSEL.Services.GibUserList.Client.Models;

/// <summary>
/// Değişiklik sorgusu sayfalanmış yanıtı.
/// </summary>
public sealed record GibUserChangesResponse
{
    /// <summary>Değişiklik kayıtları</summary>
    public List<GibUserChangeResponse> Changes { get; init; } = [];

    /// <summary>Toplam kayıt sayısı</summary>
    public int TotalCount { get; init; }

    /// <summary>Sayfa numarası</summary>
    public int Page { get; init; }

    /// <summary>Sayfa boyutu</summary>
    public int PageSize { get; init; }

    /// <summary>Toplam sayfa sayısı</summary>
    public int TotalPages { get; init; }
}
