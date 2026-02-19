using System.Net;
using System.Text.Json;
using FluentAssertions;
using MERSEL.Services.GibUserList.Client;
using MERSEL.Services.GibUserList.Client.Models;

namespace MERSEL.Services.GibUserList.Client.Tests;

public class GibUserListClientTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task GetEInvoiceGibUserAsync_WhenFound_ShouldReturnGibUser()
    {
        var expected = new GibUserResponse
        {
            Identifier = "1234567890",
            Title = "MERSEL YAZILIM A.S.",
            FirstCreationTime = DateTime.Now,
            Aliases = [new GibUserAliasModel { Name = "urn:mail:defaultpk", Type = "PK", CreationTime = DateTime.Now }]
        };

        var handler = new FakeHttpHandler(HttpStatusCode.OK, JsonSerializer.Serialize(expected, JsonOptions));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
        var client = new GibUserListClient(httpClient);

        var result = await client.GetEInvoiceGibUserAsync("1234567890");

        result.Data.Should().NotBeNull();
        result.Data!.Identifier.Should().Be("1234567890");
        result.Data.Title.Should().Be("MERSEL YAZILIM A.S.");
        result.Data.Aliases.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetEInvoiceGibUserAsync_WhenFound_WithSyncHeader_ShouldParseSyncTime()
    {
        var syncAt = new DateTime(2026, 2, 17, 3, 0, 0);
        var expected = new GibUserResponse
        {
            Identifier = "1234567890",
            Title = "TEST",
            FirstCreationTime = DateTime.Now
        };

        var handler = new FakeHttpHandler(HttpStatusCode.OK, JsonSerializer.Serialize(expected, JsonOptions),
            lastSyncAt: syncAt);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
        var client = new GibUserListClient(httpClient);

        var result = await client.GetEInvoiceGibUserAsync("1234567890");

        result.Data.Should().NotBeNull();
        result.LastSyncAt.Should().NotBeNull();
        result.LastSyncAt!.Value.Should().BeCloseTo(syncAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task GetEInvoiceGibUserAsync_WhenNotFound_ShouldReturnNullData()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.NotFound, "");
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
        var client = new GibUserListClient(httpClient);

        var result = await client.GetEInvoiceGibUserAsync("0000000000");

        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task SearchEInvoiceGibUsersAsync_ShouldReturnPaginatedResult()
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

        var result = await client.SearchEInvoiceGibUsersAsync("TEST");

        result.Data.Should().NotBeNull();
        result.Data.Items.Should().HaveCount(1);
        result.Data.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetSyncStatusAsync_ShouldReturnStatus()
    {
        var expected = new SyncStatusResponse
        {
            LastSyncAt = DateTime.Now,
            EInvoiceUserCount = 150_000,
            EDespatchUserCount = 80_000,
            LastSyncDuration = TimeSpan.FromMinutes(3)
        };

        var handler = new FakeHttpHandler(HttpStatusCode.OK, JsonSerializer.Serialize(expected, JsonOptions));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
        var client = new GibUserListClient(httpClient);

        var result = await client.GetSyncStatusAsync();

        result.EInvoiceUserCount.Should().Be(150_000);
        result.EDespatchUserCount.Should().Be(80_000);
    }

    private sealed class FakeHttpHandler(
        HttpStatusCode statusCode,
        string responseContent,
        DateTime? lastSyncAt = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
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
