using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MERSEL.Services.GibUserList.Client.Interfaces;
using MERSEL.Services.GibUserList.Client.Models;

namespace MERSEL.Services.GibUserList.Client;

/// <summary>
/// GIB Mükellef Sicil API'si için HTTP istemci uygulaması.
/// BaseAddress ve başlıklar DI (AddGibUserListClient) üzerinden yapılandırılır.
/// Tüm sorgu yanıtlarında X-Last-Sync-At header'ı okunarak <see cref="GibUserApiResponse{T}.LastSyncAt"/> olarak döner.
/// </summary>
public sealed class GibUserListClient(HttpClient httpClient) : IGibUserListClient
{
    private const string LastSyncHeader = "X-Last-Sync-At";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    // ── e-Fatura Sorgu ──

    public async Task<GibUserApiResponse<GibUserResponse?>> GetEInvoiceGibUserAsync(
        string identifier, DateTime? firstCreationTime = null, CancellationToken ct = default)
    {
        var path = BuildIdentifierPath("/api/v1/einvoice", identifier, firstCreationTime);
        return await GetGibUserAsync(path, ct);
    }

    public async Task<GibUserApiResponse<GibUserSearchResponse>> SearchEInvoiceGibUsersAsync(
        string search, int page = 1, int pageSize = 20, CancellationToken ct = default)
        => await SearchGibUsersAsync("/api/v1/einvoice", search, page, pageSize, ct);

    public async Task<GibUserApiResponse<GibUserBatchResponse>> BatchGetEInvoiceGibUsersAsync(
        IEnumerable<string> identifiers, CancellationToken ct = default)
        => await BatchGetAsync("/api/v1/einvoice/batch", identifiers, ct);

    // ── e-İrsaliye Sorgu ──

    public async Task<GibUserApiResponse<GibUserResponse?>> GetEDespatchGibUserAsync(
        string identifier, DateTime? firstCreationTime = null, CancellationToken ct = default)
    {
        var path = BuildIdentifierPath("/api/v1/edespatch", identifier, firstCreationTime);
        return await GetGibUserAsync(path, ct);
    }

    public async Task<GibUserApiResponse<GibUserSearchResponse>> SearchEDespatchGibUsersAsync(
        string search, int page = 1, int pageSize = 20, CancellationToken ct = default)
        => await SearchGibUsersAsync("/api/v1/edespatch", search, page, pageSize, ct);

    public async Task<GibUserApiResponse<GibUserBatchResponse>> BatchGetEDespatchGibUsersAsync(
        IEnumerable<string> identifiers, CancellationToken ct = default)
        => await BatchGetAsync("/api/v1/edespatch/batch", identifiers, ct);

    // ── Delta (Changes) ──

    public async Task<GibUserApiResponse<GibUserChangesResponse>> GetEInvoiceChangesAsync(
        DateTime since, int page = 1, int pageSize = 100, CancellationToken ct = default)
        => await GetChangesAsync("/api/v1/einvoice/changes", since, page, pageSize, ct);

    public async Task<GibUserApiResponse<GibUserChangesResponse>> GetEDespatchChangesAsync(
        DateTime since, int page = 1, int pageSize = 100, CancellationToken ct = default)
        => await GetChangesAsync("/api/v1/edespatch/changes", since, page, pageSize, ct);

    // ── Arşiv (Tam Liste) ──

    public async Task<IReadOnlyList<ArchiveFileResponse>> ListEInvoiceArchivesAsync(CancellationToken ct = default)
        => await ListArchivesAsync("/api/v1/einvoice/archives", ct);

    public async Task<GibUserApiResponse<Stream?>> GetLatestEInvoiceArchiveAsync(CancellationToken ct = default)
        => await GetLatestArchiveAsync("/api/v1/einvoice/archives/latest", ct);

    public async Task<Stream?> DownloadEInvoiceArchiveAsync(string fileName, CancellationToken ct = default)
        => await DownloadArchiveAsync($"/api/v1/einvoice/archives/{Uri.EscapeDataString(fileName)}", ct);

    public async Task<IReadOnlyList<ArchiveFileResponse>> ListEDespatchArchivesAsync(CancellationToken ct = default)
        => await ListArchivesAsync("/api/v1/edespatch/archives", ct);

    public async Task<GibUserApiResponse<Stream?>> GetLatestEDespatchArchiveAsync(CancellationToken ct = default)
        => await GetLatestArchiveAsync("/api/v1/edespatch/archives/latest", ct);

    public async Task<Stream?> DownloadEDespatchArchiveAsync(string fileName, CancellationToken ct = default)
        => await DownloadArchiveAsync($"/api/v1/edespatch/archives/{Uri.EscapeDataString(fileName)}", ct);

    // ── Durum ──

