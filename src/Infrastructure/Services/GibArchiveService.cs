using System.IO.Compression;
using System.Text;
using System.Xml;
using MERSEL.Services.GibUserList.Application.Interfaces;
using MERSEL.Services.GibUserList.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace MERSEL.Services.GibUserList.Infrastructure.Services;

public sealed class GibArchiveService(
    GibUserListDbContext dbContext,
    IArchiveStorage archiveStorage,
    IOptions<ArchiveStorageOptions> archiveOptions,
    ILogger<GibArchiveService> logger) : IArchiveService
{
    private static readonly HashSet<string> AllowedTableNames = ["e_invoice_gib_users", "e_despatch_gib_users"];

    public async Task<List<string>> GenerateDocumentTypeArchivesAsync(CancellationToken ct)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var errors = new List<string>();

        var invoiceError = await GenerateArchiveForTableAsync(
            "e_invoice_gib_users",
            $"einvoice/einvoice_users_{timestamp}.xml.gz",
            "Invoice",
            ct);
        if (!string.IsNullOrEmpty(invoiceError))
            errors.Add(invoiceError);

        var despatchError = await GenerateArchiveForTableAsync(
            "e_despatch_gib_users",
            $"edespatch/edespatch_users_{timestamp}.xml.gz",
            "DespatchAdvice",
            ct);
        if (!string.IsNullOrEmpty(despatchError))
            errors.Add(despatchError);

        return errors;
    }

    public async Task<string?> CleanupOldArchivesAsync(CancellationToken ct)
    {
        try
        {
            var retentionDays = archiveOptions.Value.RetentionDays;
            var cutoff = DateTime.Now.AddDays(-retentionDays);
            var files = await archiveStorage.ListAsync(ct: ct);

            foreach (var file in files.Where(f => f.CreatedAt < cutoff))
            {
                await archiveStorage.DeleteAsync(file.FileName, ct);
                logger.LogInformation("Eski arşiv dosyası silindi: {FileName}", file.FileName);
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Arşiv retention temizliği başarısız oldu.");
            return $"Archive retention cleanup failed: {ex.Message}";
        }
    }

    private async Task<string?> GenerateArchiveForTableAsync(
        string tableName,
        string archiveFileName,
        string documentType,
        CancellationToken ct)
    {
        if (!AllowedTableNames.Contains(tableName))
            throw new ArgumentException($"Invalid table: {tableName}");

        var tempFilePath = Path.Combine(Path.GetTempPath(), $"gib_archive_{Guid.NewGuid():N}.xml.gz");

        try
        {
            var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync(ct);

            long fileSize;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            await using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
            await using (var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal))
            await using (var writer = XmlWriter.Create(gzipStream, new XmlWriterSettings
            {
                Indent = false,
                Encoding = Encoding.UTF8,
                Async = true
            }))
            {
                await writer.WriteStartDocumentAsync();
                await writer.WriteStartElementAsync(null, "GibUserList", null);
                writer.WriteAttributeString("documentType", documentType);
                writer.WriteAttributeString("generatedAt", DateTime.Now.ToString("O"));

                await using (var countCmd = new NpgsqlCommand($"SELECT COUNT(*)::int FROM {tableName}", connection))
                {
                    var userCount = (int)(await countCmd.ExecuteScalarAsync(ct))!;
                    writer.WriteAttributeString("count", userCount.ToString());
                }

                await using var cmd = new NpgsqlCommand(
                    $"SELECT identifier, title, account_type, type, first_creation_time, aliases_json FROM {tableName} ORDER BY identifier",
                    connection);

                await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess, ct);

                while (await reader.ReadAsync(ct))
                {
                    await writer.WriteStartElementAsync(null, "User", null);

                    await writer.WriteElementStringAsync(null, "Identifier", null, reader.GetString(0));
                    await writer.WriteElementStringAsync(null, "Title", null, reader.GetString(1));
                    if (!reader.IsDBNull(2))
                        await writer.WriteElementStringAsync(null, "AccountType", null, reader.GetString(2));
                    if (!reader.IsDBNull(3))
                        await writer.WriteElementStringAsync(null, "Type", null, reader.GetString(3));
                    await writer.WriteElementStringAsync(null, "FirstCreationTime", null,
                        reader.GetDateTime(4).ToString("O"));

                    if (!reader.IsDBNull(5))
                    {
                        var aliasesJson = reader.GetString(5);
                        WriteAliasesFromJson(writer, aliasesJson);
                    }

                    await writer.WriteEndElementAsync();
                }

                await writer.WriteEndElementAsync();
                await writer.WriteEndDocumentAsync();
            }

            fileSize = new FileInfo(tempFilePath).Length;
            sw.Stop();

            await using (var uploadStream = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true))
            {
                await archiveStorage.SaveAsync(archiveFileName, uploadStream, ct);
            }

            logger.LogInformation(
                "Arşiv üretildi: {ArchiveFileName} ({Size:N0} bytes, {Duration})",
                archiveFileName, fileSize, sw.Elapsed);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Arşiv üretimi başarısız oldu: {TableName}", tableName);
            return $"Archive generation failed for {tableName}: {ex.Message}";
        }
        finally
        {
            try { if (File.Exists(tempFilePath)) File.Delete(tempFilePath); }
            catch { }
        }
    }

    private static void WriteAliasesFromJson(XmlWriter writer, string aliasesJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(aliasesJson);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return;

            writer.WriteStartElement("Aliases");
            foreach (var alias in doc.RootElement.EnumerateArray())
            {
                writer.WriteStartElement("Alias");
                if (alias.TryGetProperty("Alias", out var name))
                    writer.WriteElementString("Name", name.GetString());
                if (alias.TryGetProperty("Type", out var type))
                    writer.WriteElementString("Type", type.GetString());
                if (alias.TryGetProperty("CreationTime", out var creationTime))
                    writer.WriteElementString("CreationTime", creationTime.GetString());
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
        catch
        {
            // ignore malformed alias json
        }
    }
}
