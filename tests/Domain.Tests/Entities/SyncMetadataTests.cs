using FluentAssertions;
using MERSEL.Services.GibUserList.Domain.Entities;

namespace MERSEL.Services.GibUserList.Domain.Tests.Entities;

public class SyncMetadataTests
{
    [Fact]
    public void SingletonKey_ShouldHaveExpectedValue()
    {
        SyncMetadata.SingletonKey.Should().Be("gib-gibuser-sync");
    }

    [Fact]
    public void NewSyncMetadata_ShouldHaveDefaultKey()
    {
        var metadata = new SyncMetadata();

        metadata.Key.Should().Be(SyncMetadata.SingletonKey);
    }

    [Fact]
    public void SyncMetadata_ShouldStoreAllProperties()
    {
        var syncTime = DateTime.UtcNow;
        var duration = TimeSpan.FromMinutes(5);

        var metadata = new SyncMetadata
        {
            LastSyncAt = syncTime,
            EInvoiceUserCount = 150_000,
            EDespatchUserCount = 80_000,
            LastSyncDuration = duration
        };

        metadata.LastSyncAt.Should().Be(syncTime);
        metadata.EInvoiceUserCount.Should().Be(150_000);
        metadata.EDespatchUserCount.Should().Be(80_000);
        metadata.LastSyncDuration.Should().Be(duration);
    }
}
