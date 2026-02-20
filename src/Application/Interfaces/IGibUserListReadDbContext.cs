using Microsoft.EntityFrameworkCore;
using MERSEL.Services.GibUserList.Domain.Entities;

namespace MERSEL.Services.GibUserList.Application.Interfaces;

/// <summary>
/// GIB mükellef verilerini sorgulamak için salt okunur veritabanı bağlamı.
/// Kesintisiz erişim için materialized view'lerden okur.
/// </summary>
public interface IGibUserListReadDbContext
{
    DbSet<EInvoiceGibUser> EInvoiceGibUsers { get; }
    DbSet<EDespatchGibUser> EDespatchGibUsers { get; }
    DbSet<SyncMetadata> SyncMetadata { get; }
    DbSet<GibUserChangeLog> GibUserChangeLogs { get; }
    DbSet<ArchiveFile> ArchiveFiles { get; }
}
