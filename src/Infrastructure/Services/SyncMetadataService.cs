using MERSEL.Services.GibUserList.Domain.Entities;
using MERSEL.Services.GibUserList.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace MERSEL.Services.GibUserList.Infrastructure.Services;

public sealed class SyncMetadataService(
    GibUserListDbContext dbContext,
    ILogger<SyncMetadataService> logger) : ISyncMetadataService
{
    public async Task UpdateInTransactionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TimeSpan duration,
        string status,
        string? error,
        DateTime? failureAt,
        DateTime attemptAt,
        CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand(GibSyncSqlBuilder.BuildSyncMetadataUpsertSql(), connection)
        {
            Transaction = transaction
        };

        cmd.Parameters.AddWithValue("key", SyncMetadata.SingletonKey);
        cmd.Parameters.Add(new NpgsqlParameter("syncAt", NpgsqlDbType.Timestamp) { Value = DateTime.Now });
        cmd.Parameters.AddWithValue("duration", duration);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("error", (object?)TrimToMaxLength(error, 2000) ?? DBNull.Value);
        cmd.Parameters.Add(new NpgsqlParameter("attemptAt", NpgsqlDbType.Timestamp) { Value = attemptAt });
        cmd.Parameters.Add(new NpgsqlParameter("failureAt", NpgsqlDbType.Timestamp)
        {
            Value = failureAt.HasValue ? failureAt.Value : DBNull.Value
        });

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateStatusOnlyAsync(
        string status,
        string? error,
        DateTime attemptAt,
        CancellationToken ct)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            GibSyncSqlBuilder.BuildSyncMetadataStatusUpdateSql(),
            [
                new NpgsqlParameter("key", SyncMetadata.SingletonKey),
                new NpgsqlParameter("status", status),
                new NpgsqlParameter("error", (object?)TrimToMaxLength(error, 2000) ?? DBNull.Value),
                new NpgsqlParameter("attemptAt", NpgsqlDbType.Timestamp) { Value = attemptAt }
            ],
            ct);
    }

    public async Task TryUpdateFailureStatusAsync(
        DateTime attemptAt,
        Exception ex,
        CancellationToken ct)
    {
        try
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                GibSyncSqlBuilder.BuildSyncMetadataFailureUpdateSql(),
                [
                    new NpgsqlParameter("key", SyncMetadata.SingletonKey),
                    new NpgsqlParameter("status", SyncRunStatus.Failed),
                    new NpgsqlParameter("error", TrimToMaxLength(ex.Message, 2000)),
                    new NpgsqlParameter("attemptAt", NpgsqlDbType.Timestamp) { Value = attemptAt },
                    new NpgsqlParameter("failureAt", NpgsqlDbType.Timestamp) { Value = DateTime.Now }
                ],
                ct);
        }
        catch (Exception metadataEx)
        {
            logger.LogWarning(metadataEx, "Failed to persist sync failure status metadata.");
        }
    }

    public static string? TrimToMaxLength(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        return value[..maxLength];
    }
}
