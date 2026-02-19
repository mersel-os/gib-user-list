namespace MERSEL.Services.GibUserList.Client.Models;

/// <summary>
/// API'dan dönen senkronizasyon durumu yanıtı.
/// </summary>
public sealed record SyncStatusResponse
{
    public DateTime? LastSyncAt { get; init; }
    public int EInvoiceUserCount { get; init; }
    public int EDespatchUserCount { get; init; }
    public TimeSpan? LastSyncDuration { get; init; }
    public string LastSyncStatus { get; init; } = "success";
    public string? LastSyncError { get; init; }
    public DateTime? LastAttemptAt { get; init; }
    public DateTime? LastFailureAt { get; init; }
}
