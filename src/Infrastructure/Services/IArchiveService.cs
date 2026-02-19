namespace MERSEL.Services.GibUserList.Infrastructure.Services;

public interface IArchiveService
{
    Task<List<string>> GenerateDocumentTypeArchivesAsync(CancellationToken ct);

    Task<string?> CleanupOldArchivesAsync(CancellationToken ct);
}
