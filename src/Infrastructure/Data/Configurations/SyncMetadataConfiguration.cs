using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MERSEL.Services.GibUserList.Domain.Entities;

namespace MERSEL.Services.GibUserList.Infrastructure.Data.Configurations;

public sealed class SyncMetadataConfiguration : IEntityTypeConfiguration<SyncMetadata>
{
    public void Configure(EntityTypeBuilder<SyncMetadata> builder)
    {
        builder.ToTable("sync_metadata");

        builder.HasKey(e => e.Key);

        builder.Property(e => e.Key)
            .HasMaxLength(50);

        builder.Property(e => e.LastSyncAt);

        builder.Property(e => e.EInvoiceUserCount)
            .IsRequired();

        builder.Property(e => e.EDespatchUserCount)
            .IsRequired();

        builder.Property(e => e.LastSyncDuration)
            .IsRequired();

        builder.Property(e => e.LastSyncStatus)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(e => e.LastSyncError)
            .HasMaxLength(2000);

        builder.Property(e => e.LastAttemptAt)
            .IsRequired();

        builder.Property(e => e.LastFailureAt);
    }
}
