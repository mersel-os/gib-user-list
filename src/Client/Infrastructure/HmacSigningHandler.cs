using System.Security.Cryptography;
using System.Text;

namespace MERSEL.Services.GibUserList.Client.Infrastructure;

/// <summary>
/// Giden HTTP isteklerini HMAC-SHA256 ile otomatik imzalayan DelegatingHandler.
///
/// Her isteğe şu başlıkları ekler:
///   X-Access-Key  : İstemcinin public erişim anahtarı
///   X-Timestamp   : Unix epoch saniye cinsinden istek zamanı
///   X-Signature   : HMAC-SHA256(SecretKey, "{METHOD}\n{PathAndQuery}\n{Timestamp}") — hex kodlu
/// </summary>
internal sealed class HmacSigningHandler(string accessKey, string secretKey) : DelegatingHandler
{
    private const string AccessKeyHeader = "X-Access-Key";
    private const string TimestampHeader = "X-Timestamp";
    private const string SignatureHeader = "X-Signature";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var method = request.Method.Method.ToUpperInvariant();
        var path = request.RequestUri?.AbsolutePath ?? "/";
        var query = request.RequestUri?.Query ?? string.Empty;
        var pathAndQuery = $"{path}{query}";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        var signature = ComputeSignature(secretKey, method, pathAndQuery, timestamp);

        request.Headers.Remove(AccessKeyHeader);
        request.Headers.Remove(TimestampHeader);
        request.Headers.Remove(SignatureHeader);

        request.Headers.Add(AccessKeyHeader, accessKey);
        request.Headers.Add(TimestampHeader, timestamp);
        request.Headers.Add(SignatureHeader, signature);

        return base.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Sunucu tarafındaki hesaplama ile birebir aynı format.
    /// </summary>
    internal static string ComputeSignature(string secret, string method, string path, string timestamp)
    {
        var stringToSign = $"{method}\n{path}\n{timestamp}";
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var dataBytes = Encoding.UTF8.GetBytes(stringToSign);

        var hash = HMACSHA256.HashData(keyBytes, dataBytes);
#if NET9_0_OR_GREATER
        return Convert.ToHexStringLower(hash);
#else
        return Convert.ToHexString(hash).ToLowerInvariant();
#endif
    }
}
