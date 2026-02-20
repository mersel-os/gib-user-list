using MERSEL.Services.GibUserList.Application.Interfaces;
using MERSEL.Services.GibUserList.Domain.Entities;
using MERSEL.Services.GibUserList.Web.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MERSEL.Services.GibUserList.Web.Endpoints;

/// <summary>
/// Arşiv dosyası yanıt modeli.
/// </summary>
public sealed record ArchiveFileResponse(string FileName, long SizeBytes, DateTime CreatedAt, int UserCount);

/// <summary>
/// e-Fatura mükellef listesi arşiv uç noktaları.
/// Her sync sonrası PK+GB verisi birleştirilerek sadece Invoice kullanıcılarını içeren XML.GZ üretilir.
/// Tüketici "latest" ile son tam listeyi indirip, sonra /changes ile delta takibi yapabilir.
/// </summary>
public sealed class EInvoiceArchivesEndpoints : EndpointGroupBase
{
    public override void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/v1/einvoice/archives")
            .WithTags("e-Invoice Archives")
            .WithOpenApi()
            .RequireAuthorization();

        group.MapGet("/", ListArchives)
            .WithName("ListEInvoiceArchives")
            .WithSummary("e-Fatura mükellef listesi arşiv dosyalarını listeler")
            .Produces<IReadOnlyList<ArchiveFileResponse>>();

        group.MapGet("/latest", DownloadLatest)
            .WithName("DownloadLatestEInvoiceArchive")
            .WithSummary("En son e-Fatura mükellef listesi arşivini indirir (tam liste — bootstrap için)")
            .Produces(200, contentType: "application/gzip")
            .Produces(404);

        group.MapGet("/{fileName}", DownloadArchive)
            .WithName("DownloadEInvoiceArchive")
            .WithSummary("Belirtilen e-Fatura arşiv dosyasını indirir")
            .Produces(200, contentType: "application/gzip")
            .Produces(404);
    }

    private static async Task<IResult> ListArchives(
        IGibUserListReadDbContext dbContext, ISyncTimeProvider syncTime, HttpContext httpContext, CancellationToken ct)
    {
        var archives = await dbContext.ArchiveFiles
            .AsNoTracking()
            .Where(a => a.DocumentType == GibDocumentType.EInvoice)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new ArchiveFileResponse(a.FileName, a.SizeBytes, a.CreatedAt, a.UserCount))
            .ToListAsync(ct);

        await AppendSyncHeader(syncTime, httpContext, ct);
        return Results.Ok(archives);
    }

    private static async Task<IResult> DownloadLatest(
        IGibUserListReadDbContext dbContext, IArchiveStorage archiveStorage,
        ISyncTimeProvider syncTime, HttpContext httpContext, CancellationToken ct)
    {
        var latest = await dbContext.ArchiveFiles
            .AsNoTracking()
            .Where(a => a.DocumentType == GibDocumentType.EInvoice)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (latest is null)
            return Results.NotFound("Henüz e-Fatura arşivi üretilmedi.");

        var stream = await archiveStorage.GetAsync(latest.FileName, ct);
        if (stream is null)
            return Results.NotFound();

        await AppendSyncHeader(syncTime, httpContext, ct);
        return Results.File(stream, "application/gzip", Path.GetFileName(latest.FileName));
    }

    private static async Task<IResult> DownloadArchive(
        string fileName, IArchiveStorage archiveStorage, ISyncTimeProvider syncTime, HttpContext httpContext, CancellationToken ct)
    {
        if (!ArchiveFileNameGuard.IsValid(fileName))
            return Results.BadRequest("Geçersiz dosya adı.");

        var stream = await archiveStorage.GetAsync($"einvoice/{fileName}", ct);
        if (stream is null)
            return Results.NotFound();

        await AppendSyncHeader(syncTime, httpContext, ct);
        return Results.File(stream, "application/gzip", fileName);
    }

    private static async Task AppendSyncHeader(ISyncTimeProvider syncTime, HttpContext httpContext, CancellationToken ct)
    {
        var lastSync = await syncTime.GetLastSyncAtAsync(ct);
        if (lastSync.HasValue)
            httpContext.Response.Headers["X-Last-Sync-At"] = lastSync.Value.ToString("O");
    }
}

