using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MERSEL.Services.GibUserList.Domain.Entities;

namespace MERSEL.Services.GibUserList.Infrastructure.Data.Configurations;

public sealed class ArchiveFileConfiguration : IEntityTypeConfiguration<ArchiveFile>
{
    public void Configure(EntityTypeBuilder<ArchiveFile> builder)
    {
        builder.ToTable("archive_files");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.DocumentType)
            .IsRequired();

        builder.Property(e => e.FileName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.SizeBytes)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.UserCount)
            .IsRequired();

        builder.HasIndex(e => e.FileName)
            .IsUnique();

        builder.HasIndex(e => new { e.DocumentType, e.CreatedAt })
            .IsDescending(false, true);
    }
}
