namespace MERSEL.Services.GibUserList.Client.Exceptions;

/// <summary>
/// API'den alınan yanıt beklenen formatta deserialize edilemediğinde fırlatılır.
/// </summary>
public sealed class GibUserListDeserializationException : Exception
{
    public GibUserListDeserializationException()
        : base("API yanıtı beklenilen formatta deserialize edilemedi.") { }

    public GibUserListDeserializationException(string message)
        : base(message) { }

    public GibUserListDeserializationException(string message, Exception innerException)
        : base(message, innerException) { }
}
