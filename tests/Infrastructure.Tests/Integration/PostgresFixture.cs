using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MERSEL.Services.GibUserList.Application.Configuration;
using MERSEL.Services.GibUserList.Application.Interfaces;
using MERSEL.Services.GibUserList.Infrastructure.Data;
using MERSEL.Services.GibUserList.Infrastructure.Diagnostics;
using MERSEL.Services.GibUserList.Infrastructure.Services;
using MERSEL.Services.GibUserList.Infrastructure.Webhooks;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace MERSEL.Services.GibUserList.Infrastructure.Tests.Integration;

/// <summary>
/// GIB mükellef listesi integration testleri için paylaşılan PostgreSQL Testcontainer fixture.
/// Docker üzerinde gerçek PostgreSQL başlatır, şemayı oluşturur ve test servisleri sağlar.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("gib_user_list_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public string ConnectionString => _container.GetConnectionString();
    public GibUserListDbContext DbContext { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<GibUserListDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;

        DbContext = new GibUserListDbContext(options);

        // EF Core migration'ı uygula (artık tüm schema migration'da tanımlı)
        await DbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await DbContext.DisposeAsync();
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Test için GibXmlStreamParser oluşturur.
    /// </summary>
    public GibXmlStreamParser CreateParser()
    {
        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug));
        return new GibXmlStreamParser(loggerFactory.CreateLogger<GibXmlStreamParser>());
    }

    /// <summary>
    /// Test için GibBulkCopyWriter oluşturur.
    /// </summary>
    public GibBulkCopyWriter CreateBulkWriter(int batchSize = 10_000)
    {
        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug));
        var opts = Options.Create(new GibEndpointOptions { BatchSize = batchSize });
        return new GibBulkCopyWriter(opts, loggerFactory.CreateLogger<GibBulkCopyWriter>());
    }

    /// <summary>
    /// Test için GibUserListSyncService oluşturur (downloader dışarıdan verilir).
    /// </summary>
    public GibUserListSyncService CreateSyncService(Application.Interfaces.IGibListDownloader downloader)
    {
        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug));
        var meterFactory = new TestMeterFactory();
        var metrics = new GibUserListMetrics(meterFactory);
        var syncTimeProvider = Substitute.For<ISyncTimeProvider>();
        var endpointOptions = Options.Create(new GibEndpointOptions());
        var archiveOptions = Options.Create(new ArchiveStorageOptions());
        var archiveStorage = Substitute.For<IArchiveStorage>();
        var cacheService = Substitute.For<ICacheService>();

        var metadataService = new SyncMetadataService(
            DbContext,
            loggerFactory.CreateLogger<SyncMetadataService>());
        var transactionProcessor = new GibTransactionalSyncProcessor(
            DbContext,
            CreateParser(),
            CreateBulkWriter(),
            new GibDiffEngine(endpointOptions, metrics, new NullWebhookNotifier(), loggerFactory.CreateLogger<GibDiffEngine>()),
            new GibMaterializedViewRefresher(metrics, new NullWebhookNotifier(), loggerFactory.CreateLogger<GibMaterializedViewRefresher>()),
            new GibCacheInvalidationService(cacheService, loggerFactory.CreateLogger<GibCacheInvalidationService>()),
            metadataService,
            metrics,
            endpointOptions,
            loggerFactory.CreateLogger<GibTransactionalSyncProcessor>());
        var archiveService = new GibArchiveService(
            DbContext,
            archiveStorage,
            archiveOptions,
            loggerFactory.CreateLogger<GibArchiveService>());

        return new GibUserListSyncService(
            DbContext,
            downloader,
            transactionProcessor,
            archiveService,
            metadataService,
            metrics,
            syncTimeProvider,
            new NullWebhookNotifier(),
            loggerFactory.CreateLogger<GibUserListSyncService>());
    }
}

[CollectionDefinition("PostgreSQL")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>;

/// <summary>
/// Testler için basit IMeterFactory implementasyonu.
/// Metrikler kaydedilir ancak dışarıya aktarılmaz.
/// </summary>
internal sealed class TestMeterFactory : IMeterFactory
{
    private readonly List<Meter> _meters = [];

    public Meter Create(MeterOptions options)
    {
        var meter = new Meter(options.Name, options.Version);
        _meters.Add(meter);
        return meter;
    }

    public void Dispose()
    {
        foreach (var meter in _meters)
            meter.Dispose();
        _meters.Clear();
    }
}
