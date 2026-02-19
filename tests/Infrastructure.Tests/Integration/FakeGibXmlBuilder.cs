using System.IO.Compression;
using System.Text;
using MERSEL.Services.GibUserList.Application.Interfaces;

namespace MERSEL.Services.GibUserList.Infrastructure.Tests.Integration;

/// <summary>
/// Kontrollü GIB XML dosyaları üreten test yardımcısı.
/// Diff engine E2E testlerinde kullanılır — her senkronizasyonda farklı veri seti üretebilir.
/// Çoklu belge türü ve çoklu alias desteği ile gerçek GIB XML yapısını simüle eder.
/// </summary>
public sealed class FakeGibXmlBuilder
{
    private readonly List<FakeGibUserEntry> _entries = [];

    /// <summary>
    /// Tek belge türlü kullanıcı ekler (geriye uyumlu).
    /// </summary>
    public FakeGibXmlBuilder AddUser(string identifier, string title,
        string documentType = "Invoice", string aliasType = "PK",
        string? aliasName = null, string accountType = "Ozel",
        string type = "Kagit", DateTime? firstCreationTime = null,
        DateTime? aliasCreationTime = null)
    {
        aliasName ??= $"urn:mail:default{identifier}";
        firstCreationTime ??= new DateTime(2026, 1, 15, 0, 0, 0);
        aliasCreationTime ??= firstCreationTime;

        var entry = GetOrCreateEntry(identifier, title, accountType, type, firstCreationTime.Value);
        entry.Documents.Add(new FakeDocument(documentType,
        [
            new FakeAlias(aliasName, aliasType, aliasCreationTime.Value)
        ]));
        return this;
    }

    /// <summary>
    /// Çoklu belge türüne sahip kullanıcı ekler — gerçek GIB verisini simüle eder.
    /// Aynı kullanıcının hem Invoice hem DespatchAdvice belgesi olabilir.
    /// </summary>
    public FakeGibXmlBuilder AddUserWithDocuments(string identifier, string title,
        FakeDocument[] documents,
        string accountType = "Ozel", string type = "Kagit",
        DateTime? firstCreationTime = null)
    {
        firstCreationTime ??= new DateTime(2026, 1, 15, 0, 0, 0);
        var entry = GetOrCreateEntry(identifier, title, accountType, type, firstCreationTime.Value);
        entry.Documents.AddRange(documents);
        return this;
    }

    public string BuildXml()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<UserList>");

        foreach (var entry in _entries)
        {
            sb.AppendLine("  <User>");
            sb.AppendLine($"    <Identifier>{entry.Identifier}</Identifier>");
            sb.AppendLine($"    <Title>{EscapeXml(entry.Title)}</Title>");
            sb.AppendLine($"    <Type>{entry.Type}</Type>");
            sb.AppendLine($"    <AccountType>{entry.AccountType}</AccountType>");
            sb.AppendLine($"    <FirstCreationTime>{entry.FirstCreationTime:yyyy-MM-ddTHH:mm:ss}</FirstCreationTime>");
            sb.AppendLine("    <Documents>");

            foreach (var doc in entry.Documents)
            {
                sb.AppendLine($"      <Document type=\"{doc.DocumentType}\">");
                foreach (var alias in doc.Aliases)
                {
                    sb.AppendLine("        <Alias>");
                    sb.AppendLine($"          <Name>{alias.Name}</Name>");
                    sb.AppendLine($"          <CreationTime>{alias.CreationTime:yyyy-MM-ddTHH:mm:ss}</CreationTime>");
                    sb.AppendLine($"          <AliasType>{alias.AliasType}</AliasType>");
                    sb.AppendLine("        </Alias>");
                }
                sb.AppendLine("      </Document>");
            }

            sb.AppendLine("    </Documents>");
            sb.AppendLine("  </User>");
        }

        sb.AppendLine("</UserList>");
        return sb.ToString();
    }

    public string BuildXmlFile(string folder, string prefix)
    {
        var xmlPath = Path.Combine(folder, $"{prefix}_users.xml");
        File.WriteAllText(xmlPath, BuildXml(), Encoding.UTF8);
        return xmlPath;
    }

    public string BuildZipFile(string folder, string prefix)
    {
        var xmlPath = BuildXmlFile(folder, prefix);
        var zipPath = Path.Combine(folder, $"{prefix}_list.zip");

        using (var zipStream = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(xmlPath, Path.GetFileName(xmlPath));
        }

        File.Delete(xmlPath);
        return zipPath;
    }

    private FakeGibUserEntry GetOrCreateEntry(string identifier, string title, string accountType, string type, DateTime firstCreationTime)
    {
        var existing = _entries.Find(e => e.Identifier == identifier);
        if (existing is not null) return existing;

        var entry = new FakeGibUserEntry(identifier, title, accountType, type, firstCreationTime);
        _entries.Add(entry);
        return entry;
    }

    private static string EscapeXml(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}

public sealed class FakeGibUserEntry(string Identifier, string Title, string AccountType, string Type, DateTime FirstCreationTime)
{
    public string Identifier { get; } = Identifier;
    public string Title { get; } = Title;
    public string AccountType { get; } = AccountType;
    public string Type { get; } = Type;
    public DateTime FirstCreationTime { get; } = FirstCreationTime;
    public List<FakeDocument> Documents { get; } = [];
}

public sealed record FakeDocument(string DocumentType, List<FakeAlias> Aliases);
public sealed record FakeAlias(string Name, string AliasType, DateTime CreationTime);

/// <summary>
/// Kontrollü veri üreten sahte GIB indirici.
/// PK ve GB XML builder'larını ayrı ayrı ayarla, sonra sync service'e ver.
/// Her çağrıda builder'ların o anki durumundaki veriyi ZIP olarak üretir.
/// </summary>
public sealed class ConfigurableGibDownloader : IGibListDownloader
{
    public FakeGibXmlBuilder PkBuilder { get; set; } = new();
    public FakeGibXmlBuilder GbBuilder { get; set; } = new();

    public Task DownloadPkListAsync(string outputPath, CancellationToken cancellationToken)
    {
        var folder = Path.GetDirectoryName(outputPath)!;
        var zipPath = PkBuilder.BuildZipFile(folder, "pk_fake");
        File.Move(zipPath, outputPath, overwrite: true);
        return Task.CompletedTask;
    }

    public Task DownloadGbListAsync(string outputPath, CancellationToken cancellationToken)
    {
        var folder = Path.GetDirectoryName(outputPath)!;
        var zipPath = GbBuilder.BuildZipFile(folder, "gb_fake");
        File.Move(zipPath, outputPath, overwrite: true);
        return Task.CompletedTask;
    }
}
