namespace MERSEL.Services.GibUserList.Client.Exceptions;

/// <summary>
/// API 410 Gone döndüğünde fırlatılır.
/// İstemcinin belirttiği "since" tarihi changelog retention süresi dışındadır;
/// tam bir yeniden senkronizasyon (full re-sync) gereklidir.
/// </summary>
public sealed class GibUserListSyncExpiredException : Exception
{
    public GibUserListSyncExpiredException()
        : base("Delta süresi dolmuş. Full re-sync gereklidir.") { }

    public GibUserListSyncExpiredException(string message)
        : base(message) { }

    public GibUserListSyncExpiredException(string message, Exception innerException)
        : base(message, innerException) { }
}
