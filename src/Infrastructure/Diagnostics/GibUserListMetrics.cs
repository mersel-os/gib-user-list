using System.Diagnostics;
using System.Diagnostics.Metrics;
using MERSEL.Services.GibUserList.Application.Interfaces;
using MERSEL.Services.GibUserList.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MERSEL.Services.GibUserList.Infrastructure.Diagnostics;

/// <summary>
/// GibUserList servisinin tüm özel metriklerini tanımlar.
/// System.Diagnostics.Metrics altyapısını kullanır — OpenTelemetry, Prometheus
/// veya herhangi bir .NET metrikleri ile uyumludur.
///
/// Sync Job metrikleri (kullanıcı sayıları, son sync zamanı/süresi) ObservableGauge olarak
/// API process'inden expose edilir. Worker ayrı bir process olduğu için doğrudan scrape edilemez;
/// bu yüzden SyncMetadata tablosundan okunarak Prometheus'a sunulur.
/// </summary>
public sealed class GibUserListMetrics : IAppMetrics
{
    /// <summary>
    /// Meter adı. OpenTelemetry yapılandırmasında <c>AddMeter(GibUserListMetrics.MeterName)</c>
    /// ile bu metrikler dışarıya aktarılır.
    /// </summary>
    public const string MeterName = "MERSEL.Services.GibUserList";

    /// <summary>
    /// Dağıtık izleme (distributed tracing) için ActivitySource.
    /// Her sync/sorgu işlemi ayrı bir Activity (span) olarak kaydedilir.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(MeterName);

    // -- API Sorguları --
    private readonly Counter<long> _queriesTotal;
    private readonly Counter<long> _queryErrors;
    private readonly Histogram<double> _queryDuration;

    // -- Sync Job --
    private readonly Counter<long> _syncTotal;
    private readonly Histogram<double> _syncDuration;
    private readonly Counter<long> _syncUsersProcessed;
    private readonly UpDownCounter<int> _syncActive;

    // -- Cache --
    private readonly Counter<long> _cacheHits;
    private readonly Counter<long> _cacheMisses;

    // -- Auth --
    private readonly Counter<long> _authRequests;

    // -- MV Refresh --
    private readonly Histogram<double> _mvRefreshDuration;
    private readonly Counter<long> _mvRefreshErrors;

    // -- Diff / Changelog --
    private readonly Counter<long> _changesTotal;
    private readonly Counter<long> _removalSkipped;

    // -- Observable Gauge cache (SyncMetadataGaugeRefreshService tarafından periyodik olarak güncellenir) --
    internal int EInvoiceUserCount;
    internal int EDespatchUserCount;
    internal double LastSyncDurationSeconds;
    internal double LastSyncAtUnixSeconds;

    public GibUserListMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _queriesTotal = meter.CreateCounter<long>(
            "gibuserlist.queries.total",
            description: "Toplam mükellef sorgu sayısı");

        _queryErrors = meter.CreateCounter<long>(
            "gibuserlist.queries.errors",
            description: "Başarısız sorgu sayısı");

        _queryDuration = meter.CreateHistogram<double>(
            "gibuserlist.queries.duration",
            unit: "ms",
            description: "Mükellef sorgu süresi (milisaniye)");

        _syncTotal = meter.CreateCounter<long>(
            "gibuserlist.sync.total",
            description: "Toplam senkronizasyon işlemi sayısı");

        _syncDuration = meter.CreateHistogram<double>(
            "gibuserlist.sync.duration",
            unit: "s",
            description: "Senkronizasyon süresi (saniye)");

        _syncUsersProcessed = meter.CreateCounter<long>(
            "gibuserlist.sync.users_processed",
            description: "Senkronizasyonda işlenen kullanıcı sayısı");

        _syncActive = meter.CreateUpDownCounter<int>(
            "gibuserlist.sync.active",
            description: "Devam eden aktif senkronizasyon sayısı");

        _cacheHits = meter.CreateCounter<long>(
            "gibuserlist.cache.hits",
            description: "Önbellek isabet sayısı");

        _cacheMisses = meter.CreateCounter<long>(
            "gibuserlist.cache.misses",
            description: "Önbellek ıskalama sayısı");

        _authRequests = meter.CreateCounter<long>(
            "gibuserlist.auth.requests",
            description: "Kimlik doğrulama isteği sayısı");

        _mvRefreshDuration = meter.CreateHistogram<double>(
            "gibuserlist.mv_refresh.duration",
            unit: "s",
            description: "Materialized view yenileme süresi (saniye)");

        _mvRefreshErrors = meter.CreateCounter<long>(
            "gibuserlist.mv_refresh.errors",
            description: "Materialized view yenileme hata sayısı");

        _changesTotal = meter.CreateCounter<long>(
            "gibuserlist.sync.changes_total",
            description: "Senkronizasyonda tespit edilen değişiklik sayısı (change_type etiketli)");

        _removalSkipped = meter.CreateCounter<long>(
            "gibuserlist.sync.removal_skipped",
            description: "Safe removal guard nedeniyle atlanan silme işlemi sayısı");

        meter.CreateObservableGauge(
            "gibuserlist.users.einvoice_count",
            () => Volatile.Read(ref EInvoiceUserCount),
            description: "Aktif e-Fatura mükellef sayısı");

