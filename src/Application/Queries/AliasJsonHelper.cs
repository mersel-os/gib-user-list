using System.Text.Json;
using MERSEL.Services.GibUserList.Application.Models;

namespace MERSEL.Services.GibUserList.Application.Queries;

/// <summary>
/// Veritabanından JSONB takma ad verisini parse etmek için paylaşılan yardımcı sınıf.
/// </summary>
public static class AliasJsonHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<GibUserAliasDto> ParseAliases(string? aliasesJson)
    {
        if (string.IsNullOrEmpty(aliasesJson))
            return [];

        try
        {
            var aliases = JsonSerializer.Deserialize<List<AliasJsonModel>>(aliasesJson, JsonOptions);
            return aliases?.Select(a => new GibUserAliasDto
            {
                Name = a.Alias ?? string.Empty,
                Type = a.Type ?? string.Empty,
                CreationTime = a.CreationTime
            }).ToList() ?? [];
        }
        catch (JsonException)
        {
            // Malformed JSONB in DB — return empty rather than crash the query.
            // Identifiers lacking aliases are the only expected scenario;
            // if this path is hit with real data, it points to a COPY pipeline bug.
            return [];
        }
    }

    private sealed record AliasJsonModel
    {
        public string? Alias { get; init; }
        public string? Type { get; init; }
        public DateTime CreationTime { get; init; }
    }
}
