using System.Text.Json.Serialization;

namespace MERSEL.Services.GibUserList.Client.Models;

/// <summary>
/// GIB Mükellef API'si tarafından döndürülen mükellef bilgisi.
/// </summary>
public sealed record GibUserResponse
{
    [JsonPropertyName("identifier")]
    public string Identifier { get; init; } = default!;

    [JsonPropertyName("title")]
    public string Title { get; init; } = default!;

    [JsonPropertyName("accountType")]
    public string? AccountType { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("firstCreationTime")]
    public DateTime FirstCreationTime { get; init; }

    [JsonPropertyName("aliases")]
    public IReadOnlyList<GibUserAliasModel> Aliases { get; init; } = [];
}

/// <summary>
/// Mükellef alias detayı.
/// </summary>
public sealed record GibUserAliasModel
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = default!;

    [JsonPropertyName("type")]
    public string Type { get; init; } = default!;

    [JsonPropertyName("creationTime")]
    public DateTime CreationTime { get; init; }
}
