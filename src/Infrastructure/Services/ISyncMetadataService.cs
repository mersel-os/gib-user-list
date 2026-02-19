using Npgsql;

namespace MERSEL.Services.GibUserList.Infrastructure.Services;

public interface ISyncMetadataService
{
    Task UpdateInTransactionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TimeSpan duration,
        string status,
        string? error,
        DateTime? failureAt,
        DateTime attemptAt,
        CancellationToken cancellationToken);

    Task UpdateStatusOnlyAsync(
        string status,
        string? error,
        DateTime attemptAt,
        CancellationToken ct);

    Task TryUpdateFailureStatusAsync(
        DateTime attemptAt,
        Exception ex,
        CancellationToken ct);
}
