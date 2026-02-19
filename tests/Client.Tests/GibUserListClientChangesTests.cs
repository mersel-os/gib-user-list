using System.Net;
using System.Text.Json;
using FluentAssertions;
using MERSEL.Services.GibUserList.Client.Exceptions;
using MERSEL.Services.GibUserList.Client.Models;

namespace MERSEL.Services.GibUserList.Client.Tests;

public class GibUserListClientChangesTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task GetEInvoiceChangesAsync_ShouldReturnChanges()
    {
        var expected = new GibUserChangesResponse
        {
            Changes =
            [
                new GibUserChangeResponse
                {
                    Identifier = "1234567890",
                    ChangeType = "added",
                    ChangedAt = new DateTime(2026, 2, 18, 3, 0, 0),
                    Title = "TEST FIRMA"
                }
            ],
            TotalCount = 1,
            Page = 1,
            PageSize = 100,
            TotalPages = 1
        };

        var handler = new FakeHttpHandler(HttpStatusCode.OK, JsonSerializer.Serialize(expected, JsonOptions));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
        var client = new GibUserListClient(httpClient);

        var result = await client.GetEInvoiceChangesAsync(new DateTime(2026, 2, 17));

        result.Data.Should().NotBeNull();
        result.Data.Changes.Should().HaveCount(1);
        result.Data.Changes[0].Identifier.Should().Be("1234567890");
        result.Data.Changes[0].ChangeType.Should().Be("added");
    }

    [Fact]
    public async Task GetEInvoiceChangesAsync_When410Gone_ShouldThrowSyncExpiredException()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.Gone, "{}");
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
        var client = new GibUserListClient(httpClient);

        var act = async () => await client.GetEInvoiceChangesAsync(new DateTime(2025, 1, 1));

        await act.Should().ThrowAsync<GibUserListSyncExpiredException>();
    }

    [Fact]
    public async Task ListEInvoiceArchivesAsync_ShouldReturnFileList()
    {
        var expected = new List<ArchiveFileResponse>
        {
            new() { FileName = "einvoice/einvoice_users_2026-02-18_030000.xml.gz", SizeBytes = 12345, CreatedAt = new DateTime(2026, 2, 18) }
        };

        var handler = new FakeHttpHandler(HttpStatusCode.OK, JsonSerializer.Serialize(expected, JsonOptions));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
        var client = new GibUserListClient(httpClient);

        var result = await client.ListEInvoiceArchivesAsync();

        result.Should().HaveCount(1);
        result[0].FileName.Should().Contain("einvoice_users_");
    }

    [Fact]
    public async Task GetLatestEInvoiceArchiveAsync_WhenNotFound_ShouldReturnNullData()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.NotFound, "");
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
        var client = new GibUserListClient(httpClient);

        var result = await client.GetLatestEInvoiceArchiveAsync();

        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task DownloadEInvoiceArchiveAsync_WhenFound_ShouldReturnStream()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "GZIP_CONTENT");
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
        var client = new GibUserListClient(httpClient);

        var result = await client.DownloadEInvoiceArchiveAsync("einvoice_users_2026-02-18.xml.gz");

        result.Should().NotBeNull();
    }

    private sealed class FakeHttpHandler(
        HttpStatusCode statusCode,
        string responseContent) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseContent, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
