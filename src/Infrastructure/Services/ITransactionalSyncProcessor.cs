namespace MERSEL.Services.GibUserList.Infrastructure.Services;

public enum TransactionalSyncResult
{
    Applied = 0,
    SkippedByAdvisoryLock = 1
}

public sealed record SyncResultInfo(
    int EinvoiceAdded,
    int EinvoiceModified,
    int EinvoiceRemoved,
    int EdespatchAdded,
    int EdespatchModified,
    int EdespatchRemoved,
    int TotalPkRecords,
    int TotalGbRecords)
{
    public int EinvoiceTotalCount { get; init; }
    public int EdespatchTotalCount { get; init; }
    public int EinvoiceTotal => EinvoiceAdded + EinvoiceModified;
    public int EdespatchTotal => EdespatchAdded + EdespatchModified;
}

public interface ITransactionalSyncProcessor
{
    Task<(TransactionalSyncResult Result, SyncResultInfo? Info)> ProcessUserListsAsync(
        string pkXmlPath,
        string gbXmlPath,
        DateTime operationTime,
        TimeSpan duration,
        CancellationToken cancellationToken);
}
