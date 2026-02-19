using MERSEL.Services.GibUserList.Application.Interfaces;
using MERSEL.Services.GibUserList.Application.Models;
using MERSEL.Services.GibUserList.Domain.Entities;
using MERSEL.Services.GibUserList.Infrastructure.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace MERSEL.Services.GibUserList.Infrastructure.Services;

public sealed class GibDiffEngine(
    IOptions<GibEndpointOptions> endpointOptions,
    GibUserListMetrics metrics,
    IWebhookNotifier webhookNotifier,
    ILogger<GibDiffEngine> logger)
{
    private static readonly HashSet<string> AllowedDocumentTypes = ["Invoice", "DespatchAdvice"];
    private static readonly HashSet<string> AllowedTableNames = ["e_invoice_gib_users", "e_despatch_gib_users"];

    public async Task<SyncDiffResult> RunForDocumentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string documentType,
        string tableName,
        GibDocumentType docTypeEnum,
        string cacheKeyPrefix,
        CancellationToken ct)
    {
        if (!AllowedDocumentTypes.Contains(documentType))
            throw new ArgumentException($"Invalid document type: {documentType}", nameof(documentType));
        if (!AllowedTableNames.Contains(tableName))
            throw new ArgumentException($"Invalid table name: {tableName}", nameof(tableName));

        await ExecuteNonQueryAsync(
            connection,
            transaction,
            GibSyncSqlBuilder.BuildPrepareNewDataSql(documentType),
            ct);

        var addedIdentifiers = await QueryIdentifierListAsync(
            connection,
            transaction,
            GibSyncSqlBuilder.BuildAddedIdentifiersSql(tableName),
            ct);

        var modifiedIdentifiers = await QueryIdentifierListAsync(
            connection,
            transaction,
            GibSyncSqlBuilder.BuildModifiedIdentifiersSql(tableName),
            ct);

        var removedIdentifiers = await QueryIdentifierListAsync(
            connection,
            transaction,
            GibSyncSqlBuilder.BuildRemovedIdentifiersSql(tableName),
            ct);

        logger.LogInformation(
            "Diff results for {Table}: {Added} added, {Modified} modified, {Removed} removed",
            tableName, addedIdentifiers.Count, modifiedIdentifiers.Count, removedIdentifiers.Count);

        var performDeletion = true;
        if (removedIdentifiers.Count > 0)
        {
            var currentCount = await QueryScalarAsync<long>(
                connection,
                transaction,
                GibSyncSqlBuilder.BuildCurrentCountSql(tableName),
                ct);

            if (currentCount > 0)
            {
                var removalRatio = (double)removedIdentifiers.Count / currentCount;
                var maxRatio = endpointOptions.Value.MaxAllowedRemovalPercent / 100.0;

                if (removalRatio > maxRatio)
                {
                    logger.LogWarning(
                        "Removal guard tetiklendi: {RemovalCount}/{TotalCount} ({Ratio:P1}) > %{Max}. Silme ATLANIYOR.",
                        removedIdentifiers.Count, currentCount, removalRatio, endpointOptions.Value.MaxAllowedRemovalPercent);
                    metrics.RecordRemovalSkipped();
                    performDeletion = false;

                    await webhookNotifier.NotifyAsync(new WebhookEvent
                    {
                        EventType = WebhookEventType.RemovalGuardTriggered,
                        Severity = WebhookSeverity.Warning,
                        Summary = $"Removal guard triggered for {tableName}: {removedIdentifiers.Count}/{currentCount} ({removalRatio:P1}) exceeds {endpointOptions.Value.MaxAllowedRemovalPercent}% threshold. Deletion SKIPPED.",
                        Payload = new Dictionary<string, object>
                        {
                            ["Table"] = tableName,
                            ["DocumentType"] = documentType,
                            ["RemovalCount"] = removedIdentifiers.Count,
                            ["TotalCount"] = currentCount,
                            ["RemovalRatio"] = $"{removalRatio:P1}",
                            ["Threshold"] = $"{endpointOptions.Value.MaxAllowedRemovalPercent}%"
                        }
                    }, ct);
                }
            }
        }

        await ExecuteNonQueryAsync(
            connection,
            transaction,
            GibSyncSqlBuilder.BuildUpsertSql(tableName),
            ct);

        if (performDeletion && removedIdentifiers.Count > 0)
        {
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                GibSyncSqlBuilder.BuildHardDeleteSql(tableName),
                ct);
        }

        var docTypeValue = (short)docTypeEnum;
        var effectiveRemoved = performDeletion ? removedIdentifiers : [];
        var changelogSql = GibSyncSqlBuilder.BuildChangelogInsertSql(
            docTypeValue,
            addedIdentifiers,
            modifiedIdentifiers,
            effectiveRemoved);

        if (!string.IsNullOrWhiteSpace(changelogSql))
            await ExecuteNonQueryAsync(connection, transaction, changelogSql, ct);

        if (addedIdentifiers.Count > 0) metrics.RecordChanges("added", addedIdentifiers.Count);
        if (modifiedIdentifiers.Count > 0) metrics.RecordChanges("modified", modifiedIdentifiers.Count);
        if (performDeletion && removedIdentifiers.Count > 0) metrics.RecordChanges("removed", removedIdentifiers.Count);

        await ExecuteNonQueryAsync(connection, transaction, GibSyncSqlBuilder.DropNewDataTempTableSql, ct);
        return new SyncDiffResult(docTypeEnum, cacheKeyPrefix, modifiedIdentifiers, effectiveRemoved)
        {
            AddedCount = addedIdentifiers.Count,
            ModifiedCount = modifiedIdentifiers.Count,
            RemovedCount = effectiveRemoved.Count
        };
    }

    private static async Task<List<string>> QueryIdentifierListAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, connection) { Transaction = transaction };
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<string>();
        while (await reader.ReadAsync(ct))
            result.Add(reader.GetString(0));
        return result;
    }

    private static async Task<T> QueryScalarAsync<T>(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, connection) { Transaction = transaction };
        var result = await cmd.ExecuteScalarAsync(ct);
        return (T)result!;
    }

    private static async Task ExecuteNonQueryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, connection) { Transaction = transaction };
        cmd.CommandTimeout = 300;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
