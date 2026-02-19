using Mediator;
using Microsoft.EntityFrameworkCore;
using MERSEL.Services.GibUserList.Application.Interfaces;
using MERSEL.Services.GibUserList.Application.Models;
using MERSEL.Services.GibUserList.Domain.Entities;

namespace MERSEL.Services.GibUserList.Application.Queries;

/// <summary>
/// Son senkronizasyon durumunu getiren sorgu.
/// Doğrulama gerektirmez (parametresiz).
/// </summary>
public sealed record GetSyncStatusQuery : IQuery<SyncStatusDto>;

public sealed class GetSyncStatusQueryHandler(
    IGibUserListReadDbContext dbContext)
    : IQueryHandler<GetSyncStatusQuery, SyncStatusDto>
{
    public async ValueTask<SyncStatusDto> Handle(
        GetSyncStatusQuery request, CancellationToken ct)
    {
        var metadata = await dbContext.SyncMetadata
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Key == SyncMetadata.SingletonKey, ct);

        return new SyncStatusDto
        {
            LastSyncAt = metadata?.LastSyncAt,
            EInvoiceUserCount = metadata?.EInvoiceUserCount ?? 0,
            EDespatchUserCount = metadata?.EDespatchUserCount ?? 0,
            LastSyncDuration = metadata?.LastSyncDuration,
            LastSyncStatus = metadata?.LastSyncStatus ?? SyncRunStatus.Success,
            LastSyncError = metadata?.LastSyncError,
            LastAttemptAt = metadata?.LastAttemptAt,
            LastFailureAt = metadata?.LastFailureAt
        };
    }
}
