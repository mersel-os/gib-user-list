using System.Net;
using System.Text.Json;
using FluentAssertions;
using MERSEL.Services.GibUserList.Client;
using MERSEL.Services.GibUserList.Client.Models;

namespace MERSEL.Services.GibUserList.Client.Tests;

public class GibUserListClientBatchTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task BatchGetEInvoiceGibUsersAsync_ShouldReturnBatchResult()
    {
        var expected = new GibUserBatchResponse
        {
            Items =
            [
                new GibUserResponse
                {
                    Identifier = "1111111111",
                    Title = "FIRMA A",
                    FirstCreationTime = DateTime.Now
                }
            ],
            NotFound = ["9999999999"],
            TotalRequested = 2,
            TotalFound = 1
        };

        var handler = new FakeHttpHandler(HttpStatusCode.OK, JsonSerializer.Serialize(expected, JsonOptions));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
        var client = new GibUserListClient(httpClient);

        var result = await client.BatchGetEInvoiceGibUsersAsync(["1111111111", "9999999999"]);

        result.Data.Should().NotBeNull();
        result.Data.Items.Should().HaveCount(1);
        result.Data.NotFound.Should().ContainSingle().Which.Should().Be("9999999999");
        result.Data.TotalRequested.Should().Be(2);
        result.Data.TotalFound.Should().Be(1);
    }

    [Fact]
    public async Task BatchGetEDespatchGibUsersAsync_ShouldReturnBatchResult()
    {
        var expected = new GibUserBatchResponse
        {
            Items =
            [
                new GibUserResponse
                {
                    Identifier = "2222222222",
                    Title = "KARGO FIRMASI",
                    FirstCreationTime = DateTime.Now
                }
            ],
            NotFound = [],
            TotalRequested = 1,
            TotalFound = 1
        };

        var handler = new FakeHttpHandler(HttpStatusCode.OK, JsonSerializer.Serialize(expected, JsonOptions));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
        var client = new GibUserListClient(httpClient);

        var result = await client.BatchGetEDespatchGibUsersAsync(["2222222222"]);

        result.Data.Items.Should().HaveCount(1);
        result.Data.Items[0].Title.Should().Be("KARGO FIRMASI");
    }

    [Fact]
    public async Task GetEDespatchGibUserAsync_WhenFound_ShouldReturnUser()
    {
        var expected = new GibUserResponse
        {
            Identifier = "1234567890",
            Title = "TEST FIRMA",
            FirstCreationTime = DateTime.Now
        };

        var handler = new FakeHttpHandler(HttpStatusCode.OK, JsonSerializer.Serialize(expected, JsonOptions));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
        var client = new GibUserListClient(httpClient);

        var result = await client.GetEDespatchGibUserAsync("1234567890");

        result.Data.Should().NotBeNull();
        result.Data!.Identifier.Should().Be("1234567890");
    }

    [Fact]
    public async Task GetEDespatchGibUserAsync_WhenNotFound_ShouldReturnNullData()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.NotFound, "");
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
        var client = new GibUserListClient(httpClient);

        var result = await client.GetEDespatchGibUserAsync("0000000000");

        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task GetEInvoiceGibUserAsync_WithFirstCreationTime_ShouldIncludeQueryParam()
    {
        var expected = new GibUserResponse
        {
            Identifier = "1234567890",
            Title = "TEST",
            FirstCreationTime = DateTime.Now
        };

        var handler = new FakeHttpHandler(HttpStatusCode.OK, JsonSerializer.Serialize(expected, JsonOptions));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
        var client = new GibUserListClient(httpClient);

        var firstCreationTime = new DateTime(2025, 1, 1, 12, 0, 0);
        var result = await client.GetEInvoiceGibUserAsync("1234567890", firstCreationTime);

        result.Data.Should().NotBeNull();
        handler.LastRequestUri.Should().Contain("firstCreationTime=");
    }

    [Fact]
    public async Task SearchEDespatchGibUsersAsync_ShouldReturnPaginatedResult()
    {
        var expected = new GibUserSearchResponse
        {
            Items = [new GibUserResponse { Identifier = "111", Title = "TEST", FirstCreationTime = DateTime.Now }],
            TotalCount = 1,
            Page = 1,
            PageSize = 20,
            TotalPages = 1
        };

        var handler = new FakeHttpHandler(HttpStatusCode.OK, JsonSerializer.Serialize(expected, JsonOptions));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
        var client = new GibUserListClient(httpClient);

        var result = await client.SearchEDespatchGibUsersAsync("TEST");

        result.Data.Should().NotBeNull();
        result.Data.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetSyncStatusAsync_WhenServerError_ShouldThrow()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.InternalServerError, "");
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
        var client = new GibUserListClient(httpClient);

        var act = () => client.GetSyncStatusAsync();

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task BatchGetEInvoiceGibUsersAsync_WithSyncHeader_ShouldParseSyncTime()
    {
        var syncAt = new DateTime(2026, 2, 17, 3, 0, 0);
        var expected = new GibUserBatchResponse
        {
            Items = [],
            NotFound = [],
            TotalRequested = 0,
            TotalFound = 0
        };

        var handler = new FakeHttpHandler(HttpStatusCode.OK, JsonSerializer.Serialize(expected, JsonOptions),
            lastSyncAt: syncAt);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
        var client = new GibUserListClient(httpClient);

        var result = await client.BatchGetEInvoiceGibUsersAsync([]);

        result.LastSyncAt.Should().NotBeNull();
        result.LastSyncAt!.Value.Should().BeCloseTo(syncAt, TimeSpan.FromSeconds(1));
    }

    private sealed class FakeHttpHandler(
        HttpStatusCode statusCode,
        string responseContent,
        DateTime? lastSyncAt = null) : HttpMessageHandler
    {
        public string? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri?.ToString();
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseContent, System.Text.Encoding.UTF8, "application/json")
            };

            if (lastSyncAt.HasValue)
                response.Headers.TryAddWithoutValidation("X-Last-Sync-At", lastSyncAt.Value.ToString("O"));

            return Task.FromResult(response);
        }
    }
}
