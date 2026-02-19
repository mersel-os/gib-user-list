using FluentValidation;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using MERSEL.Services.GibUserList.Application.Behaviours;

namespace MERSEL.Services.GibUserList.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Application katmanı servislerini register eder.
    /// Not: AddMediator() host projede (Web.Api / Worker.Updater) çağrılmalıdır
    /// çünkü source generator yalnızca orada çalışır.
    /// </summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // ValidationBehaviour pipeline - tüm mesajlar için FluentValidation çalıştırır
        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(ValidationBehaviour<,>));

        // FluentValidation - validator'ları otomatik register et
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        return services;
    }
}
