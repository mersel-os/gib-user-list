using System.Data;
using MERSEL.Services.GibUserList.Domain.Entities;
using MERSEL.Services.GibUserList.Infrastructure.Data;
using MERSEL.Services.GibUserList.Infrastructure.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace MERSEL.Services.GibUserList.Infrastructure.Services;

public sealed class GibTransactionalSyncProcessor(
    GibUserListDbContext dbContext,
    GibXmlStreamParser xmlParser,
    GibBulkCopyWriter bulkWriter,
    GibDiffEngine diffEngine,
    GibMaterializedViewRefresher materializedViewRefresher,
    GibCacheInvalidationService cacheInvalidationService,
    ISyncMetadataService syncMetadataService,
    GibUserListMetrics metrics,
    IOptions<GibEndpointOptions> endpointOptions,
    ILogger<GibTransactionalSyncProcessor> logger) : ITransactionalSyncProcessor
{
    private const long SyncAdvisoryLockId = 8_370_142_691;

    public async Task<(TransactionalSyncResult Result, SyncResultInfo? Info)> ProcessUserListsAsync(
        string pkXmlPath,
        string gbXmlPath,
        DateTime operationTime,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var diffResults = new List<SyncDiffResult>(2);
        var pkCount = 0;
        var gbCount = 0;

        try
        {
            var lockAcquired = await QueryScalarAsync<bool>(
                connection,
                transaction,
                $"SELECT pg_try_advisory_xact_lock({SyncAdvisoryLockId});",
                cancellationToken);

            if (!lockAcquired)
            {
                logger.LogWarning("Başka bir sync işlemi zaten çalışıyor. Bu çalıştırma atlanıyor.");
                await transaction.RollbackAsync(cancellationToken);
                return (TransactionalSyncResult.SkippedByAdvisoryLock, null);
            }

            logger.LogInformation("Advisory lock acquired. Proceeding with sync.");

            logger.LogInformation("Cleaning temp tables...");
            await ExecuteBatchAsync(connection, transaction, cancellationToken,
                "TRUNCATE TABLE gib_user_temp_pk",
                "TRUNCATE TABLE gib_user_temp_gb",
                "DROP INDEX IF EXISTS idx_gib_user_temp_pk_identifier",
                "DROP INDEX IF EXISTS idx_gib_user_temp_gb_identifier",
                "DROP INDEX IF EXISTS idx_gib_user_temp_pk_documents",
                "DROP INDEX IF EXISTS idx_gib_user_temp_gb_documents");

            logger.LogInformation("Processing PK user list...");
            var pkUsers = xmlParser.ParseUsers(pkXmlPath);
            var pkCountLong = await bulkWriter.WriteBatchesToTempTableAsync(pkUsers, "PK", operationTime, connection, cancellationToken);
            pkCount = (int)pkCountLong;
            metrics.RecordUsersProcessed("pk", pkCountLong);
            File.Delete(pkXmlPath);

            logger.LogInformation("Processing GB user list...");
            var gbUsers = xmlParser.ParseUsers(gbXmlPath);
            var gbCountLong = await bulkWriter.WriteBatchesToTempTableAsync(gbUsers, "GB", operationTime, connection, cancellationToken);
            gbCount = (int)gbCountLong;
            metrics.RecordUsersProcessed("gb", gbCountLong);
            File.Delete(gbXmlPath);

            await ExecuteBatchAsync(connection, transaction, cancellationToken,
                "CREATE INDEX idx_gib_user_temp_pk_identifier ON gib_user_temp_pk (identifier)",
                "CREATE INDEX idx_gib_user_temp_gb_identifier ON gib_user_temp_gb (identifier)",
                "CREATE INDEX idx_gib_user_temp_pk_documents ON gib_user_temp_pk USING gin (documents jsonb_path_ops)",
                "CREATE INDEX idx_gib_user_temp_gb_documents ON gib_user_temp_gb USING gin (documents jsonb_path_ops)");

            logger.LogInformation("Running diff engine for e-Invoice...");
            diffResults.Add(await diffEngine.RunForDocumentAsync(
                connection,
                transaction,
                "Invoice",
                "e_invoice_gib_users",
                GibDocumentType.EInvoice,
                "einvoice",
                cancellationToken));

            logger.LogInformation("Running diff engine for e-Despatch...");
            diffResults.Add(await diffEngine.RunForDocumentAsync(
                connection,
                transaction,
                "DespatchAdvice",
                "e_despatch_gib_users",
                GibDocumentType.EDespatch,
                "edespatch",
                cancellationToken));

            var retentionDays = endpointOptions.Value.ChangeRetentionDays;
            await ExecuteNonQueryAsync(connection, transaction,
                GibSyncSqlBuilder.BuildDeleteOldChangelogSql(retentionDays),
                cancellationToken);

            await syncMetadataService.UpdateInTransactionAsync(
                connection,
                transaction,
                duration,
                SyncRunStatus.Success,
                error: null,
                failureAt: null,
                attemptAt: operationTime,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Data transaction committed successfully.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        // Refresh materialized views and invalidate cache
        await materializedViewRefresher.RefreshWithRetryAsync(connection, cancellationToken);
        await cacheInvalidationService.InvalidateChangedCacheEntriesAsync(diffResults, cancellationToken);

        // Get total identifier counts from materialized views
        int einvoiceTotalCount = 0;
        int edespatchTotalCount = 0;

        try
        {
            einvoiceTotalCount = (int)await QueryScalarAsync<long>(
                connection, 
                null, 
                "SELECT COUNT(*) FROM mv_e_invoice_gib_users", 
                cancellationToken);
        }
        catch { /* ignore */ }

        try
        {
            edespatchTotalCount = (int)await QueryScalarAsync<long>(
                connection, 
                null, 
                "SELECT COUNT(*) FROM mv_e_despatch_gib_users", 
                cancellationToken);
        }
        catch { /* ignore */ }

        // Extract diff results for webhook
        var einvoice = diffResults.FirstOrDefault(d => d.DocumentType == GibDocumentType.EInvoice);
        var edespatch = diffResults.FirstOrDefault(d => d.DocumentType == GibDocumentType.EDespatch);

        var syncInfo = new SyncResultInfo(
            einvoice?.AddedCount ?? 0,
            einvoice?.ModifiedCount ?? 0,
            einvoice?.RemovedCount ?? 0,
            edespatch?.AddedCount ?? 0,
            edespatch?.ModifiedCount ?? 0,
            edespatch?.RemovedCount ?? 0,
            pkCount,
            gbCount)
        {
            EinvoiceTotalCount = einvoiceTotalCount,
            EdespatchTotalCount = edespatchTotalCount
        };

        return (TransactionalSyncResult.Applied, syncInfo);
    }

    private static async Task<T> QueryScalarAsync<T>(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
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

    private static async Task ExecuteBatchAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken,
        params string[] commands)
    {
        await using var batch = new NpgsqlBatch(connection) { Transaction = transaction };
        foreach (var sql in commands)
            batch.BatchCommands.Add(new NpgsqlBatchCommand(sql));
        await batch.ExecuteNonQueryAsync(cancellationToken);
    }
}
