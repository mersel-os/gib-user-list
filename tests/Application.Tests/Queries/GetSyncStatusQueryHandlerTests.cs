using FluentAssertions;
using MERSEL.Services.GibUserList.Application.Queries;
using MERSEL.Services.GibUserList.Application.Tests.Helpers;
using MERSEL.Services.GibUserList.Domain.Entities;

namespace MERSEL.Services.GibUserList.Application.Tests.Queries;

public class GetSyncStatusQueryHandlerTests
{
    [Fact]
    public async Task Handle_WithExistingMetadata_ShouldReturnAllFields()
    {
        var syncTime = DateTime.Now.AddHours(-1);
        var metadata = new SyncMetadata
        {
            Key = SyncMetadata.SingletonKey,
            LastSyncAt = syncTime,
            EInvoiceUserCount = 150_000,
            EDespatchUserCount = 80_000,
            LastSyncDuration = TimeSpan.FromMinutes(3),
            LastSyncStatus = SyncRunStatus.Partial,
            LastSyncError = "archive generation timeout",
            LastAttemptAt = syncTime.AddMinutes(1),
            LastFailureAt = syncTime.AddMinutes(1)
        };

        using var dbContext = TestGibUserListDbContext.Create(syncMetadata: [metadata]);
        var handler = new GetSyncStatusQueryHandler(dbContext);

        var result = await handler.Handle(new GetSyncStatusQuery(), CancellationToken.None);

        result.LastSyncAt.Should().Be(syncTime);
        result.EInvoiceUserCount.Should().Be(150_000);
        result.EDespatchUserCount.Should().Be(80_000);
        result.LastSyncDuration.Should().Be(TimeSpan.FromMinutes(3));
        result.LastSyncStatus.Should().Be(SyncRunStatus.Partial);
        result.LastSyncError.Should().Be("archive generation timeout");
        result.LastAttemptAt.Should().Be(syncTime.AddMinutes(1));
        result.LastFailureAt.Should().Be(syncTime.AddMinutes(1));
    }

    [Fact]
    public async Task Handle_WithNoMetadata_ShouldReturnDefaults()
    {
        using var dbContext = TestGibUserListDbContext.Create();
        var handler = new GetSyncStatusQueryHandler(dbContext);

        var result = await handler.Handle(new GetSyncStatusQuery(), CancellationToken.None);

        result.LastSyncAt.Should().BeNull();
        result.EInvoiceUserCount.Should().Be(0);
        result.EDespatchUserCount.Should().Be(0);
        result.LastSyncDuration.Should().BeNull();
        result.LastSyncStatus.Should().Be(SyncRunStatus.Success);
        result.LastSyncError.Should().BeNull();
        result.LastAttemptAt.Should().BeNull();
        result.LastFailureAt.Should().BeNull();
    }
}
