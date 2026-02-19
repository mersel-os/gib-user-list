namespace MERSEL.Services.GibUserList.Web.Infrastructure;

/// <summary>
/// HMAC kimlik doğrulama yapılandırma seçenekleri.
/// </summary>
public sealed class AuthenticationOptions
{
    public const string SectionName = "Authentication";

    public bool Enabled { get; set; }

    public int TimestampToleranceSeconds { get; set; } = 300;

    public List<HmacClientConfig> Clients { get; set; } = [];
}
