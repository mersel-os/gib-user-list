using Mediator;
using MERSEL.Services.GibUserList.Application.Models;
using MERSEL.Services.GibUserList.Application.Queries;
using MERSEL.Services.GibUserList.Web.Infrastructure;

namespace MERSEL.Services.GibUserList.Web.Endpoints;

/// <summary>
/// Durum ve sağlık uç noktaları.
/// </summary>
public sealed class StatusEndpoints : EndpointGroupBase
{
    public override void Map(WebApplication app)
    {
        app.MapGet("/api/v1/status", GetStatus)
            .WithTags("Status")
            .WithName("GetSyncStatus")
            .WithSummary("Son senkronizasyon durumunu ve mükellef sayılarını getir")
            .WithOpenApi()
            .RequireAuthorization()
            .Produces<SyncStatusDto>();
    }

    private static async Task<IResult> GetStatus(
        IMediator mediator,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetSyncStatusQuery(), ct);
        return Results.Ok(result);
    }
}