/// <summary>
/// e-İrsaliye mükellef listesi arşiv uç noktaları.
/// </summary>
public sealed class EDespatchArchivesEndpoints : EndpointGroupBase
{
    public override void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/v1/edespatch/archives")
            .WithTags("e-Despatch Archives")
            .WithOpenApi()
            .RequireAuthorization();

        group.MapGet("/", ListArchives)
            .WithName("ListEDespatchArchives")
            .WithSummary("e-İrsaliye mükellef listesi arşiv dosyalarını listeler")
            .Produces<IReadOnlyList<ArchiveFileResponse>>();

        group.MapGet("/latest", DownloadLatest)
            .WithName("DownloadLatestEDespatchArchive")
            .WithSummary("En son e-İrsaliye mükellef listesi arşivini indirir (tam liste — bootstrap için)")
            .Produces(200, contentType: "application/gzip")
            .Produces(404);

        group.MapGet("/{fileName}", DownloadArchive)
            .WithName("DownloadEDespatchArchive")
            .WithSummary("Belirtilen e-İrsaliye arşiv dosyasını indirir")
            .Produces(200, contentType: "application/gzip")
            .Produces(404);
    }

    private static async Task<IResult> ListArchives(
        IGibUserListReadDbContext dbContext, ISyncTimeProvider syncTime, HttpContext httpContext, CancellationToken ct)
    {
        var archives = await dbContext.ArchiveFiles
            .AsNoTracking()
            .Where(a => a.DocumentType == GibDocumentType.EDespatch)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new ArchiveFileResponse(a.FileName, a.SizeBytes, a.CreatedAt, a.UserCount))
            .ToListAsync(ct);

        await AppendSyncHeader(syncTime, httpContext, ct);
        return Results.Ok(archives);
    }

    private static async Task<IResult> DownloadLatest(
        IGibUserListReadDbContext dbContext, IArchiveStorage archiveStorage,
        ISyncTimeProvider syncTime, HttpContext httpContext, CancellationToken ct)
    {
        var latest = await dbContext.ArchiveFiles
            .AsNoTracking()
            .Where(a => a.DocumentType == GibDocumentType.EDespatch)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (latest is null)
            return Results.NotFound("Henüz e-İrsaliye arşivi üretilmedi.");

        var stream = await archiveStorage.GetAsync(latest.FileName, ct);
        if (stream is null)
            return Results.NotFound();

        await AppendSyncHeader(syncTime, httpContext, ct);
        return Results.File(stream, "application/gzip", Path.GetFileName(latest.FileName));
    }

    private static async Task<IResult> DownloadArchive(
        string fileName, IArchiveStorage archiveStorage, ISyncTimeProvider syncTime, HttpContext httpContext, CancellationToken ct)
    {
        if (!ArchiveFileNameGuard.IsValid(fileName))
            return Results.BadRequest("Geçersiz dosya adı.");

        var stream = await archiveStorage.GetAsync($"edespatch/{fileName}", ct);
        if (stream is null)
            return Results.NotFound();

        await AppendSyncHeader(syncTime, httpContext, ct);
        return Results.File(stream, "application/gzip", fileName);
    }

    private static async Task AppendSyncHeader(ISyncTimeProvider syncTime, HttpContext httpContext, CancellationToken ct)
    {
        var lastSync = await syncTime.GetLastSyncAtAsync(ct);
        if (lastSync.HasValue)
            httpContext.Response.Headers["X-Last-Sync-At"] = lastSync.Value.ToString("O");
    }
}

/// <summary>
/// Path traversal koruması — Path.GetFileName ile normalize edip orijinalle karşılaştırır.
/// "..", "/", "\" ve encoded varyantları dahil tüm traversal girişimlerini engeller.
/// </summary>
file static class ArchiveFileNameGuard
{
    public static bool IsValid(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        var normalized = Path.GetFileName(fileName);
        return string.Equals(normalized, fileName, StringComparison.Ordinal)
            && !fileName.Contains('\0');
    }
}
