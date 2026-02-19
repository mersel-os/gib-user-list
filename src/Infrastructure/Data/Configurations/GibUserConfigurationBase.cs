using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MERSEL.Services.GibUserList.Domain.Entities;

namespace MERSEL.Services.GibUserList.Infrastructure.Data.Configurations;

/// <summary>
/// GibUser varlıkları için temel yapılandırma.
/// Kesintisiz sorgular için materialized view'lara eşlenir.
/// Job ham SQL ile ana tablolara yazar; DbSet MV'lerden okur.
/// </summary>
public abstract class GibUserConfigurationBase<T> : IEntityTypeConfiguration<T> where T : GibUser
{
    protected abstract string ViewName { get; }

    public virtual void Configure(EntityTypeBuilder<T> builder)
    {
        builder.ToView(ViewName);

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Identifier)
            .IsRequired()
            .HasMaxLength(11);

        builder.Property(e => e.Title)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.TitleLower)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.AccountType)
            .HasMaxLength(50);

        builder.Property(e => e.Type)
            .HasMaxLength(50);

        builder.Property(e => e.FirstCreationTime)
            .IsRequired();

        builder.Property(e => e.AliasesJson)
            .HasColumnType("jsonb")
            .HasColumnName("aliases_json");

        builder.Property(e => e.ContentHash)
            .HasMaxLength(32)
            .IsFixedLength();
    }
}