        meter.CreateObservableGauge(
            "gibuserlist.users.edespatch_count",
            () => Volatile.Read(ref EDespatchUserCount),
            description: "Aktif e-İrsaliye mükellef sayısı");

        meter.CreateObservableGauge(
            "gibuserlist.sync.last_duration_seconds",
            () => Volatile.Read(ref LastSyncDurationSeconds),
            unit: "s",
            description: "Son senkronizasyon süresi (saniye)");

        meter.CreateObservableGauge(
            "gibuserlist.sync.last_sync_at_unix",
            () => Volatile.Read(ref LastSyncAtUnixSeconds),
            unit: "s",
            description: "Son senkronizasyon zamanı (Unix epoch saniye)");

        var processStartTime = DateTime.Now;
        meter.CreateObservableGauge(
            "gibuserlist.process.uptime_seconds",
            () => (DateTime.Now - processStartTime).TotalSeconds,
            unit: "s",
            description: "API process çalışma süresi (saniye)");
    }

    // ── API Sorgu Metrikleri ────────────────────────────────────────

    /// <summary>
    /// Başarılı bir sorguyu kaydeder.
    /// </summary>
    /// <param name="type">Sorgu tipi: "identifier" veya "search"</param>
    /// <param name="documentType">Belge türü: "einvoice" veya "edespatch"</param>
    /// <param name="durationMs">İşlem süresi (milisaniye)</param>
    public void RecordQuery(string type, string documentType, double durationMs)
    {
        var tags = new TagList
        {
            { "type", type },
            { "document_type", documentType }
        };
        _queriesTotal.Add(1, tags);
        _queryDuration.Record(durationMs, tags);
    }

    /// <summary>
    /// Başarısız bir sorguyu kaydeder.
    /// </summary>
    public void RecordQueryError(string type, string documentType)
    {
        var tags = new TagList
        {
            { "type", type },
            { "document_type", documentType }
        };
        _queryErrors.Add(1, tags);
    }

    // ── Sync Metrikleri ─────────────────────────────────────────────

    /// <summary>
    /// Senkronizasyon işlemini kaydeder.
    /// </summary>
    /// <param name="status">"success" veya "failure"</param>
    /// <param name="durationSeconds">İşlem süresi (saniye)</param>
    public void RecordSync(string status, double durationSeconds)
    {
        _syncTotal.Add(1, new KeyValuePair<string, object?>("status", status));
        _syncDuration.Record(durationSeconds);
    }

    /// <summary>
    /// İşlenen kullanıcı sayısını kaydeder.
    /// </summary>
    /// <param name="listType">"pk" veya "gb"</param>
    /// <param name="count">İşlenen kullanıcı sayısı</param>
    public void RecordUsersProcessed(string listType, long count)
    {
        _syncUsersProcessed.Add(count, new KeyValuePair<string, object?>("list_type", listType));
    }

    /// <summary>Aktif senkronizasyon sayacını artırır.</summary>
    public void IncrementSyncActive() => _syncActive.Add(1);

    /// <summary>Aktif senkronizasyon sayacını azaltır.</summary>
    public void DecrementSyncActive() => _syncActive.Add(-1);

    // ── Cache Metrikleri ────────────────────────────────────────────

    /// <summary>Önbellek isabetini kaydeder.</summary>
    public void RecordCacheHit() => _cacheHits.Add(1);

    /// <summary>Önbellek ıskalamasını kaydeder.</summary>
    public void RecordCacheMiss() => _cacheMisses.Add(1);

    // ── Auth Metrikleri ─────────────────────────────────────────────

    /// <summary>
    /// Kimlik doğrulama girişimini kaydeder.
    /// </summary>
    /// <param name="clientName">İstemci adı ("anonymous" veya yapılandırmadaki Name/AccessKey)</param>
    /// <param name="status">"success", "failure" veya "disabled"</param>
    /// <param name="reason">Başarısızlık nedeni (opsiyonel, hata durumlarında)</param>
    public void RecordAuthRequest(string clientName, string status, string? reason = null)
    {
        var tags = new TagList
        {
            { "client", clientName },
            { "status", status }
        };
        if (reason is not null)
            tags.Add("reason", reason);

        _authRequests.Add(1, tags);
    }

    // ── Materialized View Refresh Metrikleri ────────────────────────

    // ── Diff / Changelog Metrikleri ─────────────────────────────────

    /// <summary>Tespit edilen değişiklik sayısını kaydeder.</summary>
    /// <param name="changeType">"added", "modified" veya "removed"</param>
    /// <param name="count">Değişiklik sayısı</param>
    public void RecordChanges(string changeType, long count)
    {
        _changesTotal.Add(count, new KeyValuePair<string, object?>("change_type", changeType));
    }

    /// <summary>Safe removal guard tetiklenmesini kaydeder.</summary>
    public void RecordRemovalSkipped() => _removalSkipped.Add(1);

    // ── Materialized View Refresh Metrikleri ────────────────────────

    /// <summary>Materialized view yenileme süresini kaydeder.</summary>
    public void RecordMvRefreshDuration(double durationSeconds)
        => _mvRefreshDuration.Record(durationSeconds);

    /// <summary>Materialized view yenileme hatasını kaydeder.</summary>
    public void RecordMvRefreshError() => _mvRefreshErrors.Add(1);
}