    public async Task<SyncStatusResponse> GetSyncStatusAsync(CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync("/api/v1/status", ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<SyncStatusResponse>(JsonOptions, ct)
            ?? throw new Exceptions.GibUserListDeserializationException("Senkronizasyon durumu yanıtı deserialize edilemedi.");
    }

    // ── Private Helpers ──

    private static string BuildIdentifierPath(string basePath, string identifier, DateTime? firstCreationTime)
    {
        var path = $"{basePath}/{Uri.EscapeDataString(identifier)}";
        if (firstCreationTime.HasValue)
            path += $"?firstCreationTime={firstCreationTime.Value:O}";
        return path;
    }

    private async Task<GibUserApiResponse<GibUserResponse?>> GetGibUserAsync(string path, CancellationToken ct)
    {
        var response = await httpClient.GetAsync(path, ct);
        var lastSync = ParseLastSyncHeader(response);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return new GibUserApiResponse<GibUserResponse?> { Data = null, LastSyncAt = lastSync };

        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<GibUserResponse>(JsonOptions, ct);
        return new GibUserApiResponse<GibUserResponse?> { Data = data, LastSyncAt = lastSync };
    }

    private async Task<GibUserApiResponse<GibUserSearchResponse>> SearchGibUsersAsync(
        string basePath, string search, int page, int pageSize, CancellationToken ct)
    {
        var url = $"{basePath}?search={Uri.EscapeDataString(search)}&page={page}&pageSize={pageSize}";
        var response = await httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var lastSync = ParseLastSyncHeader(response);
        var data = await response.Content.ReadFromJsonAsync<GibUserSearchResponse>(JsonOptions, ct)
            ?? throw new Exceptions.GibUserListDeserializationException("Arama sonucu yanıtı deserialize edilemedi.");

        return new GibUserApiResponse<GibUserSearchResponse> { Data = data, LastSyncAt = lastSync };
    }

    private async Task<GibUserApiResponse<GibUserBatchResponse>> BatchGetAsync(
        string path, IEnumerable<string> identifiers, CancellationToken ct)
    {
        var body = new { Identifiers = identifiers.ToList() };
        var response = await httpClient.PostAsJsonAsync(path, body, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var lastSync = ParseLastSyncHeader(response);
        var data = await response.Content.ReadFromJsonAsync<GibUserBatchResponse>(JsonOptions, ct)
            ?? throw new Exceptions.GibUserListDeserializationException("Toplu sorgu yanıtı deserialize edilemedi.");

        return new GibUserApiResponse<GibUserBatchResponse> { Data = data, LastSyncAt = lastSync };
    }

    private async Task<GibUserApiResponse<GibUserChangesResponse>> GetChangesAsync(
        string basePath, DateTime since, int page, int pageSize, CancellationToken ct)
    {
        var url = $"{basePath}?since={since:O}&page={page}&pageSize={pageSize}";
        var response = await httpClient.GetAsync(url, ct);
        var lastSync = ParseLastSyncHeader(response);

        if (response.StatusCode == HttpStatusCode.Gone)
            throw new Exceptions.GibUserListSyncExpiredException(
                "Delta süresi dolmuş. Full re-sync gereklidir. /archives/latest ile tam listeyi indirin.");

        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<GibUserChangesResponse>(JsonOptions, ct)
            ?? throw new Exceptions.GibUserListDeserializationException("Değişiklik sorgusu yanıtı deserialize edilemedi.");

        return new GibUserApiResponse<GibUserChangesResponse> { Data = data, LastSyncAt = lastSync };
    }

    private async Task<IReadOnlyList<ArchiveFileResponse>> ListArchivesAsync(string path, CancellationToken ct)
    {
        var response = await httpClient.GetAsync(path, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<ArchiveFileResponse>>(JsonOptions, ct)
            ?? throw new Exceptions.GibUserListDeserializationException("Arşiv listesi yanıtı deserialize edilemedi.");
    }

    private async Task<GibUserApiResponse<Stream?>> GetLatestArchiveAsync(string path, CancellationToken ct)
    {
        var response = await httpClient.GetAsync(path, ct);
        var lastSync = ParseLastSyncHeader(response);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return new GibUserApiResponse<Stream?> { Data = null, LastSyncAt = lastSync };

        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(ct);
        return new GibUserApiResponse<Stream?> { Data = stream, LastSyncAt = lastSync };
    }

    private async Task<Stream?> DownloadArchiveAsync(string path, CancellationToken ct)
    {
        var response = await httpClient.GetAsync(path, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(ct);
    }

    private static DateTime? ParseLastSyncHeader(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues(LastSyncHeader, out var values))
        {
            var headerValue = values.FirstOrDefault();
            if (headerValue is not null &&
                DateTime.TryParse(headerValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                return parsed;
        }
        return null;
    }
}
