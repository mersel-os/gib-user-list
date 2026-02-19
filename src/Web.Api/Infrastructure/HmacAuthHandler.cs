using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using MERSEL.Services.GibUserList.Infrastructure.Diagnostics;

namespace MERSEL.Services.GibUserList.Web.Infrastructure;

/// <summary>
/// HMAC-SHA256 tabanlı kimlik doğrulama işleyicisi.
///
/// İstemci her istekte şu başlıkları gönderir:
///   X-Access-Key  : Yapılandırmada tanımlı erişim anahtarı (public identifier)
///   X-Timestamp   : Unix epoch saniye cinsinden istek zamanı
///   X-Signature   : HMAC-SHA256(SecretKey, "{METHOD}\n{PathAndQuery}\n{Timestamp}") — hex kodlu
///
/// Sunucu tarafında:
///   1) AccessKey ile eşleşen istemci kaydını bulur
///   2) Timestamp tolerans penceresini kontrol eder (varsayılan 5 dk)
///   3) İmzayı aynı algoritma ile yeniden hesaplar ve sabit-zaman karşılaştırması yapar
///
/// Authentication:Enabled = false olduğunda tüm istekler anonim olarak geçer.
///
/// Her kimlik doğrulama girişimi GibUserListMetrics üzerinden kaydedilir:
///   - client etiketi: istemci adı veya "anonymous"
///   - status etiketi: "success", "failure", "disabled"
/// </summary>
public sealed class HmacAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "HmacAuth";

    private const string AccessKeyHeader = "X-Access-Key";
    private const string TimestampHeader = "X-Timestamp";
    private const string SignatureHeader = "X-Signature";

    private const int DefaultTimestampToleranceSeconds = 300; // 5 dakika

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var metrics = Context.RequestServices.GetService<GibUserListMetrics>();

        // Kimlik doğrulama devre dışıysa anonim olarak geçir
        var enabled = configuration.GetValue<bool>("Authentication:Enabled");
        if (!enabled)
        {
            metrics?.RecordAuthRequest("anonymous", "disabled");
            return Task.FromResult(AuthenticateResult.Success(CreateTicket("anonymous")));
        }

        // Gerekli başlıkları kontrol et
        if (!Request.Headers.TryGetValue(AccessKeyHeader, out var accessKeyValues) ||
            string.IsNullOrEmpty(accessKeyValues.ToString()))
        {
            metrics?.RecordAuthRequest("unknown", "failure", "missing_access_key");
            return Task.FromResult(AuthenticateResult.Fail($"Eksik başlık: {AccessKeyHeader}"));
        }

        if (!Request.Headers.TryGetValue(TimestampHeader, out var timestampValues) ||
            string.IsNullOrEmpty(timestampValues.ToString()))
        {
            metrics?.RecordAuthRequest("unknown", "failure", "missing_timestamp");
            return Task.FromResult(AuthenticateResult.Fail($"Eksik başlık: {TimestampHeader}"));
        }

        if (!Request.Headers.TryGetValue(SignatureHeader, out var signatureValues) ||
            string.IsNullOrEmpty(signatureValues.ToString()))
        {
            metrics?.RecordAuthRequest("unknown", "failure", "missing_signature");
            return Task.FromResult(AuthenticateResult.Fail($"Eksik başlık: {SignatureHeader}"));
        }

        var accessKey = accessKeyValues.ToString();
        var timestampStr = timestampValues.ToString();
        var providedSignature = signatureValues.ToString();

        // Timestamp geçerliliğini kontrol et
        if (!long.TryParse(timestampStr, out var timestampEpoch))
        {
            metrics?.RecordAuthRequest(accessKey, "failure", "invalid_timestamp");
            return Task.FromResult(AuthenticateResult.Fail("Geçersiz timestamp formatı."));
        }

        var toleranceSeconds = configuration.GetValue<int?>("Authentication:TimestampToleranceSeconds")
                               ?? DefaultTimestampToleranceSeconds;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var diff = Math.Abs(now - timestampEpoch);

        if (diff > toleranceSeconds)
        {
            metrics?.RecordAuthRequest(accessKey, "failure", "timestamp_expired");
            return Task.FromResult(AuthenticateResult.Fail(
                $"Timestamp tolerans dışında. Fark: {diff}s, Tolerans: {toleranceSeconds}s"));
        }

        // AccessKey ile eşleşen istemciyi bul
        var clients = configuration.GetSection("Authentication:Clients").Get<HmacClientConfig[]>() ?? [];
        var matchedClient = Array.Find(clients, c =>
            string.Equals(c.AccessKey, accessKey, StringComparison.Ordinal));

        if (matchedClient is null || string.IsNullOrEmpty(matchedClient.SecretKey))
        {
            metrics?.RecordAuthRequest(accessKey, "failure", "unknown_access_key");
            return Task.FromResult(AuthenticateResult.Fail("Bilinmeyen AccessKey."));
        }

        // İmzayı yeniden hesapla ve karşılaştır
        var method = Request.Method.ToUpperInvariant();
        var path = Request.Path.Value ?? "/";
        var pathAndQuery = $"{path}{Request.QueryString.Value}";

        var expectedSignature = ComputeSignature(matchedClient.SecretKey, method, pathAndQuery, timestampStr);

        var providedBytes = Encoding.UTF8.GetBytes(providedSignature);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedSignature);

        var clientName = matchedClient.Name ?? accessKey;

        var matchesCanonical = providedBytes.Length == expectedBytes.Length &&
                               CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);

        if (!matchesCanonical)
        {
            metrics?.RecordAuthRequest(clientName, "failure", "invalid_signature");
            return Task.FromResult(AuthenticateResult.Fail("Geçersiz imza."));
        }

        metrics?.RecordAuthRequest(clientName, "success");
        return Task.FromResult(AuthenticateResult.Success(CreateTicket(clientName)));
    }

    /// <summary>
    /// İmzalanacak metni oluşturur ve HMAC-SHA256 ile hex olarak döner.
    /// Format: "{METHOD}\n{PathAndQuery}\n{Timestamp}"
    /// </summary>
    internal static string ComputeSignature(string secretKey, string method, string path, string timestamp)
    {
        var stringToSign = $"{method}\n{path}\n{timestamp}";
        var keyBytes = Encoding.UTF8.GetBytes(secretKey);
        var dataBytes = Encoding.UTF8.GetBytes(stringToSign);

        var hash = HMACSHA256.HashData(keyBytes, dataBytes);
        return Convert.ToHexStringLower(hash);
    }

    private AuthenticationTicket CreateTicket(string name)
    {
        var claims = new[] { new Claim(ClaimTypes.Name, name) };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        return new AuthenticationTicket(principal, SchemeName);
    }
}

/// <summary>
/// Yapılandırmadan okunan HMAC istemci tanımı.
/// </summary>
public sealed class HmacClientConfig
{
    /// <summary>İstemcinin erişim anahtarı (public)</summary>
    public string AccessKey { get; set; } = default!;

    /// <summary>İmza oluşturmak için kullanılan gizli anahtar</summary>
    public string SecretKey { get; set; } = default!;

    /// <summary>İstemci adı (log/claims için, isteğe bağlı)</summary>
    public string? Name { get; set; }
}
