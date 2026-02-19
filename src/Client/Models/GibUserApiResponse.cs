namespace MERSEL.Services.GibUserList.Client.Models;

/// <summary>
/// API yanıtını ve X-Last-Sync-At header bilgisini birlikte taşıyan wrapper.
/// </summary>
/// <typeparam name="T">Yanıt gövdesi tipi</typeparam>
public sealed record GibUserApiResponse<T>
{
    /// <summary>API yanıt gövdesi</summary>
    public T Data { get; init; } = default!;

    /// <summary>
    /// Son başarılı senkronizasyon zamanı (X-Last-Sync-At header'ından).
    /// Henüz hiç senkronizasyon yapılmamışsa null döner.
    /// </summary>
    public DateTime? LastSyncAt { get; init; }
}
