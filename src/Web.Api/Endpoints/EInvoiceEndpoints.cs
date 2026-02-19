using Mediator;
using MERSEL.Services.GibUserList.Application.Interfaces;
using MERSEL.Services.GibUserList.Application.Models;
using MERSEL.Services.GibUserList.Application.Queries;
using MERSEL.Services.GibUserList.Web.Infrastructure;

namespace MERSEL.Services.GibUserList.Web.Endpoints;

/// <summary>
/// e-Fatura mükellef sorgulama uç noktaları.
/// </summary>
public sealed class EInvoiceEndpoints : EndpointGroupBase
{
    public override void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/v1/einvoice")
            .WithTags("e-Invoice GibUsers")
            .WithOpenApi()
            .RequireAuthorization();

        group.MapGet("/{identifier}", GetByIdentifier)
            .WithName("GetEInvoiceGibUser")
            .WithSummary("VKN/TCKN ile e-Fatura mükellefi sorgula (opsiyonel firstCreationTime filtresi)")
            .Produces<GibUserDto>()
            .Produces(400)
            .Produces(404);

        group.MapGet("/", Search)
            .WithName("SearchEInvoiceGibUsers")
            .WithSummary("Ünvan veya kimlik numarası ile e-Fatura mükellefi ara")
            .Produces<GibUserSearchResult>()
            .Produces(400);

        group.MapPost("/batch", BatchGetByIdentifiers)
            .WithName("BatchGetEInvoiceGibUsers")
            .WithSummary("Birden fazla VKN/TCKN ile toplu e-Fatura mükellefi sorgula (maks 100)")
            .Produces<GibUserBatchResult>()
            .Produces(400);

        group.MapGet("/changes", GetChanges)
            .WithName("GetEInvoiceChanges")
            .WithSummary("Belirtilen tarihten sonraki e-Fatura mükellef değişikliklerini getirir")
            .Produces<GibUserChangesResult>()
            .Produces(400)
            .Produces(410);
    }

    private static async Task<IResult> GetByIdentifier(
        string identifier,
        DateTime? firstCreationTime,
        IMediator mediator,
        ISyncTimeProvider syncTime,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var query = new GetGibUserByIdentifierQuery(identifier, GibUserDocumentType.EInvoice, firstCreationTime);
        var result = await mediator.Send(query, ct);
        await AppendSyncHeader(syncTime, httpContext, ct);
        return result is not null ? Results.Ok(result) : Results.NotFound();
    }

    private static async Task<IResult> Search(
        string? search,
        int? page,
        int? pageSize,
        IMediator mediator,
        ISyncTimeProvider syncTime,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var query = new SearchGibUsersQuery(
            search ?? string.Empty,
            GibUserDocumentType.EInvoice,
            page ?? 1,
            pageSize ?? 20);

        var result = await mediator.Send(query, ct);
        await AppendSyncHeader(syncTime, httpContext, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> BatchGetByIdentifiers(
        BatchIdentifierRequest request,
        IMediator mediator,
        ISyncTimeProvider syncTime,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var query = new GetGibUsersByIdentifiersQuery(request.Identifiers, GibUserDocumentType.EInvoice);
        var result = await mediator.Send(query, ct);
        await AppendSyncHeader(syncTime, httpContext, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetChanges(
        DateTime since,
        DateTime? until,
        int? page,
        int? pageSize,
        IMediator mediator,
        ISyncTimeProvider syncTime,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var query = new GetGibUserChangesQuery(since, GibUserDocumentType.EInvoice, page ?? 1, pageSize ?? 100, until);
        var result = await mediator.Send(query, ct);
        await AppendSyncHeader(syncTime, httpContext, ct);

        if (result is null)
            return Results.Problem(
                title: "Delta süresi dolmuş",
                detail: "Belirtilen tarih changelog retention süresi dışında. Full re-sync gereklidir.",
                statusCode: 410);

        return Results.Ok(result);
    }

    private static async Task AppendSyncHeader(ISyncTimeProvider syncTime, HttpContext httpContext, CancellationToken ct)
    {
        var lastSync = await syncTime.GetLastSyncAtAsync(ct);
        if (lastSync.HasValue)
            httpContext.Response.Headers["X-Last-Sync-At"] = lastSync.Value.ToString("O");
    }
}

/// <summary>
/// Toplu kimlik numarası sorgulama isteği.
/// </summary>
public sealed record BatchIdentifierRequest
{
    /// <summary>Sorgulanacak VKN/TCKN listesi (maks 100)</summary>
    public IReadOnlyList<string> Identifiers { get; init; } = [];
}
