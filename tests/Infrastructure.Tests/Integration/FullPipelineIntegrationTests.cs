using System.Globalization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MERSEL.Services.GibUserList.Infrastructure.Tests.Integration;

/// <summary>
/// GİB test ortamından gerçek XML'leri indirip tam pipeline'ı çalıştıran integration testleri.
/// Docker'da PostgreSQL Testcontainer üzerinde çalışır.
///
/// Pipeline: İndirme -> ZIP Çıkarma -> XML Parse -> COPY Bulk Insert -> 
///           Merge (ana tablolara) -> Materialized View Yenileme -> Sorgu Doğrulama
///
/// NOT: Bu testler GİB test ortamına (merkeztest.gib.gov.tr) HTTP erişimi ve Docker gerektirir.
/// CI ortamında bu testler [Trait("Category", "Integration")] ile filtrelenebilir.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public class FullPipelineIntegrationTests
{
    private readonly PostgresFixture _fixture;
    private static readonly CultureInfo TurkishCulture = new("tr-TR", false);

    public FullPipelineIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task FullSync_WithRealGibTestData_ShouldPopulateAllTablesAndViews()
    {
        // Arrange
        var downloader = new GibTestDownloader();
        var syncService = _fixture.CreateSyncService(downloader);

        // Act - Tam senkronizasyon çalıştır
        await syncService.SyncGibUserListsAsync(CancellationToken.None);

        // Assert - Ana tablolarda veri olmalı
        var connection = (NpgsqlConnection)_fixture.DbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        var eInvoiceCount = await GetTableCount(connection, "e_invoice_gib_users");
        var eDespatchCount = await GetTableCount(connection, "e_despatch_gib_users");

        eInvoiceCount.Should().BeGreaterThan(0, "e-Fatura ana tablosunda mükellef kaydı olmalı");
        eDespatchCount.Should().BeGreaterThan(0, "e-İrsaliye ana tablosunda mükellef kaydı olmalı");

        // Materialized view'larda da veri olmalı
        var mvEInvoiceCount = await GetTableCount(connection, "mv_e_invoice_gib_users");
        var mvEDespatchCount = await GetTableCount(connection, "mv_e_despatch_gib_users");

        mvEInvoiceCount.Should().Be(eInvoiceCount, "MV'deki kayıt sayısı ana tabloyla eşleşmeli");
        mvEDespatchCount.Should().Be(eDespatchCount, "MV'deki kayıt sayısı ana tabloyla eşleşmeli");

        // sync_metadata güncellenmeli
        var metadata = await _fixture.DbContext.SyncMetadata
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Key == Domain.Entities.SyncMetadata.SingletonKey);

        metadata.Should().NotBeNull("Senkronizasyon meta verisi oluşturulmuş olmalı");
        metadata!.EInvoiceUserCount.Should().Be(eInvoiceCount);
        metadata.EDespatchUserCount.Should().Be(eDespatchCount);
        metadata.LastSyncAt.Should().BeCloseTo(DateTime.Now, TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task FullSync_EInvoiceUsers_ShouldHaveValidIdentifiersAndAliases()
    {
        // Arrange & Act - Fixture zaten sync çalıştırmış olabilir, ama bu test bağımsız çalışmalı
        var downloader = new GibTestDownloader();
        var syncService = _fixture.CreateSyncService(downloader);
        await syncService.SyncGibUserListsAsync(CancellationToken.None);

        // Assert - EF Core üzerinden MV'den oku
        var users = await _fixture.DbContext.EInvoiceGibUsers
            .AsNoTracking()
            .Take(100)
            .ToListAsync();

        users.Should().NotBeEmpty("e-Fatura MV'sinde mükellef olmalı");

        foreach (var user in users)
        {
            // Identifier 10 veya 11 haneli rakam olmalı (VKN/TCKN)
            user.Identifier.Should().MatchRegex(@"^\d{10,11}$",
                $"Identifier '{user.Identifier}' geçerli VKN/TCKN formatında olmalı");

            // Title dolu olmalı
            user.Title.Should().NotBeNullOrWhiteSpace("Her mükellefte ünvan olmalı");

            // TitleLower, Title'ın Türkçe küçük harfe çevrilmiş hali olmalı
            user.TitleLower.Should().Be(user.Title.ToLower(TurkishCulture),
                "TitleLower, Türkçe kültüre göre küçük harfli olmalı");

            // AliasesJson boş olmayan bir JSONB olmalı
            user.AliasesJson.Should().NotBeNullOrWhiteSpace("Her mükellefte en az bir alias olmalı");
            user.AliasesJson.Should().StartWith("[", "AliasesJson geçerli JSON array olmalı");
        }
    }

    [Fact]
    public async Task FullSync_EDespatchUsers_ShouldHaveValidIdentifiersAndAliases()
    {
        // Arrange & Act
        var downloader = new GibTestDownloader();
        var syncService = _fixture.CreateSyncService(downloader);
        await syncService.SyncGibUserListsAsync(CancellationToken.None);

        // Assert - e-İrsaliye mükellefleri
        var users = await _fixture.DbContext.EDespatchGibUsers
            .AsNoTracking()
            .Take(100)
            .ToListAsync();

        users.Should().NotBeEmpty("e-İrsaliye MV'sinde mükellef olmalı");

        foreach (var user in users)
        {
            user.Identifier.Should().MatchRegex(@"^\d{10,11}$",
                $"Identifier '{user.Identifier}' geçerli VKN/TCKN formatında olmalı");

            user.Title.Should().NotBeNullOrWhiteSpace();
            user.TitleLower.Should().Be(user.Title.ToLower(TurkishCulture));
            user.AliasesJson.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task FullSync_SearchByTitle_ShouldReturnMatchingResults()
    {
        // Arrange & Act
        var downloader = new GibTestDownloader();
        var syncService = _fixture.CreateSyncService(downloader);
        await syncService.SyncGibUserListsAsync(CancellationToken.None);

        // Assert - Ünvana göre arama (Türkçe büyük/küçük harf duyarsız)
        // Test verisinde en az bir mükellef olacağından ilk mükellefi bulup ünvanıyla arayalım
        var firstUser = await _fixture.DbContext.EInvoiceGibUsers
            .AsNoTracking()
            .FirstAsync();

        var searchTerm = firstUser.Title[..Math.Min(5, firstUser.Title.Length)].ToLower(TurkishCulture);

        var searchResults = await _fixture.DbContext.EInvoiceGibUsers
            .AsNoTracking()
            .Where(u => u.TitleLower.Contains(searchTerm))
            .Take(10)
            .ToListAsync();

        searchResults.Should().NotBeEmpty($"'{searchTerm}' araması en az bir sonuç dönmeli");
        searchResults.Should().Contain(u => u.Identifier == firstUser.Identifier,
            "Aranan mükellef sonuçlar arasında olmalı");
    }

    [Fact]
    public async Task FullSync_SearchByIdentifier_ShouldReturnExactMatch()
    {
        // Arrange & Act
        var downloader = new GibTestDownloader();
        var syncService = _fixture.CreateSyncService(downloader);
        await syncService.SyncGibUserListsAsync(CancellationToken.None);

        // Assert - VKN/TCKN ile birebir sorgulama
        var knownUser = await _fixture.DbContext.EInvoiceGibUsers
            .AsNoTracking()
            .FirstAsync();

        var foundUser = await _fixture.DbContext.EInvoiceGibUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Identifier == knownUser.Identifier);

        foundUser.Should().NotBeNull("Bilinen bir identifier ile sorgulama sonuç dönmeli");
        foundUser!.Identifier.Should().Be(knownUser.Identifier);
        foundUser.Title.Should().Be(knownUser.Title);
    }

    [Fact]
    public async Task FullSync_AliasesJsonStructure_ShouldContainValidAliasObjects()
    {
        // Arrange & Act
        var downloader = new GibTestDownloader();
        var syncService = _fixture.CreateSyncService(downloader);
        await syncService.SyncGibUserListsAsync(CancellationToken.None);

        // Assert - aliases_json yapısını doğrula
        var userWithAliases = await _fixture.DbContext.EInvoiceGibUsers
            .AsNoTracking()
            .FirstAsync(u => u.AliasesJson != null);

        var aliases = Application.Queries.AliasJsonHelper.ParseAliases(userWithAliases.AliasesJson);

        aliases.Should().NotBeEmpty("AliasesJson'dan parse edilen alias listesi dolu olmalı");

        foreach (var alias in aliases)
        {
            alias.Name.Should().NotBeNullOrWhiteSpace("Alias adı dolu olmalı (ör: urn:mail:defaultpk)");
            alias.Type.Should().NotBeNullOrWhiteSpace("Alias tipi dolu olmalı (PK veya GB)");
            alias.CreationTime.Should().BeBefore(DateTime.Now, "Alias oluşturma zamanı geçmişte olmalı");
        }
    }

    [Fact]
    public async Task FullSync_ReRunSync_ShouldBeIdempotent()
    {
        // Arrange
        var downloader = new GibTestDownloader();
        var syncService = _fixture.CreateSyncService(downloader);

        // Act - İlk senkronizasyon
        await syncService.SyncGibUserListsAsync(CancellationToken.None);

        var connection = (NpgsqlConnection)_fixture.DbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        var countAfterFirst = await GetTableCount(connection, "e_invoice_gib_users");

        // Act - İkinci senkronizasyon (aynı veriyle)
        await syncService.SyncGibUserListsAsync(CancellationToken.None);

        var countAfterSecond = await GetTableCount(connection, "e_invoice_gib_users");

        // Assert - Kayıt sayısı aynı kalmalı (TRUNCATE + yeniden insert = aynı sonuç)
        countAfterSecond.Should().Be(countAfterFirst,
            "Tekrar senkronizasyon sonucu kayıt sayısı değişmemeli (idempotent)");
    }

    private static async Task<int> GetTableCount(NpgsqlConnection connection, string tableName)
    {
        // Güvenli tablo adı kontrolü
        var allowedTables = new HashSet<string>
        {
            "e_invoice_gib_users", "e_despatch_gib_users",
            "mv_e_invoice_gib_users", "mv_e_despatch_gib_users",
            "gib_user_temp_pk", "gib_user_temp_gb", "sync_metadata"
        };

        if (!allowedTables.Contains(tableName))
            throw new ArgumentException($"Geçersiz tablo adı: {tableName}");

        await using var cmd = new NpgsqlCommand($"SELECT COUNT(*)::int FROM {tableName}", connection);
        var result = await cmd.ExecuteScalarAsync();
        return result is int count ? count : 0;
    }
}
