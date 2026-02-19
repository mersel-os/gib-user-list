namespace MERSEL.Services.GibUserList.Domain.ValueObjects;

/// <summary>
/// GİB sisteminde mükellefin takma adını (posta kutusu) temsil eder.
/// Değiştirilemez value object.
/// </summary>
public sealed record GibUserAlias
{
    /// <summary>Takma ad adı (örn. urn:mail:defaultpk)</summary>
    public required string Name { get; init; }

    /// <summary>Takma ad tipi: PK (Posta Kutusu) veya GB (Gönderici Birim)</summary>
    public required string Type { get; init; }

    /// <summary>Takma adın GİB sisteminde oluşturulma zamanı (UTC)</summary>
    public required DateTime CreationTime { get; init; }
}
