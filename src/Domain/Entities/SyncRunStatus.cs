namespace MERSEL.Services.GibUserList.Domain.Entities;

/// <summary>
/// Senkronizasyon çalışma durumunu temsil eder.
/// </summary>
public static class SyncRunStatus
{
    public const string Success = "success";
    public const string Partial = "partial";
    public const string Failed = "failed";
}
