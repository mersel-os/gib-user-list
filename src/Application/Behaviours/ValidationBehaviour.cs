using FluentValidation;
using Mediator;

namespace MERSEL.Services.GibUserList.Application.Behaviours;

/// <summary>
/// Mediator pipeline behaviour: FluentValidation ile istek doğrulama.
/// Tüm IMessage'lar için kayıtlı validator varsa otomatik çalıştırır.
/// Doğrulama hatası varsa ValidationException fırlatır.
/// </summary>
public sealed class ValidationBehaviour<TMessage, TResponse>(
    IEnumerable<IValidator<TMessage>> validators)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!validators.Any())
            return await next(message, cancellationToken);

        var context = new ValidationContext<TMessage>(message);

        var validationResults = await Task.WhenAll(
            validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = validationResults
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count > 0)
            throw new ValidationException(failures);

        return await next(message, cancellationToken);
    }
}
