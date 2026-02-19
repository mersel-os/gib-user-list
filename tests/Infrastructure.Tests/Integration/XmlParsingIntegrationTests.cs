using System.IO.Compression;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using MERSEL.Services.GibUserList.Infrastructure.Services;

namespace MERSEL.Services.GibUserList.Infrastructure.Tests.Integration;

/// <summary>
/// GİB test ortamından indirilen gerçek XML dosyalarının yapısını doğrulayan testler.
/// XML şemasının canlı ortamla aynı olduğunu garanti eder.
/// Docker gerekmez, sadece HTTP erişimi yeterlidir.
/// </summary>
[Trait("Category", "Integration")]
public class XmlParsingIntegrationTests
{
    private const string PkListUrl = "https://merkeztest.gib.gov.tr/EFaturaMerkez/newUserPkListxml.zip";
    private const string GbListUrl = "https://merkeztest.gib.gov.tr/EFaturaMerkez/newUserGbListxml.zip";

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(5) };

    [Fact]
    public async Task PkList_DownloadAndParse_ShouldYieldValidUsers()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"gib_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var zipPath = Path.Combine(tempDir, "pk_list.zip");
            await DownloadFileAsync(PkListUrl, zipPath);

            var xmlPath = ExtractFirstXml(zipPath, tempDir, "pk");

            var parser = CreateParser();

            // Act
            var users = parser.ParseUsers(xmlPath).ToList();

            // Assert
            users.Should().NotBeEmpty("PK listesinde en az bir mükellef olmalı");

            var sampleUser = users.First();
            sampleUser.Identifier.Should().NotBeNullOrWhiteSpace("Identifier alanı dolu olmalı");
            sampleUser.Title.Should().NotBeNullOrWhiteSpace("Title alanı dolu olmalı");
            sampleUser.Documents.Should().NotBeNull("Her kullanıcıda Documents olmalı");
            sampleUser.Documents!.Document.Should().NotBeNull("En az bir Document öğesi olmalı");
            sampleUser.Documents.Document!.Should().NotBeEmpty();

            // Belge tiplerini doğrula
            var documentTypes = users
                .Where(u => u.Documents?.Document is not null)
                .SelectMany(u => u.Documents!.Document!)
                .Select(d => d.Type)
                .Distinct()
                .ToList();

            documentTypes.Should().NotBeEmpty("Belge tipleri bulunmalı");

            // Alias yapısını doğrula
            var usersWithAliases = users
                .Where(u => u.Documents?.Document is not null)
                .SelectMany(u => u.Documents!.Document!)
                .Where(d => d.Aliases is not null)
                .SelectMany(d => d.Aliases!)
                .Where(a => a.Names is not null && a.Names.Count > 0)
                .ToList();

            usersWithAliases.Should().NotBeEmpty("En az bir alias içeren mükellef olmalı");
            usersWithAliases.First().Names.Should().NotBeEmpty();
            usersWithAliases.First().CreationTime.Should().BeBefore(DateTime.Now);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GbList_DownloadAndParse_ShouldYieldValidUsers()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"gib_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var zipPath = Path.Combine(tempDir, "gb_list.zip");
            await DownloadFileAsync(GbListUrl, zipPath);

            var xmlPath = ExtractFirstXml(zipPath, tempDir, "gb");

            var parser = CreateParser();

            // Act
            var users = parser.ParseUsers(xmlPath).ToList();

            // Assert
            users.Should().NotBeEmpty("GB listesinde en az bir mükellef olmalı");

            var sampleUser = users.First();
            sampleUser.Identifier.Should().NotBeNullOrWhiteSpace();
            sampleUser.Title.Should().NotBeNullOrWhiteSpace();
            sampleUser.Documents.Should().NotBeNull();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PkList_DocumentTypes_ShouldContainInvoiceOrDespatchAdvice()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"gib_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var zipPath = Path.Combine(tempDir, "pk_list.zip");
            await DownloadFileAsync(PkListUrl, zipPath);
            var xmlPath = ExtractFirstXml(zipPath, tempDir, "pk");

            var parser = CreateParser();
            var users = parser.ParseUsers(xmlPath).Take(1000).ToList();

            // Act
            var documentTypes = users
                .Where(u => u.Documents?.Document is not null)
                .SelectMany(u => u.Documents!.Document!)
                .Select(d => d.Type)
                .Distinct()
                .ToList();

            // Assert - Bilinen GİB belge tipleri olmalı
            documentTypes.Should().NotBeEmpty("Belge tipleri listesi boş olmamalı");

            var knownTypes = new[] { "Invoice", "DespatchAdvice" };
            documentTypes.Should().IntersectWith(knownTypes,
                "Belge tipleri bilinen GİB tipleriyle (Invoice, DespatchAdvice) kesişmeli");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PkList_UserCount_ShouldBeReasonable()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"gib_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var zipPath = Path.Combine(tempDir, "pk_list.zip");
            await DownloadFileAsync(PkListUrl, zipPath);
            var xmlPath = ExtractFirstXml(zipPath, tempDir, "pk");

            var parser = CreateParser();

            // Act
            var count = parser.ParseUsers(xmlPath).Count();

            // Assert - Test ortamında makul bir sayıda mükellef olmalı
            count.Should().BeGreaterThan(10,
                "Test ortamında en az 10 mükellef olması beklenir");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private static GibXmlStreamParser CreateParser()
    {
        using var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Information));
        return new GibXmlStreamParser(loggerFactory.CreateLogger<GibXmlStreamParser>());
    }

    private async Task DownloadFileAsync(string url, string outputPath)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
        await stream.CopyToAsync(fileStream);
    }

    private static string ExtractFirstXml(string zipPath, string targetFolder, string prefix)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.FirstOrDefault(
            e => e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"{zipPath} içinde XML dosyası bulunamadı");

        var extractPath = Path.Combine(targetFolder, $"{prefix}_{entry.Name}");
        entry.ExtractToFile(extractPath, overwrite: true);
        return extractPath;
    }
}
