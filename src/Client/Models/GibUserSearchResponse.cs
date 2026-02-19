namespace MERSEL.Services.GibUserList.Client.Models;

/// <summary>
/// Sayfalanmış mükellef arama sonucu.
/// </summary>
public sealed record GibUserSearchResponse
{
    public IReadOnlyList<GibUserResponse> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages { get; init; }
}
