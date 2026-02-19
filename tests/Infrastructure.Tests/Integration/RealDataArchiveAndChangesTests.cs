using System.Data;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MERSEL.Services.GibUserList.Application.Interfaces;
using MERSEL.Services.GibUserList.Domain.Entities;
using MERSEL.Services.GibUserList.Infrastructure.Data;
using MERSEL.Services.GibUserList.Infrastructure.Diagnostics;
using MERSEL.Services.GibUserList.Infrastructure.Services;
using MERSEL.Services.GibUserList.Infrastructure.Webhooks;
using Npgsql;
using NSubstitute;

namespace MERSEL.Services.GibUserList.Infrastructure.Tests.Integration;

/// <summary>
/// Gerçek GIB test ortamından (merkeztest.gib.gov.tr) indirilen verilerle:
///   1. Full sync + arşiv üretimi doğrulama
///   2. Veri manipülasyonuyla diff/changes testi
///   3. Arşiv XML.GZ içerik doğrulama
///   4. Tüketici protokolü: bootstrap → delta → re-bootstrap
///
/// Bu testler GIB test ortamına HTTP erişimi ve Docker gerektirir.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public class RealDataArchiveAndChangesTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private InMemoryArchiveStorage _archiveStorage = null!;
    private GibUserListSyncService _syncService = null!;
    private static readonly CultureInfo TurkishCulture = new("tr-TR", false);

    public RealDataArchiveAndChangesTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _archiveStorage = new InMemoryArchiveStorage();
        _syncService = CreateSyncServiceWithArchiveStorage(new GibTestDownloader(), _archiveStorage);

        // Tabloları temizle (önceki testlerden kalan veri)
        var connection = await GetOpenConnectionAsync();
        await ExecuteSql(connection,
            "DELETE FROM gib_user_changelog",
            "DELETE FROM e_invoice_gib_users",
            "DELETE FROM e_despatch_gib_users");
        await ExecuteSql(connection,
            "REFRESH MATERIALIZED VIEW CONCURRENTLY mv_e_invoice_gib_users",
            "REFRESH MATERIALIZED VIEW CONCURRENTLY mv_e_despatch_gib_users");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ──────────────────────────────────────────────────────────────
    // 1. Gerçek GIB verisiyle tam sync + arşiv üretimi
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task FullSync_ShouldGenerateEInvoiceAndEDespatchArchives()
    {
        await _syncService.SyncGibUserListsAsync(CancellationToken.None);

        // Arşiv dosyaları üretilmiş olmalı
        var allFiles = await _archiveStorage.ListAsync(ct: CancellationToken.None);
        allFiles.Should().HaveCount(2, "Her belge türü için bir arşiv dosyası üretilmeli");

        var invoiceFiles = await _archiveStorage.ListAsync("einvoice/", CancellationToken.None);
        invoiceFiles.Should().ContainSingle();
        invoiceFiles[0].SizeBytes.Should().BeGreaterThan(0, "Arşiv boş olmamalı");

        var despatchFiles = await _archiveStorage.ListAsync("edespatch/", CancellationToken.None);
        despatchFiles.Should().ContainSingle();
        despatchFiles[0].SizeBytes.Should().BeGreaterThan(0);
    }

    // ──────────────────────────────────────────────────────────────
    // 2. Arşiv XML.GZ içeriği: geçerli XML, doğru yapı, veri tutarlılığı
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Archive_ShouldContainValidXmlWithAllUsers()
    {
        await _syncService.SyncGibUserListsAsync(CancellationToken.None);

        // DB'deki gerçek e-Fatura kullanıcı sayısı
        var dbInvoiceCount = await _fixture.DbContext.EInvoiceGibUsers.AsNoTracking().CountAsync();

        var invoiceFiles = await _archiveStorage.ListAsync("einvoice/", CancellationToken.None);
        var stream = await _archiveStorage.GetAsync(invoiceFiles[0].FileName, CancellationToken.None);
        stream.Should().NotBeNull();

        var xml = await DecompressGzipToString(stream!);

        // XML geçerli olmalı
        var doc = new XmlDocument();
        doc.LoadXml(xml);

        // Root element doğru olmalı
        doc.DocumentElement!.Name.Should().Be("GibUserList");
        doc.DocumentElement.GetAttribute("documentType").Should().Be("Invoice");
        doc.DocumentElement.GetAttribute("count").Should().Be(dbInvoiceCount.ToString());

        // User elementleri olmalı
        var userNodes = doc.DocumentElement.GetElementsByTagName("User");
        userNodes.Count.Should().Be(dbInvoiceCount, "Arşivdeki kullanıcı sayısı DB ile eşleşmeli");

        // İlk kullanıcının yapısını doğrula
        var firstUser = userNodes[0]!;
        firstUser.SelectSingleNode("Identifier")!.InnerText.Should().MatchRegex(@"^\d{10,11}$");
        firstUser.SelectSingleNode("Title")!.InnerText.Should().NotBeNullOrWhiteSpace();
    }

    // ──────────────────────────────────────────────────────────────
    // 3. İlk sync'te tüm kullanıcılar changelog'da "added" olmalı
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task FirstSync_ShouldCreateAddedChangelogForAllUsers()
    {
        await _syncService.SyncGibUserListsAsync(CancellationToken.None);

        var invoiceCount = await _fixture.DbContext.EInvoiceGibUsers.AsNoTracking().CountAsync();
        var addedCount = await _fixture.DbContext.GibUserChangeLogs.AsNoTracking()
            .CountAsync(c => c.DocumentType == GibDocumentType.EInvoice && c.ChangeType == GibChangeType.Added);

        addedCount.Should().Be(invoiceCount, "Her kullanıcı için 'added' changelog kaydı olmalı");
    }

    // ──────────────────────────────────────────────────────────────
    // 4. Manipülasyon: Gerçek veriden kullanıcı silme → diff engine removed tespit etmeli
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task WhenUsersRemovedFromList_DiffEngine_ShouldDetectAndLogRemoved()
    {
        // 1) İlk sync: gerçek GIB verisi
        var realDownloader = new GibTestDownloader();
        _syncService = CreateSyncServiceWithArchiveStorage(realDownloader, _archiveStorage);
        await _syncService.SyncGibUserListsAsync(CancellationToken.None);

        var invoiceCountBefore = await _fixture.DbContext.EInvoiceGibUsers.AsNoTracking().CountAsync();
        invoiceCountBefore.Should().BeGreaterThan(10, "Gerçek veride yeterli e-Fatura kullanıcısı olmalı");

        // 2) İkinci sync: gerçek veriden rastgele bazı kullanıcıları çıkararak
        var removedIdentifiers = await _fixture.DbContext.EInvoiceGibUsers.AsNoTracking()
            .OrderBy(u => u.Identifier).Take(3).Select(u => u.Identifier).ToListAsync();

        var manipulatedDownloader = new ManipulatedGibDownloader(realDownloader, removedIdentifiers, "Invoice");
        _syncService = CreateSyncServiceWithGuard(manipulatedDownloader, maxRemovalPercent: 50);
        await _syncService.SyncGibUserListsAsync(CancellationToken.None);

        // 3) Doğrula: silinen kullanıcılar changelog'da "removed" olmalı
        var removedLogs = await _fixture.DbContext.GibUserChangeLogs.AsNoTracking()
            .Where(c => c.DocumentType == GibDocumentType.EInvoice && c.ChangeType == GibChangeType.Removed)
            .Select(c => c.Identifier)
            .ToListAsync();

        foreach (var id in removedIdentifiers)
            removedLogs.Should().Contain(id, $"Silinen {id} changelog'da 'removed' olarak görünmeli");

        // 4) Silinen kullanıcılar ana tablodan da kaldırılmış olmalı (hard-delete)
        var invoiceCountAfter = await _fixture.DbContext.EInvoiceGibUsers.AsNoTracking().CountAsync();
        invoiceCountAfter.Should().Be(invoiceCountBefore - removedIdentifiers.Count);
    }

    // ──────────────────────────────────────────────────────────────
    // 5. Manipülasyon: Title değişikliği → modified tespit
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task WhenUserTitleChanges_DiffEngine_ShouldDetectModified()
    {
        // 1) İlk sync
        var realDownloader = new GibTestDownloader();
        _syncService = CreateSyncServiceWithArchiveStorage(realDownloader, _archiveStorage);
        await _syncService.SyncGibUserListsAsync(CancellationToken.None);

        var targetUser = await _fixture.DbContext.EInvoiceGibUsers.AsNoTracking()
            .OrderBy(u => u.Identifier).FirstAsync();

        // 2) İkinci sync: hedef kullanıcının title'ını değiştir
        var titleChangeMap = new Dictionary<string, string>
        {
            [targetUser.Identifier] = "DEGISTIRILMIS UNVAN TEST A.S."
        };
        var manipulatedDownloader = new ManipulatedGibDownloader(realDownloader, titleChangeMap);
        _syncService = CreateSyncServiceWithArchiveStorage(manipulatedDownloader, _archiveStorage);
        await _syncService.SyncGibUserListsAsync(CancellationToken.None);

        // 3) Doğrula: changelog'da "modified" kaydı olmalı
        var modifiedLogs = await _fixture.DbContext.GibUserChangeLogs.AsNoTracking()
            .Where(c => c.DocumentType == GibDocumentType.EInvoice
                && c.ChangeType == GibChangeType.Modified
                && c.Identifier == targetUser.Identifier)
            .ToListAsync();

        modifiedLogs.Should().NotBeEmpty("Title değişikliği modified olarak tespit edilmeli");

        // 4) Ana tabloda yeni title olmalı
        var updatedUser = await _fixture.DbContext.EInvoiceGibUsers.AsNoTracking()
            .FirstAsync(u => u.Identifier == targetUser.Identifier);
        updatedUser.Title.Should().Contain("DEGISTIRILMIS", "Ana tabloda yeni title olmalı");
    }

    // ──────────────────────────────────────────────────────────────
    // 6. İkinci sync arşivi — güncel veriyi yansıtmalı
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SecondSync_ShouldProduceUpdatedArchive()
    {
        await _syncService.SyncGibUserListsAsync(CancellationToken.None);
        var firstArchiveFiles = await _archiveStorage.ListAsync("einvoice/", CancellationToken.None);

        // İkinci sync (aynı veri — size değişmemeli)
        await _syncService.SyncGibUserListsAsync(CancellationToken.None);
        var secondArchiveFiles = await _archiveStorage.ListAsync("einvoice/", CancellationToken.None);

        secondArchiveFiles.Should().HaveCount(2, "İki sync → iki arşiv dosyası");
    }

    // ──────────────────────────────────────────────────────────────
    // 7. Arşivde belge türü ayrımı: Invoice arşivinde DespatchAdvice kullanıcısı olmamalı
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Archive_ShouldStrictlySeparateDocumentTypes()
    {
        await _syncService.SyncGibUserListsAsync(CancellationToken.None);

        // e-İrsaliye'de olup e-Fatura'da olmayan bir kullanıcı bul
        var despatchOnlyIds = await _fixture.DbContext.EDespatchGibUsers.AsNoTracking()
            .Where(d => !_fixture.DbContext.EInvoiceGibUsers.Any(i => i.Identifier == d.Identifier))
            .Take(5)
            .Select(d => d.Identifier)
            .ToListAsync();

        if (despatchOnlyIds.Count > 0)
        {
            var invoiceArchives = await _archiveStorage.ListAsync("einvoice/", CancellationToken.None);
            var invoiceStream = await _archiveStorage.GetAsync(invoiceArchives[0].FileName, CancellationToken.None);
            var invoiceXml = await DecompressGzipToString(invoiceStream!);
            foreach (var id in despatchOnlyIds)
                invoiceXml.Should().NotContain($"<Identifier>{id}</Identifier>",
                    $"Sadece e-İrsaliye mükellefi {id} Invoice arşivinde olmamalı");
        }
    }

    // ──────────────────────────────────────────────────────────────
    // 8. Tüketici protokolü E2E: bootstrap → delta takibi
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConsumerProtocol_Bootstrap_Then_DeltaTracking()
    {
        // Adım 1: İlk sync → bootstrap verisi hazır
        await _syncService.SyncGibUserListsAsync(CancellationToken.None);
        var syncTime = DateTime.Now;

        // Adım 2: Tüketici "latest" arşivi indirir → bu, tam kullanıcı listesi
        var latestArchiveFiles = await _archiveStorage.ListAsync("einvoice/", CancellationToken.None);
        latestArchiveFiles.Should().NotBeEmpty("Bootstrap arşivi hazır olmalı");

        var archiveStream = await _archiveStorage.GetAsync(latestArchiveFiles[0].FileName, CancellationToken.None);
        archiveStream.Should().NotBeNull("Arşiv indirilebilir olmalı");

        var archiveXml = await DecompressGzipToString(archiveStream!);
        var archiveDoc = new XmlDocument();
        archiveDoc.LoadXml(archiveXml);
        var archiveUserCount = archiveDoc.DocumentElement!.GetElementsByTagName("User").Count;
        archiveUserCount.Should().BeGreaterThan(0, "Arşivde kullanıcılar olmalı");

        // Adım 3: Changelog'dan "since" ile değişiklikleri sorgula
        var addedCount = await _fixture.DbContext.GibUserChangeLogs.AsNoTracking()
            .CountAsync(c => c.DocumentType == GibDocumentType.EInvoice
                && c.ChangeType == GibChangeType.Added
                && c.ChangedAt <= syncTime);
        addedCount.Should().Be(archiveUserCount,
            "Bootstrap'taki kullanıcı sayısı = changelog'daki added sayısı");
    }

    // ──────────────────────────────────────────────────────────────
    // Yardımcılar
    // ──────────────────────────────────────────────────────────────

    private GibUserListSyncService CreateSyncServiceWithArchiveStorage(
        IGibListDownloader downloader, IArchiveStorage storage)
    {
        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug));
        var meterFactory = new TestMeterFactory();
        var metrics = new GibUserListMetrics(meterFactory);
        var syncTimeProvider = Substitute.For<ISyncTimeProvider>();

        var cacheService = Substitute.For<ICacheService>();
        var endpointOptions = Options.Create(new GibEndpointOptions { MaxAllowedRemovalPercent = 50, ChangeRetentionDays = 30 });
        var archiveOptions = Options.Create(new ArchiveStorageOptions());
        var metadataService = new SyncMetadataService(
            _fixture.DbContext,
            loggerFactory.CreateLogger<SyncMetadataService>());
        var transactionProcessor = new GibTransactionalSyncProcessor(
            _fixture.DbContext,
            _fixture.CreateParser(),
            _fixture.CreateBulkWriter(),
            new GibDiffEngine(endpointOptions, metrics, new NullWebhookNotifier(), loggerFactory.CreateLogger<GibDiffEngine>()),
            new GibMaterializedViewRefresher(metrics, new NullWebhookNotifier(), loggerFactory.CreateLogger<GibMaterializedViewRefresher>()),
            new GibCacheInvalidationService(cacheService, loggerFactory.CreateLogger<GibCacheInvalidationService>()),
            metadataService,
            metrics,
            endpointOptions,
            loggerFactory.CreateLogger<GibTransactionalSyncProcessor>());
        var archiveService = new GibArchiveService(
            _fixture.DbContext,
            storage,
            archiveOptions,
            loggerFactory.CreateLogger<GibArchiveService>());

        return new GibUserListSyncService(
            _fixture.DbContext, downloader, transactionProcessor, archiveService, metadataService,
            metrics, syncTimeProvider, new NullWebhookNotifier(),
            loggerFactory.CreateLogger<GibUserListSyncService>());
    }

    private GibUserListSyncService CreateSyncServiceWithGuard(
        IGibListDownloader downloader, double maxRemovalPercent)
    {
        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug));
        var meterFactory = new TestMeterFactory();
        var metrics = new GibUserListMetrics(meterFactory);
        var syncTimeProvider = Substitute.For<ISyncTimeProvider>();
        var cacheService2 = Substitute.For<ICacheService>();
        var endpointOptions = Options.Create(new GibEndpointOptions { MaxAllowedRemovalPercent = maxRemovalPercent, ChangeRetentionDays = 30 });
        var archiveOptions = Options.Create(new ArchiveStorageOptions());
        var metadataService = new SyncMetadataService(
            _fixture.DbContext,
            loggerFactory.CreateLogger<SyncMetadataService>());
        var transactionProcessor = new GibTransactionalSyncProcessor(
            _fixture.DbContext,
            _fixture.CreateParser(),
            _fixture.CreateBulkWriter(),
            new GibDiffEngine(endpointOptions, metrics, new NullWebhookNotifier(), loggerFactory.CreateLogger<GibDiffEngine>()),
            new GibMaterializedViewRefresher(metrics, new NullWebhookNotifier(), loggerFactory.CreateLogger<GibMaterializedViewRefresher>()),
            new GibCacheInvalidationService(cacheService2, loggerFactory.CreateLogger<GibCacheInvalidationService>()),
            metadataService,
            metrics,
            endpointOptions,
            loggerFactory.CreateLogger<GibTransactionalSyncProcessor>());
        var archiveService = new GibArchiveService(
            _fixture.DbContext,
            _archiveStorage,
            archiveOptions,
            loggerFactory.CreateLogger<GibArchiveService>());

        return new GibUserListSyncService(
            _fixture.DbContext, downloader, transactionProcessor, archiveService, metadataService,
            metrics, syncTimeProvider, new NullWebhookNotifier(),
            loggerFactory.CreateLogger<GibUserListSyncService>());
    }

    private async Task<NpgsqlConnection> GetOpenConnectionAsync()
    {
        var connection = (NpgsqlConnection)_fixture.DbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();
        return connection;
    }

    private static async Task ExecuteSql(NpgsqlConnection connection, params string[] commands)
    {
        foreach (var sql in commands)
        {
            await using var cmd = new NpgsqlCommand(sql, connection);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task<string> DecompressGzipToString(Stream gzipStream)
    {
        using var decompressed = new GZipStream(gzipStream, CompressionMode.Decompress);
        using var reader = new StreamReader(decompressed, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }
}

/// <summary>
/// Gerçek GIB verisini indirip, belirli kullanıcıları listeden çıkararak sunan manipülatif downloader.
/// Diff engine'in "removed" davranışını test etmek için kullanılır.
/// </summary>
internal sealed class ManipulatedGibDownloader : IGibListDownloader
{
    private readonly IGibListDownloader _inner;
    private readonly HashSet<string> _removeIdentifiers;
    private readonly Dictionary<string, string> _titleChanges;
    private readonly string? _removeFromDocType;

    /// <summary>Kullanıcıları listeden çıkar.</summary>
    public ManipulatedGibDownloader(IGibListDownloader inner, IEnumerable<string> removeIdentifiers, string? removeFromDocType = null)
    {
        _inner = inner;
        _removeIdentifiers = new HashSet<string>(removeIdentifiers);
        _titleChanges = new Dictionary<string, string>();
        _removeFromDocType = removeFromDocType;
    }

    /// <summary>Kullanıcı title'larını değiştir.</summary>
    public ManipulatedGibDownloader(IGibListDownloader inner, Dictionary<string, string> titleChanges)
    {
        _inner = inner;
        _removeIdentifiers = [];
        _titleChanges = titleChanges;
    }

    public async Task DownloadPkListAsync(string outputPath, CancellationToken ct)
    {
        await _inner.DownloadPkListAsync(outputPath, ct);
        ManipulateZip(outputPath);
    }

    public async Task DownloadGbListAsync(string outputPath, CancellationToken ct)
    {
        await _inner.DownloadGbListAsync(outputPath, ct);
        ManipulateZip(outputPath);
    }

    private void ManipulateZip(string zipPath)
    {
        if (_removeIdentifiers.Count == 0 && _titleChanges.Count == 0) return;

        var tempDir = Path.Combine(Path.GetTempPath(), $"gib_manip_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            ZipFile.ExtractToDirectory(zipPath, tempDir);
            var xmlFile = Directory.GetFiles(tempDir, "*.xml").FirstOrDefault();
            if (xmlFile is null) return;

            var xmlContent = File.ReadAllText(xmlFile, Encoding.UTF8);
            var doc = new XmlDocument();
            doc.LoadXml(xmlContent);

            var userNodes = doc.DocumentElement?.GetElementsByTagName("User");
            if (userNodes is null) return;

            var nodesToRemove = new List<XmlNode>();
            for (var i = 0; i < userNodes.Count; i++)
            {
                var node = userNodes[i]!;
                var identifier = node.SelectSingleNode("Identifier")?.InnerText;
                if (identifier is null) continue;

                if (_removeIdentifiers.Contains(identifier))
                {
                    if (_removeFromDocType is null)
                    {
                        nodesToRemove.Add(node);
                    }
                    else
                    {
                        var documents = node.SelectSingleNode("Documents");
                        if (documents is not null)
                        {
                            var docNodes = documents.SelectNodes($"Document[@type='{_removeFromDocType}']");
                            if (docNodes is not null)
                            {
                                foreach (XmlNode docNode in docNodes)
                                    documents.RemoveChild(docNode);
                            }
                            if (!documents.HasChildNodes)
                                nodesToRemove.Add(node);
                        }
                    }
                }

                if (_titleChanges.TryGetValue(identifier, out var newTitle))
                {
                    var titleNode = node.SelectSingleNode("Title");
                    if (titleNode is not null)
                        titleNode.InnerText = newTitle;
                }
            }

            foreach (var node in nodesToRemove)
                node.ParentNode?.RemoveChild(node);

            doc.Save(xmlFile);
            File.Delete(zipPath);
            ZipFile.CreateFromDirectory(tempDir, zipPath);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
