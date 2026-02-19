using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using MERSEL.Services.GibUserList.Infrastructure.Services;

namespace MERSEL.Services.GibUserList.Infrastructure.Tests.Services;

public class GibDownloadRetryHandlerTests
{
    private readonly ILogger<GibDownloadRetryHandler> _logger = Substitute.For<ILogger<GibDownloadRetryHandler>>();

    [Fact]
    public async Task SendAsync_WhenFirstAttemptSucceeds_ShouldReturnImmediately()
    {
        var innerHandler = new FakeInnerHandler([HttpStatusCode.OK]);
        var retryHandler = CreateHandler(innerHandler);
        var client = new HttpClient(retryHandler) { BaseAddress = new Uri("http://test") };

        var response = await client.GetAsync("/data");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        innerHandler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_WhenServerError_ShouldRetryAndSucceed()
    {
        var innerHandler = new FakeInnerHandler([
            HttpStatusCode.InternalServerError,
            HttpStatusCode.OK
        ]);
        var retryHandler = CreateHandler(innerHandler);
        var client = new HttpClient(retryHandler) { BaseAddress = new Uri("http://test") };

        var response = await client.GetAsync("/data");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        innerHandler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task SendAsync_WhenAllRetriesFail_ShouldReturnLastResponse()
    {
        var innerHandler = new FakeInnerHandler([
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable // 4 kez (1 + 3 retry)
        ]);
        var retryHandler = CreateHandler(innerHandler);
        var client = new HttpClient(retryHandler) { BaseAddress = new Uri("http://test") };

        var response = await client.GetAsync("/data");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        innerHandler.CallCount.Should().Be(4); // İlk deneme + 3 retry
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task SendAsync_WithRetryableStatusCode_ShouldRetry(HttpStatusCode statusCode)
    {
        var innerHandler = new FakeInnerHandler([statusCode, HttpStatusCode.OK]);
        var retryHandler = CreateHandler(innerHandler);
        var client = new HttpClient(retryHandler) { BaseAddress = new Uri("http://test") };

        var response = await client.GetAsync("/data");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        innerHandler.CallCount.Should().Be(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task SendAsync_WithNonRetryableStatusCode_ShouldNotRetry(HttpStatusCode statusCode)
    {
        var innerHandler = new FakeInnerHandler([statusCode]);
        var retryHandler = CreateHandler(innerHandler);
        var client = new HttpClient(retryHandler) { BaseAddress = new Uri("http://test") };

        var response = await client.GetAsync("/data");

        response.StatusCode.Should().Be(statusCode);
        innerHandler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_OnHttpRequestException_ShouldRetryAndSucceed()
    {
        var innerHandler = new FakeInnerHandler(throwOnAttempts: [0]);
        var retryHandler = CreateHandler(innerHandler);
        var client = new HttpClient(retryHandler) { BaseAddress = new Uri("http://test") };

        var response = await client.GetAsync("/data");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        innerHandler.CallCount.Should().Be(2);
    }

    private GibDownloadRetryHandler CreateHandler(HttpMessageHandler innerHandler)
    {
        var handler = new GibDownloadRetryHandler(_logger)
        {
            InnerHandler = innerHandler
        };
        return handler;
    }

    /// <summary>
    /// Sıralı HTTP yanıtları veya hata fırlatma senaryoları için test handler'ı.
    /// </summary>
    private sealed class FakeInnerHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode[] _responses;
        private readonly HashSet<int> _throwOnAttempts;
        private int _callIndex;

        public int CallCount => _callIndex;

        public FakeInnerHandler(HttpStatusCode[]? responses = null, int[]? throwOnAttempts = null)
        {
            _responses = responses ?? [HttpStatusCode.OK];
            _throwOnAttempts = throwOnAttempts is not null ? [.. throwOnAttempts] : [];
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var currentAttempt = _callIndex++;

            if (_throwOnAttempts.Contains(currentAttempt))
                throw new HttpRequestException("Ağ hatası simülasyonu");

            var statusCode = currentAttempt < _responses.Length
                ? _responses[currentAttempt]
                : _responses[^1];

            return Task.FromResult(new HttpResponseMessage(statusCode));
        }
    }
}
