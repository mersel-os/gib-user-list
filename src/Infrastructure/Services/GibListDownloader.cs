using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MERSEL.Services.GibUserList.Application.Interfaces;

namespace MERSEL.Services.GibUserList.Infrastructure.Services;

/// <summary>
/// Yapılandırılmış uç noktalardan GIB kullanıcı listesi ZIP dosyalarını indirir.
/// </summary>
public sealed class GibListDownloader(
    IHttpClientFactory httpClientFactory,
    IOptions<GibEndpointOptions> options,
    ILogger<GibListDownloader> logger) : IGibListDownloader
{
    private readonly GibEndpointOptions _options = options.Value;

    public Task DownloadPkListAsync(string outputPath, CancellationToken cancellationToken)
        => DownloadFileAsync(_options.PkListUrl, outputPath, "PK", cancellationToken);

    public Task DownloadGbListAsync(string outputPath, CancellationToken cancellationToken)
        => DownloadFileAsync(_options.GbListUrl, outputPath, "GB", cancellationToken);

    private async Task DownloadFileAsync(
        string url, string outputPath, string listType, CancellationToken cancellationToken)
    {
        logger.LogInformation("Downloading {ListType} list from {Url}...", listType, url);

        var client = httpClientFactory.CreateClient("GibDownloader");
        client.Timeout = _options.DownloadTimeout;

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
        await stream.CopyToAsync(fileStream, cancellationToken);

        var fileInfo = new FileInfo(outputPath);
        logger.LogInformation("Downloaded {ListType} list: {Size:N0} bytes", listType, fileInfo.Length);
    }
}
