using System.Diagnostics;
using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MERSEL.Services.GibUserList.Application.Interfaces;
using MERSEL.Services.GibUserList.Application.Models;
using MERSEL.Services.GibUserList.Domain.Entities;
using MERSEL.Services.GibUserList.Infrastructure.Data;
using MERSEL.Services.GibUserList.Infrastructure.Diagnostics;

namespace MERSEL.Services.GibUserList.Infrastructure.Services;

/// <summary>
/// GIB mükellef listesi senkronizasyonunu yönetir:
/// ZIP indir -> XML çıkar -> akış ile parse et -> geçici tablolara COPY yap ->
/// diff engine (detect + upsert + hard-delete + changelog) -> MV'leri yenile.
/// </summary>
public sealed class GibUserListSyncService(
    GibUserListDbContext dbContext,
    IGibListDownloader downloader,
    ITransactionalSyncProcessor transactionalSyncProcessor,
    IArchiveService archiveService,
    ISyncMetadataService syncMetadataService,
    GibUserListMetrics metrics,
    ISyncTimeProvider syncTimeProvider,
    IWebhookNotifier webhookNotifier,
    ILogger<GibUserListSyncService> logger) : IGibUserSyncService
{
    public async Task EnsureDatabaseSchemaAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Ensuring database schema...");

        await dbContext.Database.MigrateAsync(cancellationToken);

        logger.LogInformation("Database schema ensured via EF Core migrations.");
    }

    public async Task SyncGibUserListsAsync(CancellationToken cancellationToken)
    {
        using var activity = GibUserListMetrics.ActivitySource.StartActivity("SyncGibUserLists");
        metrics.IncrementSyncActive();

        var sw = Stopwatch.StartNew();
        var attemptAt = DateTime.Now;
        var tempFolder = Path.Combine(Path.GetTempPath(), $"gib_user_list_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempFolder);
        var postSyncWarnings = new List<string>();

        try
        {
            // Adım 1: İndirme
            var pkZipPath = Path.Combine(tempFolder, "pk_list.zip");
            var gbZipPath = Path.Combine(tempFolder, "gb_list.zip");

            logger.LogInformation("Downloading GIB user lists...");
            await Task.WhenAll(
                downloader.DownloadPkListAsync(pkZipPath, cancellationToken),
                downloader.DownloadGbListAsync(gbZipPath, cancellationToken));

            // Adım 2: Çıkarma
            var pkXmlPath = ExtractFirstXmlFromZip(pkZipPath, tempFolder, "pk");
            var gbXmlPath = ExtractFirstXmlFromZip(gbZipPath, tempFolder, "gb");
            logger.LogInformation("Extracted XML files.");

            // Adım 3: İşleme (veri transaction'ı + MV yenileme)
            var operationTime = DateTime.Now;
            var (transactionResult, syncInfo) = await transactionalSyncProcessor.ProcessUserListsAsync(
                pkXmlPath,
                gbXmlPath,
                operationTime,
                sw.Elapsed,
                cancellationToken);

            if (transactionResult == TransactionalSyncResult.SkippedByAdvisoryLock)
            {
                sw.Stop();
                metrics.RecordSync("partial", sw.Elapsed.TotalSeconds);
                await syncMetadataService.UpdateStatusOnlyAsync(
                    SyncRunStatus.Partial,
                    "Sync skipped because another sync run is currently active.",
                    attemptAt,
                    cancellationToken);
                logger.LogWarning("Sync run skipped because advisory lock was not acquired.");

                await webhookNotifier.NotifyAsync(new WebhookEvent
                {
                    EventType = WebhookEventType.SyncPartial,
                    Severity = WebhookSeverity.Warning,
                    Summary = $"GIB senkronizasyonu ATLANDI — başka bir senkronizasyon çalışıyor (advisory lock). Süre: {sw.Elapsed:hh\\:mm\\:ss\\.fff}",
                    Payload = new Dictionary<string, object>
                    {
                        ["Reason"] = "AdvisoryLockNotAcquired",
                        ["Duration"] = sw.Elapsed.ToString(@"hh\:mm\:ss\.fff"),
                        ["Status"] = "Atlandı"
                    }
                }, cancellationToken);

                return;
            }

            // Adım 4: Belge türüne göre XML.GZ arşiv üret (PK+GB birleşik, türe göre ayrık)
            var archiveErrors = await archiveService.GenerateDocumentTypeArchivesAsync(cancellationToken);
            postSyncWarnings.AddRange(archiveErrors);

            // Eski arşiv dosyalarını temizle
            var cleanupError = await archiveService.CleanupOldArchivesAsync(cancellationToken);
            if (!string.IsNullOrEmpty(cleanupError))
                postSyncWarnings.Add(cleanupError);

            sw.Stop();
            metrics.RecordSync("success", sw.Elapsed.TotalSeconds);
            if (postSyncWarnings.Count == 0)
            {
                await syncMetadataService.UpdateStatusOnlyAsync(
                    SyncRunStatus.Success,
                    error: null,
                    attemptAt,
                    cancellationToken);
            }
            else
            {
                var partialError = TrimToMaxLength(string.Join(" | ", postSyncWarnings), 2000);
                await syncMetadataService.UpdateStatusOnlyAsync(
                    SyncRunStatus.Partial,
                    partialError,
                    attemptAt,
                    cancellationToken);
            }
            syncTimeProvider.Invalidate();
            logger.LogInformation("GIB User list sync completed in {Duration}.", sw.Elapsed);

            if (postSyncWarnings.Count == 0)
            {
                await webhookNotifier.NotifyAsync(new WebhookEvent
                {
                    EventType = WebhookEventType.SyncCompleted,
                    Severity = WebhookSeverity.Info,
                    Summary = $"GIB mükellef listesi senkronizasyonu başarıyla tamamlandı. Süre: {sw.Elapsed:hh\\:mm\\:ss\\.fff}",
                    Payload = new Dictionary<string, object>
                    {
                        ["Duration"] = sw.Elapsed.ToString(@"hh\:mm\:ss\.fff"),
                        ["Status"] = "Başarılı",
                        ["PkCount"] = syncInfo?.TotalPkRecords ?? 0,
                        ["GbCount"] = syncInfo?.TotalGbRecords ?? 0,
                        ["EInvoice"] = new
                        {
                            Added = syncInfo?.EinvoiceAdded ?? 0,
                            Modified = syncInfo?.EinvoiceModified ?? 0,
                            Removed = syncInfo?.EinvoiceRemoved ?? 0,
                            Total = (syncInfo?.EinvoiceAdded ?? 0) + (syncInfo?.EinvoiceModified ?? 0),
                            TotalCount = syncInfo?.EinvoiceTotalCount ?? 0
                        },
                        ["EDespatch"] = new
                        {
                            Added = syncInfo?.EdespatchAdded ?? 0,
                            Modified = syncInfo?.EdespatchModified ?? 0,
                            Removed = syncInfo?.EdespatchRemoved ?? 0,
                            Total = (syncInfo?.EdespatchAdded ?? 0) + (syncInfo?.EdespatchModified ?? 0),
                            TotalCount = syncInfo?.EdespatchTotalCount ?? 0
                        }
                    }
                }, cancellationToken);
            }
            else
            {
                await webhookNotifier.NotifyAsync(new WebhookEvent
                {
                    EventType = WebhookEventType.SyncPartial,
                    Severity = WebhookSeverity.Warning,
                    Summary = $"GIB senkronizasyonu uyarılarla tamamlandı. Süre: {sw.Elapsed:hh\\:mm\\:ss\\.fff}",
                    Payload = new Dictionary<string, object>
                    {
                        ["Duration"] = sw.Elapsed.ToString(@"hh\:mm\:ss\.fff"),
                        ["Status"] = "Kısmi",
                        ["Warnings"] = string.Join(" | ", postSyncWarnings)
                    }
                }, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            metrics.RecordSync("failure", sw.Elapsed.TotalSeconds);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            await syncMetadataService.TryUpdateFailureStatusAsync(attemptAt, ex, cancellationToken);

            await webhookNotifier.NotifyAsync(new WebhookEvent
            {
                EventType = WebhookEventType.SyncFailed,
                Severity = WebhookSeverity.Critical,
                Summary = $"GIB mükellef listesi senkronizasyonu HATALI olarak sonlandı. Süre: {sw.Elapsed:hh\\:mm\\:ss\\.fff}",
                Payload = new Dictionary<string, object>
                {
                    ["Duration"] = sw.Elapsed.ToString(@"hh\:mm\:ss\.fff"),
                    ["Status"] = "Hatalı",
                    ["Error"] = ex.Message,
                    ["ExceptionType"] = ex.GetType().Name,
                    ["StackTrace"] = TrimToMaxLength(ex.StackTrace ?? "", 500)
                }
            }, cancellationToken);

            throw;
        }
        finally
        {
            metrics.DecrementSyncActive();
            CleanupTempFolder(tempFolder);
        }
    }

    private static string ExtractFirstXmlFromZip(string zipPath, string targetFolder, string prefix)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"No XML file found in {zipPath}");

        var extractPath = Path.Combine(targetFolder, $"{prefix}_{entry.Name}");
        entry.ExtractToFile(extractPath, overwrite: true);
        return extractPath;
    }

    private void CleanupTempFolder(string tempFolder)
    {
        try
        {
            if (Directory.Exists(tempFolder))
                Directory.Delete(tempFolder, recursive: true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to cleanup temp folder: {TempFolder}", tempFolder);
        }
    }

    private static string TrimToMaxLength(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        return value[..maxLength];
    }
}
