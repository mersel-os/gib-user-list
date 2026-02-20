using Microsoft.EntityFrameworkCore;
using MERSEL.Services.GibUserList.Application.Interfaces;
using MERSEL.Services.GibUserList.Domain.Entities;

namespace MERSEL.Services.GibUserList.Application.Tests.Helpers;

/// <summary>
/// Sorgu handler testlerinde kullanılmak üzere InMemory EF Core DbContext.
/// IGibUserListReadDbContext arayüzünü uygular.
/// </summary>
internal class TestGibUserListDbContext : DbContext, IGibUserListReadDbContext
{
    public TestGibUserListDbContext(DbContextOptions<TestGibUserListDbContext> options)
        : base(options) { }

    public DbSet<EInvoiceGibUser> EInvoiceGibUsers => Set<EInvoiceGibUser>();
    public DbSet<EDespatchGibUser> EDespatchGibUsers => Set<EDespatchGibUser>();
    public DbSet<SyncMetadata> SyncMetadata => Set<SyncMetadata>();
    public DbSet<GibUserChangeLog> GibUserChangeLogs => Set<GibUserChangeLog>();
    public DbSet<ArchiveFile> ArchiveFiles => Set<ArchiveFile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EInvoiceGibUser>(b => b.HasKey(e => e.Id));
        modelBuilder.Entity<EDespatchGibUser>(b => b.HasKey(e => e.Id));
        modelBuilder.Entity<SyncMetadata>(b => b.HasKey(e => e.Key));
        modelBuilder.Entity<GibUserChangeLog>(b => b.HasKey(e => e.Id));
        modelBuilder.Entity<ArchiveFile>(b => b.HasKey(e => e.Id));
    }

    /// <summary>
    /// Test verileri ile doldurulmuş yeni bir InMemory context oluşturur.
    /// </summary>
    public static TestGibUserListDbContext Create(
        List<EInvoiceGibUser>? eInvoiceUsers = null,
        List<EDespatchGibUser>? eDespatchUsers = null,
        List<SyncMetadata>? syncMetadata = null)
    {
        var options = new DbContextOptionsBuilder<TestGibUserListDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new TestGibUserListDbContext(options);
        context.Database.EnsureCreated();

        if (eInvoiceUsers is not null)
            context.EInvoiceGibUsers.AddRange(eInvoiceUsers);

        if (eDespatchUsers is not null)
            context.EDespatchGibUsers.AddRange(eDespatchUsers);

        if (syncMetadata is not null)
            context.SyncMetadata.AddRange(syncMetadata);

        context.SaveChanges();
        context.ChangeTracker.Clear();

        return context;
    }

    /// <summary>
    /// Changelog test verileri ile doldurulmuş yeni bir InMemory context oluşturur.
    /// </summary>
    public static TestGibUserListDbContext CreateWithChangeLogs(List<GibUserChangeLog> changeLogs)
    {
        var options = new DbContextOptionsBuilder<TestGibUserListDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new TestGibUserListDbContext(options);
        context.Database.EnsureCreated();

        context.GibUserChangeLogs.AddRange(changeLogs);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        return context;
    }
}
