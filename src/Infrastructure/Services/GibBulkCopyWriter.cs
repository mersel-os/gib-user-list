using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using PostgreSQLCopyHelper;
using MERSEL.Services.GibUserList.Application.Models;

namespace MERSEL.Services.GibUserList.Infrastructure.Services;

/// <summary>
/// Ayrıştırılmış GIB kullanıcılarını maksimum verimlilik için COPY protokolü ile PostgreSQL geçici tablolarına yazar.
/// </summary>
public sealed class GibBulkCopyWriter(
    IOptions<GibEndpointOptions> options,
    ILogger<GibBulkCopyWriter> logger)
{
    private static readonly CultureInfo TurkishCulture = new("tr-TR", false);
    private readonly int _batchSize = options.Value.BatchSize;

    private static readonly PostgreSQLCopyHelper<GibUserTemp> PkCopyHelper =
        new PostgreSQLCopyHelper<GibUserTemp>("public", "gib_user_temp_pk")
            .Map("identifier", x => x.Identifier, NpgsqlDbType.Varchar)
            .Map("account_type", x => x.AccountType, NpgsqlDbType.Varchar)
            .Map("first_creation_time", x => x.FirstCreationTime, NpgsqlDbType.Timestamp)
            .Map("title", x => x.Title, NpgsqlDbType.Varchar)
            .Map("title_lower", x => x.TitleLower, NpgsqlDbType.Varchar)
            .Map("type", x => x.Type, NpgsqlDbType.Varchar)
            .Map("documents", x => x.DocumentsJson, NpgsqlDbType.Jsonb);

    private static readonly PostgreSQLCopyHelper<GibUserTemp> GbCopyHelper =
        new PostgreSQLCopyHelper<GibUserTemp>("public", "gib_user_temp_gb")
            .Map("identifier", x => x.Identifier, NpgsqlDbType.Varchar)
            .Map("account_type", x => x.AccountType, NpgsqlDbType.Varchar)
            .Map("first_creation_time", x => x.FirstCreationTime, NpgsqlDbType.Timestamp)
            .Map("title", x => x.Title, NpgsqlDbType.Varchar)
            .Map("title_lower", x => x.TitleLower, NpgsqlDbType.Varchar)
            .Map("type", x => x.Type, NpgsqlDbType.Varchar)
            .Map("documents", x => x.DocumentsJson, NpgsqlDbType.Jsonb);

    /// <summary>
    /// Ayrıştırılmış kullanıcıları ilgili geçici tabloya toplu olarak yazar.
    /// Yazılan toplam kayıt sayısını döner.
    /// </summary>
    public async Task<long> WriteBatchesToTempTableAsync(
        IEnumerable<GibXmlUser> users,
        string aliasType,
        DateTime operationTime,
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var copyHelper = aliasType == "PK" ? PkCopyHelper : GbCopyHelper;
        var batch = new List<GibUserTemp>();
        var totalWritten = 0L;

        foreach (var user in users)
        {
            cancellationToken.ThrowIfCancellationRequested();
            batch.Add(ConvertToTemp(user, aliasType, operationTime));

            if (batch.Count >= _batchSize)
            {
                await copyHelper.SaveAllAsync(connection, batch);
                totalWritten += batch.Count;
                logger.LogInformation("Wrote {Count} {AliasType} records (total: {Total})",
                    batch.Count, aliasType, totalWritten);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await copyHelper.SaveAllAsync(connection, batch);
            totalWritten += batch.Count;
            logger.LogInformation("Wrote {Count} remaining {AliasType} records (total: {Total})",
                batch.Count, aliasType, totalWritten);
        }

        return totalWritten;
    }

    private static GibUserTemp ConvertToTemp(GibXmlUser user, string aliasType, DateTime operationTime)
    {
        var documents = user.Documents?.Document?
            .Select(d => new GibDocumentTemp
            {
                Type = d.Type,
                Aliases = d.Aliases?
                    .Where(a => a.DeletionTime is null && a.Names?.Any() == true)
                    .SelectMany(a => a.Names!, (alias, name) => new GibAliasTemp
                    {
                        Alias = name,
                        CreationTime = alias.CreationTime,
                        Type = aliasType
                    })
                    .ToList() ?? []
            })
            .ToList();

        return new GibUserTemp
        {
            Identifier = user.Identifier,
            Title = user.Title,
            TitleLower = user.Title.ToLower(TurkishCulture),
            AccountType = user.AccountType,
            Type = user.Type,
            FirstCreationTime = user.FirstCreationTime,
            DocumentsJson = documents is not null ? JsonSerializer.Serialize(documents) : null
        };
    }

}

internal sealed class GibUserTemp
{
    public string Identifier { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string TitleLower { get; set; } = default!;
    public string? AccountType { get; set; }
    public string? Type { get; set; }
    public DateTime FirstCreationTime { get; set; }
    public string? DocumentsJson { get; set; }
}

internal sealed class GibDocumentTemp
{
    public string Type { get; set; } = default!;
    public List<GibAliasTemp>? Aliases { get; set; }
}

internal sealed class GibAliasTemp
{
    public string Alias { get; set; } = default!;
    public DateTime CreationTime { get; set; }
    public string Type { get; set; } = default!;
}
