namespace MERSEL.Services.GibUserList.Application.Interfaces;

/// <summary>
/// GIB mükellef listesi senkronizasyon sürecini yönetir:
/// indirme -> parse -> toplu ekleme -> birleştirme -> materialized view'lerin yenilenmesi.
/// </summary>
public interface IGibUserSyncService
{
    /// <summary>
    /// Veritabanı şemasının hazır olduğunu sağlar (geçici tablolar, materialized view'ler).
    /// İdempotent - her başlatmada güvenle çağrılabilir.
    /// </summary>
    Task EnsureDatabaseSchemaAsync(CancellationToken cancellationToken);

    /// <summary>
    /// GIB'den PK ve GB listelerini indirir, XML'i parse eder, geçici tablolara toplu ekleme yapar,
    /// ana tablolarla birleştirir ve materialized view'leri yeniler.
    /// </summary>
    Task SyncGibUserListsAsync(CancellationToken cancellationToken);
}
