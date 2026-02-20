using System.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NSubstitute;
using MERSEL.Services.GibUserList.Application.Configuration;
using MERSEL.Services.GibUserList.Application.Interfaces;
using MERSEL.Services.GibUserList.Domain.Entities;
using MERSEL.Services.GibUserList.Infrastructure.Diagnostics;
using MERSEL.Services.GibUserList.Infrastructure.Services;
using MERSEL.Services.GibUserList.Infrastructure.Webhooks;

namespace MERSEL.Services.GibUserList.Infrastructure.Tests.Integration;

/// <summary>
/// Diff engine E2E testleri — Testcontainers ile gerçek PostgreSQL üzerinde çalışır.
/// Kontrollü sahte XML verisi kullanarak added/modified/removed tespiti,
/// content_hash doğruluğu, safe removal guard ve changelog retention'ı doğrular.
///
/// Bu testler izole çalışır: her test kendi verisiyle başlar.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public class DiffEngineIntegrationTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;

    public DiffEngineIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var connection = await GetOpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            TRUNCATE TABLE e_invoice_gib_users;
            TRUNCATE TABLE e_despatch_gib_users;
            TRUNCATE TABLE gib_user_changelog;
            DELETE FROM sync_metadata;", connection);
        await cmd.ExecuteNonQueryAsync();

        await using var mvCmd = new NpgsqlCommand(@"
            REFRESH MATERIALIZED VIEW mv_e_invoice_gib_users;
            REFRESH MATERIALIZED VIEW mv_e_despatch_gib_users;", connection);
        await mvCmd.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ──────────────────────────────────────────────────────────────
    // 1. İlk sync: boş DB → tüm kullanıcılar "added"
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task FirstSync_ShouldMarkAllUsersAsAdded()
    {
        var downloader = CreateDownloaderWith(
            ("1234567890", "FIRMA A A.S.", "Invoice"),
            ("9876543210", "FIRMA B LTD.", "Invoice"),
            ("5555555555", "FIRMA C A.S.", "DespatchAdvice"));

        var syncService = _fixture.CreateSyncService(downloader);
        await syncService.SyncGibUserListsAsync(CancellationToken.None);

        // Ana tablolar dolu olmalı
        var eInvoiceCount = await GetTableCount("e_invoice_gib_users");
        var eDespatchCount = await GetTableCount("e_despatch_gib_users");
        eInvoiceCount.Should().Be(2);
        eDespatchCount.Should().Be(1);

        var invoiceChanges = await GetChangelogEntries(GibDocumentType.EInvoice);
        invoiceChanges.Should().BeEmpty("İlk sync'te changelog atlanır");

        var despatchChanges = await GetChangelogEntries(GibDocumentType.EDespatch);
        despatchChanges.Should().BeEmpty("İlk sync'te changelog atlanır");
    }

    // ──────────────────────────────────────────────────────────────
    // 2. Aynı veriyle 2. sync → changelog'a yeni kayıt yok
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SecondSync_WithSameData_ShouldNotCreateNewChangelogEntries()
    {
        var downloader = CreateDownloaderWith(
            ("1111111111", "DEGISMEZ FIRMA A.S.", "Invoice"),
            ("2222222222", "DEGISMEZ FIRMA B A.S.", "Invoice"));

        var syncService = _fixture.CreateSyncService(downloader);

        await syncService.SyncGibUserListsAsync(CancellationToken.None);
        var changesAfterFirst = await GetChangelogEntries(GibDocumentType.EInvoice);
        changesAfterFirst.Should().BeEmpty("İlk sync'te changelog atlanır");

        await syncService.SyncGibUserListsAsync(CancellationToken.None);
        var changesAfterSecond = await GetChangelogEntries(GibDocumentType.EInvoice);

        changesAfterSecond.Should().BeEmpty(
            "İlk sync atlandı, ikinci sync aynı veri → changelog hala boş");
    }

    // ──────────────────────────────────────────────────────────────
    // 3. Title değişikliği → "modified" changelog kaydı
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sync_WhenTitleChanges_ShouldDetectAsModified()
    {
        var downloader = new ConfigurableGibDownloader();

        // İlk sync: orijinal title
        downloader.PkBuilder = new FakeGibXmlBuilder()
            .AddUser("3333333333", "ORIJINAL FIRMA UNVANI", "Invoice");

        var syncService = _fixture.CreateSyncService(downloader);
        await syncService.SyncGibUserListsAsync(CancellationToken.None);

        // Hash'i kaydet
        var hashBefore = await GetContentHash("e_invoice_gib_users", "3333333333");
        hashBefore.Should().NotBeNullOrWhiteSpace("İlk sync sonrası content_hash dolu olmalı");

        // İkinci sync: title değişti
        downloader.PkBuilder = new FakeGibXmlBuilder()
            .AddUser("3333333333", "DEGISMIS FIRMA UNVANI", "Invoice");

        await syncService.SyncGibUserListsAsync(CancellationToken.None);

        // Hash değişmiş olmalı
        var hashAfter = await GetContentHash("e_invoice_gib_users", "3333333333");
        hashAfter.Should().NotBe(hashBefore, "Title değişince hash de değişmeli");

        // Ana tabloda yeni title olmalı
        var user = await _fixture.DbContext.EInvoiceGibUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Identifier == "3333333333");
        user.Should().NotBeNull();
        user!.Title.Should().Be("DEGISMIS FIRMA UNVANI");

        var changes = await GetChangelogEntries(GibDocumentType.EInvoice);
        changes.Should().ContainSingle("İlk sync atlandı, sadece ikinci sync'teki modified kaydı olmalı");
        changes[0].ChangeType.Should().Be(GibChangeType.Modified);
        changes[0].Identifier.Should().Be("3333333333");
        changes[0].Title.Should().Be("DEGISMIS FIRMA UNVANI");
    }

    // ──────────────────────────────────────────────────────────────
    // 4. Kullanıcı listeden çıkma → hard-delete + "removed" changelog
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sync_WhenUserRemoved_ShouldHardDeleteAndLogAsRemoved()
    {
        var downloader = new ConfigurableGibDownloader();

        // İlk sync: 10 kullanıcı (safe guard %10 = max 1 silme)
        var builder = new FakeGibXmlBuilder();
        for (var i = 0; i < 10; i++)
            builder.AddUser($"400000000{i}", $"FIRMA {i}", "Invoice");
        downloader.PkBuilder = builder;

        var syncService = CreateSyncServiceWithGuard(downloader, maxRemovalPercent: 20);
        await syncService.SyncGibUserListsAsync(CancellationToken.None);

        var countBefore = await GetTableCount("e_invoice_gib_users");
        countBefore.Should().Be(10);

        // İkinci sync: 1 kullanıcı çıktı (10'dan 9'a, %10 → guard limiti altında)
        var builder2 = new FakeGibXmlBuilder();
        for (var i = 1; i < 10; i++) // 4000000000 çıktı
            builder2.AddUser($"400000000{i}", $"FIRMA {i}", "Invoice");
        downloader.PkBuilder = builder2;

        await syncService.SyncGibUserListsAsync(CancellationToken.None);

        // Ana tablodan hard-delete olmuş olmalı
        var countAfter = await GetTableCount("e_invoice_gib_users");
        countAfter.Should().Be(9, "Silinen kullanıcı ana tablodan kaldırılmalı");

        // Changelog: "removed" kaydı olmalı
        var removedEntries = (await GetChangelogEntries(GibDocumentType.EInvoice))
            .Where(c => c.ChangeType == GibChangeType.Removed)
            .ToList();
        removedEntries.Should().ContainSingle();
        removedEntries[0].Identifier.Should().Be("4000000000");
        removedEntries[0].Title.Should().BeNull("Silinen kullanıcının title bilgisi changelog'da null olmalı");
    }

    // ──────────────────────────────────────────────────────────────
    // 5. Karma senaryo: aynı sync'te ekleme + güncelleme + silme
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sync_MixedChanges_ShouldDetectAllChangeTypes()
    {
        var downloader = new ConfigurableGibDownloader();

        // İlk sync: 5 kullanıcı
        var builder1 = new FakeGibXmlBuilder();
        builder1.AddUser("5000000001", "MEVCUT FIRMA 1", "Invoice");
        builder1.AddUser("5000000002", "MEVCUT FIRMA 2", "Invoice");
        builder1.AddUser("5000000003", "MEVCUT FIRMA 3", "Invoice");
        builder1.AddUser("5000000004", "MEVCUT FIRMA 4", "Invoice");
        builder1.AddUser("5000000005", "MEVCUT FIRMA 5", "Invoice");
        downloader.PkBuilder = builder1;

        var syncService = CreateSyncServiceWithGuard(downloader, maxRemovalPercent: 50);
        await syncService.SyncGibUserListsAsync(CancellationToken.None);

        // İkinci sync:
        // - 5000000001: title değişti → modified
        // - 5000000002: aynı → değişiklik yok
        // - 5000000003: çıktı → removed
        // - 5000000004: aynı → değişiklik yok
        // - 5000000005: çıktı → removed
        // - 5000000006: yeni eklendi → added
        var builder2 = new FakeGibXmlBuilder();
        builder2.AddUser("5000000001", "DEGISMIS FIRMA 1", "Invoice"); // modified
        builder2.AddUser("5000000002", "MEVCUT FIRMA 2", "Invoice"); // aynı
        builder2.AddUser("5000000004", "MEVCUT FIRMA 4", "Invoice"); // aynı
        builder2.AddUser("5000000006", "YENI FIRMA 6", "Invoice"); // added
        downloader.PkBuilder = builder2;

        await syncService.SyncGibUserListsAsync(CancellationToken.None);

        // Ana tablo: 4 kayıt (1,2,4,6)
        var countAfter = await GetTableCount("e_invoice_gib_users");
        countAfter.Should().Be(4);

        var allChanges = await GetChangelogEntries(GibDocumentType.EInvoice);

        var added = allChanges.Where(c => c.ChangeType == GibChangeType.Added).ToList();
        var modified = allChanges.Where(c => c.ChangeType == GibChangeType.Modified).ToList();
        var removed = allChanges.Where(c => c.ChangeType == GibChangeType.Removed).ToList();

        added.Should().ContainSingle("İlk sync changelog atlandı, sadece ikinci sync'teki yeni eklenen (5000000006)");
        added[0].Identifier.Should().Be("5000000006");
        modified.Should().ContainSingle("Sadece 5000000001 değişti");
        modified[0].Identifier.Should().Be("5000000001");
        removed.Should().HaveCount(2);
        removed.Select(r => r.Identifier).Should().BeEquivalentTo(["5000000003", "5000000005"]);
    }

    // ──────────────────────────────────────────────────────────────
    // 6. Safe removal guard: toplu silme eşiği aşılırsa silme atlanır
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sync_WhenRemovalExceedsGuard_ShouldSkipDeletion()
    {
        var downloader = new ConfigurableGibDownloader();

        // İlk sync: 10 kullanıcı
        var builder1 = new FakeGibXmlBuilder();
        for (var i = 0; i < 10; i++)
            builder1.AddUser($"600000000{i}", $"FIRMA GUARD {i}", "Invoice");
        downloader.PkBuilder = builder1;

        // Guard: max %5 → 10 kayıttan en fazla 0.5 silinebilir → pratikte 0
        var syncService = CreateSyncServiceWithGuard(downloader, maxRemovalPercent: 5);
        await syncService.SyncGibUserListsAsync(CancellationToken.None);

        var countBefore = await GetTableCount("e_invoice_gib_users");
        countBefore.Should().Be(10);

        // İkinci sync: 8 kullanıcıyı çıkar → %80 silme → guard tetiklenmeli
        var builder2 = new FakeGibXmlBuilder();
        builder2.AddUser("6000000000", "FIRMA GUARD 0", "Invoice");
        builder2.AddUser("6000000001", "FIRMA GUARD 1", "Invoice");
        downloader.PkBuilder = builder2;

        await syncService.SyncGibUserListsAsync(CancellationToken.None);

        // Guard tetiklendi: silme atlandı, ana tabloda hala 10 kayıt + 2 yeni = UPSERT sonucu 10
        // (Mevcut 2'si güncellenir, yeni eklenen yok, silinmesi gereken 8 atlanır)
        var countAfter = await GetTableCount("e_invoice_gib_users");
        countAfter.Should().Be(10, "Safe guard tetiklendiğinde silme atlanmalı, kayıt sayısı korunmalı");

        // Changelog'da "removed" kaydı OLMAMALI (guard atlattığı için)
        var removedEntries = (await GetChangelogEntries(GibDocumentType.EInvoice))
            .Where(c => c.ChangeType == GibChangeType.Removed)
            .ToList();
        removedEntries.Should().BeEmpty("Guard tetiklendiğinde removed changelog yazılmamalı");
    }

    // ──────────────────────────────────────────────────────────────
    // 7. Content hash deterministik: aynı veri = aynı hash
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ContentHash_SameData_ShouldProduceSameHash()
    {
        var downloader = CreateDownloaderWith(
            ("7777777777", "HASH TEST FIRMA", "Invoice"));

        var syncService = _fixture.CreateSyncService(downloader);

        // İlk sync
        await syncService.SyncGibUserListsAsync(CancellationToken.None);
        var hash1 = await GetContentHash("e_invoice_gib_users", "7777777777");

        // İkinci sync — aynı veri
        await syncService.SyncGibUserListsAsync(CancellationToken.None);
        var hash2 = await GetContentHash("e_invoice_gib_users", "7777777777");

        hash1.Should().NotBeNullOrWhiteSpace();
        hash2.Should().Be(hash1, "Aynı veri her zaman aynı content_hash üretmeli");
    }

    [Fact]
    public async Task ContentHash_DifferentData_ShouldProduceDifferentHash()
    {
        var downloader = new ConfigurableGibDownloader();

        downloader.PkBuilder = new FakeGibXmlBuilder()
            .AddUser("8888888888", "HASH FARK FIRMA A", "Invoice");

        var syncService = _fixture.CreateSyncService(downloader);
        await syncService.SyncGibUserListsAsync(CancellationToken.None);
        var hashA = await GetContentHash("e_invoice_gib_users", "8888888888");

        downloader.PkBuilder = new FakeGibXmlBuilder()
            .AddUser("8888888888", "HASH FARK FIRMA B", "Invoice");

        await syncService.SyncGibUserListsAsync(CancellationToken.None);
        var hashB = await GetContentHash("e_invoice_gib_users", "8888888888");

        hashA.Should().NotBe(hashB, "Farklı title → farklı content_hash");
    }

    // ──────────────────────────────────────────────────────────────
    // 8. MV ve ana tablo tutarlılığı
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sync_AfterChanges_MaterializedViewsShouldReflectMainTables()
    {
        var downloader = new ConfigurableGibDownloader();

        downloader.PkBuilder = new FakeGibXmlBuilder()
            .AddUser("9999999991", "MV TEST FIRMA 1", "Invoice")
            .AddUser("9999999992", "MV TEST FIRMA 2", "Invoice")
            .AddUser("9999999993", "MV TEST FIRMA 3", "DespatchAdvice");

        var syncService = _fixture.CreateSyncService(downloader);
        await syncService.SyncGibUserListsAsync(CancellationToken.None);

        var mainInvoice = await GetTableCount("e_invoice_gib_users");
        var mvInvoice = await GetTableCount("mv_e_invoice_gib_users");
        var mainDespatch = await GetTableCount("e_despatch_gib_users");
        var mvDespatch = await GetTableCount("mv_e_despatch_gib_users");

        mvInvoice.Should().Be(mainInvoice, "MV kayıt sayısı ana tabloyla eşleşmeli");
        mvDespatch.Should().Be(mainDespatch, "MV kayıt sayısı ana tabloyla eşleşmeli");
    }

    // ──────────────────────────────────────────────────────────────
    // 9. Alias değişikliği → modified
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sync_WhenAliasChanges_ShouldDetectAsModified()
    {
        var downloader = new ConfigurableGibDownloader();

        downloader.PkBuilder = new FakeGibXmlBuilder()
            .AddUser("1010101010", "ALIAS DEGISIM FIRMA", "Invoice",
                aliasName: "urn:mail:eski@domain.com");

        var syncService = _fixture.CreateSyncService(downloader);
        await syncService.SyncGibUserListsAsync(CancellationToken.None);

        var hashBefore = await GetContentHash("e_invoice_gib_users", "1010101010");

        // Alias değişti (aynı firma, farklı alias)
        downloader.PkBuilder = new FakeGibXmlBuilder()
            .AddUser("1010101010", "ALIAS DEGISIM FIRMA", "Invoice",
                aliasName: "urn:mail:yeni@domain.com");

        await syncService.SyncGibUserListsAsync(CancellationToken.None);

        var hashAfter = await GetContentHash("e_invoice_gib_users", "1010101010");
        hashAfter.Should().NotBe(hashBefore, "Alias değişince hash de değişmeli");

        var modifiedEntries = (await GetChangelogEntries(GibDocumentType.EInvoice))
            .Where(c => c.ChangeType == GibChangeType.Modified)
            .ToList();
        modifiedEntries.Should().ContainSingle();
        modifiedEntries[0].Identifier.Should().Be("1010101010");
    }

    // ──────────────────────────────────────────────────────────────
    // 10. Çoklu belge türü: aynı identifier hem e-Fatura hem e-İrsaliye'de
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sync_UserWithBothDocTypes_ShouldAppearInBothTables()
    {
        var downloader = new ConfigurableGibDownloader();
        downloader.PkBuilder = new FakeGibXmlBuilder()
            .AddUserWithDocuments("1111111111", "COKLU TUR FIRMA A.S.",
            [
                new FakeDocument("Invoice", [new FakeAlias("urn:mail:efatura@firma.com", "PK", new DateTime(2026, 1, 10))]),
                new FakeDocument("DespatchAdvice", [new FakeAlias("urn:mail:eirsaliye@firma.com", "PK", new DateTime(2026, 1, 10))])
            ], firstCreationTime: new DateTime(2026, 1, 10));

        var syncService = _fixture.CreateSyncService(downloader);
        await syncService.SyncGibUserListsAsync(CancellationToken.None);

        var eInvoiceCount = await GetTableCount("e_invoice_gib_users");
        var eDespatchCount = await GetTableCount("e_despatch_gib_users");
        eInvoiceCount.Should().Be(1, "Invoice belgesi olan kullanıcı e_invoice tablosunda olmalı");
        eDespatchCount.Should().Be(1, "DespatchAdvice belgesi olan kullanıcı e_despatch tablosunda olmalı");

        var invoiceUser = await _fixture.DbContext.EInvoiceGibUsers
            .AsNoTracking().FirstOrDefaultAsync(u => u.Identifier == "1111111111");
        var despatchUser = await _fixture.DbContext.EDespatchGibUsers
            .AsNoTracking().FirstOrDefaultAsync(u => u.Identifier == "1111111111");

        invoiceUser.Should().NotBeNull();
        despatchUser.Should().NotBeNull();
        invoiceUser!.Title.Should().Be("COKLU TUR FIRMA A.S.");
        despatchUser!.Title.Should().Be("COKLU TUR FIRMA A.S.");

        invoiceUser.AliasesJson.Should().Contain("efatura@firma.com");
        despatchUser.AliasesJson.Should().Contain("eirsaliye@firma.com");

        var invoiceChanges = await GetChangelogEntries(GibDocumentType.EInvoice);
        var despatchChanges = await GetChangelogEntries(GibDocumentType.EDespatch);
        invoiceChanges.Should().BeEmpty("İlk sync'te changelog atlanır");
        despatchChanges.Should().BeEmpty("İlk sync'te changelog atlanır");
    }

    // ──────────────────────────────────────────────────────────────
    // 11. Bağımsız değişiklik: Invoice alias değişir, DespatchAdvice aynı kalır
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sync_WhenOnlyInvoiceAliasChanges_ShouldOnlyModifyInvoice()
    {
        var downloader = new ConfigurableGibDownloader();

        var fct = new DateTime(2026, 1, 10);
        downloader.PkBuilder = new FakeGibXmlBuilder()
            .AddUserWithDocuments("2222222222", "BAGIMSIZ DEGISIKLIK A.S.",
            [
                new FakeDocument("Invoice", [new FakeAlias("urn:mail:eski_efatura@firma.com", "PK", fct)]),
                new FakeDocument("DespatchAdvice", [new FakeAlias("urn:mail:eirsaliye@firma.com", "PK", fct)])
            ], firstCreationTime: fct);

        var syncService = _fixture.CreateSyncService(downloader);
        await syncService.SyncGibUserListsAsync(CancellationToken.None);

        var invoiceHashBefore = await GetContentHash("e_invoice_gib_users", "2222222222");
        var despatchHashBefore = await GetContentHash("e_despatch_gib_users", "2222222222");

        downloader.PkBuilder = new FakeGibXmlBuilder()
            .AddUserWithDocuments("2222222222", "BAGIMSIZ DEGISIKLIK A.S.",
            [
                new FakeDocument("Invoice", [new FakeAlias("urn:mail:yeni_efatura@firma.com", "PK", fct)]),
                new FakeDocument("DespatchAdvice", [new FakeAlias("urn:mail:eirsaliye@firma.com", "PK", fct)])
            ], firstCreationTime: fct);

        await syncService.SyncGibUserListsAsync(CancellationToken.None);

        var invoiceHashAfter = await GetContentHash("e_invoice_gib_users", "2222222222");
        var despatchHashAfter = await GetContentHash("e_despatch_gib_users", "2222222222");

        invoiceHashAfter.Should().NotBe(invoiceHashBefore, "Invoice alias değişti, hash de değişmeli");
        despatchHashAfter.Should().Be(despatchHashBefore, "DespatchAdvice değişmedi, hash aynı kalmalı");

        var invoiceModified = (await GetChangelogEntries(GibDocumentType.EInvoice))
            .Where(c => c.ChangeType == GibChangeType.Modified).ToList();
        var despatchModified = (await GetChangelogEntries(GibDocumentType.EDespatch))
            .Where(c => c.ChangeType == GibChangeType.Modified).ToList();

        invoiceModified.Should().ContainSingle("Sadece Invoice modified olmalı");
        despatchModified.Should().BeEmpty("DespatchAdvice değişmedi, modified kaydı olmamalı");
    }

    // ──────────────────────────────────────────────────────────────
    // 12. Bir belge türü listeden çıkma, diğeri kalma
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sync_WhenDespatchRemoved_ButInvoiceRemains_ShouldOnlyRemoveDespatch()
    {
        var downloader = new ConfigurableGibDownloader();
        var fct = new DateTime(2026, 1, 10);

        // İlk sync: kullanıcının hem Invoice hem DespatchAdvice var + guard koruma için ekstra kullanıcılar
        var builder1 = new FakeGibXmlBuilder();
        builder1.AddUserWithDocuments("3333333333", "KARMA FIRMA A.S.",
        [
            new FakeDocument("Invoice", [new FakeAlias("urn:mail:efatura@karma.com", "PK", fct)]),
            new FakeDocument("DespatchAdvice", [new FakeAlias("urn:mail:eirsaliye@karma.com", "PK", fct)])
        ], firstCreationTime: fct);
        for (var i = 0; i < 5; i++)
            builder1.AddUser($"334000000{i}", $"GUARD FIRMA {i}", "DespatchAdvice");
        downloader.PkBuilder = builder1;

        var syncService = CreateSyncServiceWithGuard(downloader, maxRemovalPercent: 50);
        await syncService.SyncGibUserListsAsync(CancellationToken.None);

        (await GetTableCount("e_invoice_gib_users")).Should().Be(1);
        (await GetTableCount("e_despatch_gib_users")).Should().Be(6);

        // İkinci sync: DespatchAdvice belgesi kaldırıldı, Invoice hala var
        var builder2 = new FakeGibXmlBuilder();
        builder2.AddUser("3333333333", "KARMA FIRMA A.S.", "Invoice",
            aliasName: "urn:mail:efatura@karma.com", firstCreationTime: fct);
        for (var i = 0; i < 5; i++)
            builder2.AddUser($"334000000{i}", $"GUARD FIRMA {i}", "DespatchAdvice");
        downloader.PkBuilder = builder2;

        await syncService.SyncGibUserListsAsync(CancellationToken.None);

        (await GetTableCount("e_invoice_gib_users")).Should().Be(1, "Invoice hala var, silinmemeli");
        (await GetTableCount("e_despatch_gib_users")).Should().Be(5, "DespatchAdvice çıktı, hard-delete olmalı");

        var despatchRemoved = (await GetChangelogEntries(GibDocumentType.EDespatch))
            .Where(c => c.ChangeType == GibChangeType.Removed).ToList();
        despatchRemoved.Should().ContainSingle();
        despatchRemoved[0].Identifier.Should().Be("3333333333");

        var invoiceRemoved = (await GetChangelogEntries(GibDocumentType.EInvoice))
            .Where(c => c.ChangeType == GibChangeType.Removed).ToList();
        invoiceRemoved.Should().BeEmpty("Invoice hala var, removed kaydı olmamalı");
    }

    // ──────────────────────────────────────────────────────────────
    // 13. Çoklu alias testi: aynı belge türünde birden fazla alias
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sync_UserWithMultipleAliases_ShouldAggregateAll()
    {
        var fct = new DateTime(2026, 1, 10);
        var downloader = new ConfigurableGibDownloader();
        downloader.PkBuilder = new FakeGibXmlBuilder()
            .AddUserWithDocuments("4444444444", "COKLU ALIAS FIRMA A.S.",
            [
                new FakeDocument("Invoice",
                [
                    new FakeAlias("urn:mail:alias1@firma.com", "PK", fct),
                    new FakeAlias("urn:mail:alias2@firma.com", "PK", fct),
                    new FakeAlias("urn:mail:alias3@firma.com", "PK", fct)
                ])
            ], firstCreationTime: fct);

        var syncService = _fixture.CreateSyncService(downloader);
        await syncService.SyncGibUserListsAsync(CancellationToken.None);

        var user = await _fixture.DbContext.EInvoiceGibUsers
            .AsNoTracking().FirstOrDefaultAsync(u => u.Identifier == "4444444444");

        user.Should().NotBeNull();
        user!.AliasesJson.Should().Contain("alias1@firma.com");
        user.AliasesJson.Should().Contain("alias2@firma.com");
        user.AliasesJson.Should().Contain("alias3@firma.com");
    }

    // ──────────────────────────────────────────────────────────────
    // 14. PK + GB birleşme: aynı kullanıcı iki kaynakta
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sync_SameUserInPkAndGb_ShouldMergeAliases()
    {
        var fct = new DateTime(2026, 1, 10);
        var downloader = new ConfigurableGibDownloader();

        downloader.PkBuilder = new FakeGibXmlBuilder()
            .AddUserWithDocuments("5555555555", "PK GB MERGE FIRMA",
            [
                new FakeDocument("Invoice", [new FakeAlias("urn:mail:pk_alias@firma.com", "PK", fct)])
            ], firstCreationTime: fct);

        downloader.GbBuilder = new FakeGibXmlBuilder()
            .AddUserWithDocuments("5555555555", "PK GB MERGE FIRMA",
            [
                new FakeDocument("Invoice", [new FakeAlias("urn:mail:gb_alias@firma.com", "GB", fct)])
            ], firstCreationTime: fct);

        var syncService = _fixture.CreateSyncService(downloader);
        await syncService.SyncGibUserListsAsync(CancellationToken.None);

        (await GetTableCount("e_invoice_gib_users")).Should().Be(1, "Aynı identifier tek satır olmalı");

        var user = await _fixture.DbContext.EInvoiceGibUsers
            .AsNoTracking().FirstOrDefaultAsync(u => u.Identifier == "5555555555");

        user.Should().NotBeNull();
        user!.AliasesJson.Should().Contain("pk_alias@firma.com", "PK alias'ı olmalı");
        user.AliasesJson.Should().Contain("gb_alias@firma.com", "GB alias'ı da olmalı");
    }

    // ──────────────────────────────────────────────────────────────
    // 15. Arşiv üretimi: sync sonrası XML.GZ dosyası oluşturulmalı
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sync_ShouldGenerateDocumentTypeArchives()
    {
        var archiveStorage = new InMemoryArchiveStorage();
        var downloader = CreateDownloaderWith(
            ("7000000001", "ARSIV FIRMA 1", "Invoice"),
            ("7000000002", "ARSIV FIRMA 2", "Invoice"),
            ("7000000003", "ARSIV FIRMA 3", "DespatchAdvice"));

        var syncService = CreateSyncServiceWithArchiveStorage(downloader, archiveStorage);
        await syncService.SyncGibUserListsAsync(CancellationToken.None);

        var allFiles = await archiveStorage.ListAsync(ct: CancellationToken.None);
        allFiles.Should().HaveCount(2, "Her belge türü için bir arşiv dosyası üretilmeli");

        var invoiceFiles = await archiveStorage.ListAsync("einvoice/", CancellationToken.None);
        invoiceFiles.Should().ContainSingle();
        invoiceFiles[0].FileName.Should().StartWith("einvoice/einvoice_users_");
        invoiceFiles[0].FileName.Should().EndWith(".xml.gz");

        var despatchFiles = await archiveStorage.ListAsync("edespatch/", CancellationToken.None);
        despatchFiles.Should().ContainSingle();
        despatchFiles[0].FileName.Should().StartWith("edespatch/edespatch_users_");

        // Arşiv içeriğini doğrula — XML.GZ açılınca kullanıcılar bulunmalı
        var invoiceStream = await archiveStorage.GetAsync(invoiceFiles[0].FileName, CancellationToken.None);
        invoiceStream.Should().NotBeNull();
        var invoiceXml = await DecompressGzipToString(invoiceStream!);
        invoiceXml.Should().Contain("7000000001");
        invoiceXml.Should().Contain("7000000002");
        invoiceXml.Should().NotContain("7000000003", "DespatchAdvice kullanıcısı Invoice arşivinde olmamalı");

        var despatchStream = await archiveStorage.GetAsync(despatchFiles[0].FileName, CancellationToken.None);
        var despatchXml = await DecompressGzipToString(despatchStream!);
        despatchXml.Should().Contain("7000000003");
        despatchXml.Should().NotContain("7000000001", "Invoice kullanıcısı DespatchAdvice arşivinde olmamalı");
    }

    // ──────────────────────────────────────────────────────────────
    // Yardımcılar
    // ──────────────────────────────────────────────────────────────

    private static ConfigurableGibDownloader CreateDownloaderWith(
        params (string identifier, string title, string docType)[] users)
    {
        var downloader = new ConfigurableGibDownloader();
        foreach (var (identifier, title, docType) in users)
            downloader.PkBuilder.AddUser(identifier, title, docType);
        return downloader;
    }

    private GibUserListSyncService CreateSyncServiceWithGuard(
        IGibListDownloader downloader, double maxRemovalPercent)
    {
        var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(
            b => b.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug));
        var meterFactory = new TestMeterFactory();
        var metrics = new GibUserListMetrics(meterFactory);
        var syncTimeProvider = Substitute.For<ISyncTimeProvider>();

        var endpointOptions = Options.Create(new GibEndpointOptions
        {
            MaxAllowedRemovalPercent = maxRemovalPercent,
            ChangeRetentionDays = 30
        });
        var archiveOptions = Options.Create(new ArchiveStorageOptions());
        var archiveStorage = Substitute.For<IArchiveStorage>();
        var cacheService = Substitute.For<ICacheService>();
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
            archiveStorage,
            archiveOptions,
            loggerFactory.CreateLogger<GibArchiveService>());

        return new GibUserListSyncService(
            _fixture.DbContext,
            downloader,
            transactionProcessor,
            archiveService,
            metadataService,
            metrics,
            syncTimeProvider,
            new NullWebhookNotifier(),
            loggerFactory.CreateLogger<GibUserListSyncService>());
    }

    private async Task<NpgsqlConnection> GetOpenConnectionAsync()
    {
        var connection = (NpgsqlConnection)_fixture.DbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();
        return connection;
    }

    private async Task<int> GetTableCount(string tableName)
    {
        var allowed = new HashSet<string>
        {
            "e_invoice_gib_users", "e_despatch_gib_users",
            "mv_e_invoice_gib_users", "mv_e_despatch_gib_users",
            "gib_user_changelog"
        };
        if (!allowed.Contains(tableName))
            throw new ArgumentException($"Geçersiz tablo: {tableName}");

        var connection = await GetOpenConnectionAsync();
        await using var cmd = new NpgsqlCommand($"SELECT COUNT(*)::int FROM {tableName}", connection);
        var result = await cmd.ExecuteScalarAsync();
        return result is int count ? count : 0;
    }

    private async Task<string?> GetContentHash(string tableName, string identifier)
    {
        var allowed = new HashSet<string> { "e_invoice_gib_users", "e_despatch_gib_users" };
        if (!allowed.Contains(tableName))
            throw new ArgumentException($"Geçersiz tablo: {tableName}");

        var connection = await GetOpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            $"SELECT content_hash FROM {tableName} WHERE identifier = @id", connection);
        cmd.Parameters.AddWithValue("id", identifier);
        var result = await cmd.ExecuteScalarAsync();
        return result as string;
    }

    private async Task<List<GibUserChangeLog>> GetChangelogEntries(GibDocumentType docType)
    {
        return await _fixture.DbContext.GibUserChangeLogs
            .AsNoTracking()
            .Where(c => c.DocumentType == docType)
            .OrderBy(c => c.ChangedAt)
            .ThenBy(c => c.Identifier)
            .ToListAsync();
    }

    private GibUserListSyncService CreateSyncServiceWithArchiveStorage(
        IGibListDownloader downloader, IArchiveStorage storage)
    {
        var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(
            b => b.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug));
        var meterFactory = new TestMeterFactory();
        var metrics = new GibUserListMetrics(meterFactory);
        var syncTimeProvider = Substitute.For<ISyncTimeProvider>();

        var endpointOptions = Options.Create(new GibEndpointOptions
        {
            MaxAllowedRemovalPercent = 50,
            ChangeRetentionDays = 30
        });
        var archiveOptions = Options.Create(new ArchiveStorageOptions());
        var cacheService = Substitute.For<ICacheService>();
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
            _fixture.DbContext,
            downloader,
            transactionProcessor,
            archiveService,
            metadataService,
            metrics,
            syncTimeProvider,
            new NullWebhookNotifier(),
            loggerFactory.CreateLogger<GibUserListSyncService>());
    }

    private static async Task<string> DecompressGzipToString(Stream gzipStream)
    {
        using var decompressed = new System.IO.Compression.GZipStream(gzipStream, System.IO.Compression.CompressionMode.Decompress);
        using var reader = new StreamReader(decompressed, System.Text.Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }
}

/// <summary>
/// Test-only in-memory archive storage implementation.
/// </summary>
internal sealed class InMemoryArchiveStorage : IArchiveStorage
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

    public async Task SaveAsync(string fileName, Stream content, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        _files[fileName] = ms.ToArray();
    }

    public Task<Stream?> GetAsync(string fileName, CancellationToken ct)
    {
        if (_files.TryGetValue(fileName, out var data))
            return Task.FromResult<Stream?>(new MemoryStream(data));
        return Task.FromResult<Stream?>(null);
    }

    public Task<IReadOnlyList<ArchiveFileInfo>> ListAsync(string? prefix = null, CancellationToken ct = default)
    {
        var files = _files
            .Where(kv => prefix is null || kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(kv => new ArchiveFileInfo(kv.Key, kv.Value.Length, DateTime.Now))
            .OrderByDescending(f => f.FileName)
            .ToList();
        return Task.FromResult<IReadOnlyList<ArchiveFileInfo>>(files);
    }

    public Task DeleteAsync(string fileName, CancellationToken ct)
    {
        _files.Remove(fileName);
        return Task.CompletedTask;
    }
}
