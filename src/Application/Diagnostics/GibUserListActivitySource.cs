using System.Diagnostics;

namespace MERSEL.Services.GibUserList.Application.Queries;

/// <summary>
/// Application katmanı için dağıtık izleme (distributed tracing) ActivitySource.
/// Sorgu handler'ları bu kaynak üzerinden Activity (span) oluşturur.
/// </summary>
public static class GibUserListActivitySource
{
    public const string Name = "MERSEL.Services.GibUserList";

    public static readonly ActivitySource Source = new(Name);
}
