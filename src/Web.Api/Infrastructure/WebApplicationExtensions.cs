using System.Reflection;

namespace MERSEL.Services.GibUserList.Web.Infrastructure;

public static class WebApplicationExtensions
{
    public static WebApplication MapEndpoints(this WebApplication app)
    {
        var endpointGroupType = typeof(EndpointGroupBase);

        var endpointGroups = Assembly.GetExecutingAssembly()
            .GetExportedTypes()
            .Where(t => t.IsSubclassOf(endpointGroupType) && !t.IsAbstract);

        foreach (var type in endpointGroups)
        {
            if (Activator.CreateInstance(type) is EndpointGroupBase instance)
            {
                instance.Map(app);
            }
        }

        return app;
    }
}
