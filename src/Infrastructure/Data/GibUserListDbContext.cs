using Microsoft.EntityFrameworkCore;
using MERSEL.Services.GibUserList.Application.Interfaces;
using MERSEL.Services.GibUserList.Domain.Entities;

namespace MERSEL.Services.GibUserList.Infrastructure.Data;

public sealed class GibUserListDbContext(DbContextOptions<GibUserListDbContext> options)
    : DbContext(options), IGibUserListReadDbContext
{
    public DbSet<EInvoiceGibUser> EInvoiceGibUsers => Set<EInvoiceGibUser>();
    public DbSet<EDespatchGibUser> EDespatchGibUsers => Set<EDespatchGibUser>();
    public DbSet<SyncMetadata> SyncMetadata => Set<SyncMetadata>();
    public DbSet<GibUserChangeLog> GibUserChangeLogs => Set<GibUserChangeLog>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Tüm DateTime property'leri PostgreSQL'de "timestamp without time zone" olarak saklanır.
        // UTC dönüşümü yapılmaz; sunucu yerel saati (DateTime.Now) olduğu gibi yazılır/okunur.
        configurationBuilder.Properties<DateTime>()
            .HaveColumnType("timestamp without time zone");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GibUserListDbContext).Assembly);
    }
}
