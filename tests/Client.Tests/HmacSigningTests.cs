using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MERSEL.Services.GibUserList.Client.Infrastructure;

namespace MERSEL.Services.GibUserList.Client.Tests;

public class HmacSigningTests
{
    private const string TestAccessKey = "test-access-key";
    private const string TestSecretKey = "super-secret-key-for-testing-123";

    [Fact]
    public void ComputeSignature_ShouldProduceConsistentResult()
    {
        var sig1 = HmacSigningHandler.ComputeSignature(TestSecretKey, "GET", "/api/v1/einvoice/1234567890", "1700000000");
        var sig2 = HmacSigningHandler.ComputeSignature(TestSecretKey, "GET", "/api/v1/einvoice/1234567890", "1700000000");

        sig1.Should().Be(sig2);
    }

    [Fact]
    public void ComputeSignature_DifferentPaths_ShouldProduceDifferentResults()
    {
        var sig1 = HmacSigningHandler.ComputeSignature(TestSecretKey, "GET", "/api/v1/einvoice/111", "1700000000");
        var sig2 = HmacSigningHandler.ComputeSignature(TestSecretKey, "GET", "/api/v1/einvoice/222", "1700000000");

        sig1.Should().NotBe(sig2);
    }

    [Fact]
    public void ComputeSignature_DifferentTimestamps_ShouldProduceDifferentResults()
    {
        var sig1 = HmacSigningHandler.ComputeSignature(TestSecretKey, "GET", "/test", "1700000000");
        var sig2 = HmacSigningHandler.ComputeSignature(TestSecretKey, "GET", "/test", "1700000001");

        sig1.Should().NotBe(sig2);
    }

    [Fact]
    public void ComputeSignature_DifferentMethods_ShouldProduceDifferentResults()
    {
        var sig1 = HmacSigningHandler.ComputeSignature(TestSecretKey, "GET", "/test", "1700000000");
        var sig2 = HmacSigningHandler.ComputeSignature(TestSecretKey, "POST", "/test", "1700000000");

        sig1.Should().NotBe(sig2);
    }

    [Fact]
    public void ComputeSignature_ShouldMatchManualHmacSha256Computation()
    {
        const string method = "GET";
        const string path = "/api/v1/einvoice/1234567890";
        const string timestamp = "1700000000";

        var stringToSign = $"{method}\n{path}\n{timestamp}";
        var keyBytes = Encoding.UTF8.GetBytes(TestSecretKey);
        var dataBytes = Encoding.UTF8.GetBytes(stringToSign);
        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(keyBytes, dataBytes));

        var result = HmacSigningHandler.ComputeSignature(TestSecretKey, method, path, timestamp);

        result.Should().Be(expected);
    }

    [Fact]
    public void ComputeSignature_OutputFormat_ShouldBeLowercaseHex()
    {
        var result = HmacSigningHandler.ComputeSignature(TestSecretKey, "GET", "/test", "1700000000");

        result.Should().MatchRegex(@"^[0-9a-f]{64}$"); // SHA256 = 64 hex karakter
    }

    [Fact]
    public async Task SendAsync_ShouldAddAllRequiredHeaders()
    {
        var headerCapture = new HeaderCaptureHandler();
        var signingHandler = new HmacSigningHandler(TestAccessKey, TestSecretKey)
        {
            InnerHandler = headerCapture
        };
        var client = new HttpClient(signingHandler) { BaseAddress = new Uri("http://test") };

        await client.GetAsync("/api/v1/einvoice/1234567890");

        headerCapture.CapturedHeaders.Should().ContainKey("X-Access-Key");
        headerCapture.CapturedHeaders.Should().ContainKey("X-Timestamp");
        headerCapture.CapturedHeaders.Should().ContainKey("X-Signature");
    }

    [Fact]
    public async Task SendAsync_ShouldSetCorrectAccessKey()
    {
        var headerCapture = new HeaderCaptureHandler();
        var signingHandler = new HmacSigningHandler(TestAccessKey, TestSecretKey)
        {
            InnerHandler = headerCapture
        };
        var client = new HttpClient(signingHandler) { BaseAddress = new Uri("http://test") };

        await client.GetAsync("/test");

        headerCapture.CapturedHeaders["X-Access-Key"].Should().Be(TestAccessKey);
    }

    [Fact]
    public async Task SendAsync_Signature_ShouldBeVerifiable()
    {
        var headerCapture = new HeaderCaptureHandler();
        var signingHandler = new HmacSigningHandler(TestAccessKey, TestSecretKey)
        {
            InnerHandler = headerCapture
        };
        var client = new HttpClient(signingHandler) { BaseAddress = new Uri("http://test") };

        await client.GetAsync("/api/v1/test");

        var timestamp = headerCapture.CapturedHeaders["X-Timestamp"];
        var signature = headerCapture.CapturedHeaders["X-Signature"];

        // Sunucu tarafında aynı hesaplama ile doğrulama
        var expectedSignature = HmacSigningHandler.ComputeSignature(
            TestSecretKey, "GET", "/api/v1/test", timestamp);

        signature.Should().Be(expectedSignature);
    }

    [Fact]
    public async Task SendAsync_Timestamp_ShouldBeRecentEpoch()
    {
        var headerCapture = new HeaderCaptureHandler();
        var signingHandler = new HmacSigningHandler(TestAccessKey, TestSecretKey)
        {
            InnerHandler = headerCapture
        };
        var client = new HttpClient(signingHandler) { BaseAddress = new Uri("http://test") };

        await client.GetAsync("/test");

        var timestamp = long.Parse(headerCapture.CapturedHeaders["X-Timestamp"]);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Math.Abs(now - timestamp).Should().BeLessThan(5); // 5 saniye tolerans
    }

    /// <summary>
    /// Gönderilen HTTP başlıklarını yakalayan test handler'ı.
    /// </summary>
    private sealed class HeaderCaptureHandler : HttpMessageHandler
    {
        public Dictionary<string, string> CapturedHeaders { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            foreach (var header in request.Headers)
            {
                CapturedHeaders[header.Key] = string.Join(",", header.Value);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
