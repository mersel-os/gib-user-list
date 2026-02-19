namespace MERSEL.Services.GibUserList.Web.Infrastructure;

/// <summary>
/// Minimal API uç noktalarını gruplamak için temel sınıf.
/// </summary>
public abstract class EndpointGroupBase
{
    public abstract void Map(WebApplication app);
}
