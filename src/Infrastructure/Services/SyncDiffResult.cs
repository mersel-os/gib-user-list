using MERSEL.Services.GibUserList.Domain.Entities;

namespace MERSEL.Services.GibUserList.Infrastructure.Services;

public sealed record SyncDiffResult(
    GibDocumentType DocumentType,
    string CacheKeyPrefix,
    List<string> ModifiedIdentifiers,
    List<string> RemovedIdentifiers)
{
    public int AddedCount { get; init; }
    public int ModifiedCount { get; init; }
    public int RemovedCount { get; init; }
    public int InvalidationCount => ModifiedIdentifiers.Count + RemovedIdentifiers.Count;
}
