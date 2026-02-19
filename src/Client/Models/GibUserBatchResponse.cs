namespace MERSEL.Services.GibUserList.Client.Models;

/// <summary>
/// Toplu (batch) mükellef sorgulama sonucu.
/// </summary>
public sealed record GibUserBatchResponse
{
    /// <summary>Bulunan mükellefler</summary>
    public IReadOnlyList<GibUserResponse> Items { get; init; } = [];

    /// <summary>Bulunamayan kimlik numaraları</summary>
    public IReadOnlyList<string> NotFound { get; init; } = [];

    /// <summary>İstenen toplam kimlik sayısı (tekrarsız)</summary>
    public int TotalRequested { get; init; }

    /// <summary>Bulunan mükellef sayısı</summary>
    public int TotalFound { get; init; }
}
