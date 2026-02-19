namespace MERSEL.Services.GibUserList.Application.Models;

/// <summary>
/// Toplu (batch) mükellef sorgulama sonucu.
/// </summary>
public sealed record GibUserBatchResult
{
    /// <summary>Bulunan mükellefler</summary>
    public required IReadOnlyList<GibUserDto> Items { get; init; }

    /// <summary>Bulunamayan kimlik numaraları</summary>
    public required IReadOnlyList<string> NotFound { get; init; }

    /// <summary>İstenen toplam kimlik sayısı (tekrarsız)</summary>
    public required int TotalRequested { get; init; }

    /// <summary>Bulunan mükellef sayısı</summary>
    public required int TotalFound { get; init; }
}
