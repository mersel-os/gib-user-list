using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MERSEL.Services.GibUserList.Application.Interfaces;

namespace MERSEL.Services.GibUserList.Infrastructure.Services;

/// <summary>
/// Yerel dosya sistemi / mount noktası üzerinde arşiv depolama.
/// Docker volume veya NFS mount ile kalıcı hale getirilebilir.
/// </summary>
public sealed class FileSystemArchiveStorage(
    IOptions<ArchiveStorageOptions> options,
    ILogger<FileSystemArchiveStorage> logger) : IArchiveStorage
{
    private readonly string _basePath = options.Value.BasePath;

    public async Task SaveAsync(string fileName, Stream content, CancellationToken ct)
    {
        var filePath = GetSafePath(fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        await content.CopyToAsync(fileStream, ct);

        logger.LogInformation("Arşiv dosyası kaydedildi: {FilePath}", filePath);
    }

    public Task<Stream?> GetAsync(string fileName, CancellationToken ct)
    {
        var filePath = GetSafePath(fileName);
        if (!File.Exists(filePath))
            return Task.FromResult<Stream?>(null);

        Stream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return Task.FromResult<Stream?>(stream);
    }

    public async Task<IReadOnlyList<ArchiveFileInfo>> ListAsync(string? prefix = null, CancellationToken ct = default)
    {
        if (!Directory.Exists(_basePath))
            return [];

        return await Task.Run(() =>
            Directory.EnumerateFiles(_basePath, "*.*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".xml.gz", StringComparison.OrdinalIgnoreCase)
                            || path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .Select(path =>
                {
                    var info = new FileInfo(path);
                    var relativeName = Path.GetRelativePath(_basePath, path).Replace('\\', '/');
                    return new ArchiveFileInfo(relativeName, info.Length, info.CreationTime);
                })
                .Where(f => prefix is null || f.FileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.CreatedAt)
                .ToList(), ct);
    }

    public Task DeleteAsync(string fileName, CancellationToken ct)
    {
        var filePath = GetSafePath(fileName);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            logger.LogInformation("Arşiv dosyası silindi: {FilePath}", filePath);
        }
        return Task.CompletedTask;
    }

    private string GetSafePath(string fileName)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_basePath, fileName));
        var baseFull = Path.GetFullPath(_basePath);
        if (!fullPath.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Geçersiz dosya yolu — dizin dışı erişim engellendi.");
        return fullPath;
    }
}
