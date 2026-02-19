using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MERSEL.Services.GibUserList.Domain.Entities;

namespace MERSEL.Services.GibUserList.Infrastructure.Data.Configurations;

/// <summary>
/// GibUserChangeLog entity'si için EF Core tablo yapılandırması.
/// Changelog tablosu raw SQL ile yazılır; bu yapılandırma sadece okumalar içindir.
/// </summary>
public sealed class GibUserChangeLogConfiguration : IEntityTypeConfiguration<GibUserChangeLog>
{
    public void Configure(EntityTypeBuilder<GibUserChangeLog> builder)
    {
        builder.ToTable("gib_user_changelog");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.DocumentType)
            .IsRequired();

        builder.Property(e => e.Identifier)
            .IsRequired()
            .HasMaxLength(11);

        builder.Property(e => e.ChangeType)
            .IsRequired();

        builder.Property(e => e.ChangedAt)
            .IsRequired();

        builder.Property(e => e.Title)
            .HasMaxLength(500);

        builder.Property(e => e.AccountType)
            .HasMaxLength(50);

        builder.Property(e => e.Type)
            .HasMaxLength(50);

        builder.Property(e => e.AliasesJson)
            .HasColumnType("jsonb");

        builder.HasIndex(e => new { e.DocumentType, e.ChangedAt });
    }
}
