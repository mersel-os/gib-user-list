using MERSEL.Services.GibUserList.Application.Interfaces;

namespace MERSEL.Services.GibUserList.Infrastructure.Tests.Integration;

/// <summary>
/// GİB test ortamından gerçek mükellef listesi ZIP dosyalarını indiren yardımcı.
/// Test ortamı URL'leri: merkeztest.gib.gov.tr
/// </summary>
public sealed class GibTestDownloader : IGibListDownloader
{
    private const string GbListUrl = "https://merkeztest.gib.gov.tr/EFaturaMerkez/newUserGbListxml.zip";
    private const string PkListUrl = "https://merkeztest.gib.gov.tr/EFaturaMerkez/newUserPkListxml.zip";

    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(5)
    };

    public async Task DownloadPkListAsync(string outputPath, CancellationToken cancellationToken)
    {
        await DownloadAsync(PkListUrl, outputPath, cancellationToken);
    }

    public async Task DownloadGbListAsync(string outputPath, CancellationToken cancellationToken)
    {
        await DownloadAsync(GbListUrl, outputPath, cancellationToken);
    }

    private async Task DownloadAsync(string url, string outputPath, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
        await stream.CopyToAsync(fileStream, ct);
    }
}
