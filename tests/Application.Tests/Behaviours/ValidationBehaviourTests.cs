using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Mediator;
using NSubstitute;
using MERSEL.Services.GibUserList.Application.Behaviours;

namespace MERSEL.Services.GibUserList.Application.Tests.Behaviours;

// NSubstitute proxy erişimi için test mesaj tipi public olmalıdır
public sealed record TestQuery(string Value) : IQuery<string>;

public class ValidationBehaviourTests
{
    [Fact]
    public async Task Handle_WithNoValidators_ShouldPassThrough()
    {
        var validators = Array.Empty<IValidator<TestQuery>>();
        var behaviour = new ValidationBehaviour<TestQuery, string>(validators);
        var query = new TestQuery("test");

        var result = await behaviour.Handle(
            query,
            (msg, ct) => new ValueTask<string>("sonuç"),
            CancellationToken.None);

        result.Should().Be("sonuç");
    }

    [Fact]
    public async Task Handle_WithPassingValidator_ShouldPassThrough()
    {
        var validator = Substitute.For<IValidator<TestQuery>>();
        validator.ValidateAsync(Arg.Any<ValidationContext<TestQuery>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());

        var behaviour = new ValidationBehaviour<TestQuery, string>([validator]);
        var query = new TestQuery("test");

        var result = await behaviour.Handle(
            query,
            (msg, ct) => new ValueTask<string>("sonuç"),
            CancellationToken.None);

        result.Should().Be("sonuç");
    }

    [Fact]
    public async Task Handle_WithFailingValidator_ShouldThrowValidationException()
    {
        var validator = Substitute.For<IValidator<TestQuery>>();
        var failures = new List<ValidationFailure>
        {
            new("Value", "Değer boş olamaz."),
            new("Value", "Değer en az 3 karakter olmalıdır.")
        };
        validator.ValidateAsync(Arg.Any<ValidationContext<TestQuery>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult(failures));

        var behaviour = new ValidationBehaviour<TestQuery, string>([validator]);
        var query = new TestQuery("");

        var act = () => behaviour.Handle(
            query,
            (msg, ct) => new ValueTask<string>("ulaşılmamalı"),
            CancellationToken.None).AsTask();

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_WithMultipleValidators_ShouldAggregateErrors()
    {
        var validator1 = Substitute.For<IValidator<TestQuery>>();
        validator1.ValidateAsync(Arg.Any<ValidationContext<TestQuery>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult([new ValidationFailure("Value", "Hata 1")]));

        var validator2 = Substitute.For<IValidator<TestQuery>>();
        validator2.ValidateAsync(Arg.Any<ValidationContext<TestQuery>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult([new ValidationFailure("Value", "Hata 2")]));

        var behaviour = new ValidationBehaviour<TestQuery, string>([validator1, validator2]);
        var query = new TestQuery("");

        var act = () => behaviour.Handle(
            query,
            (msg, ct) => new ValueTask<string>("ulaşılmamalı"),
            CancellationToken.None).AsTask();

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_WithFailingValidator_ShouldNotCallNext()
    {
        var nextCalled = false;
        var validator = Substitute.For<IValidator<TestQuery>>();
        validator.ValidateAsync(Arg.Any<ValidationContext<TestQuery>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult([new ValidationFailure("Value", "Hata")]));

        var behaviour = new ValidationBehaviour<TestQuery, string>([validator]);
        var query = new TestQuery("");

        try
        {
            await behaviour.Handle(
                query,
                (msg, ct) =>
                {
                    nextCalled = true;
                    return new ValueTask<string>("sonuç");
                },
                CancellationToken.None);
        }
        catch (ValidationException)
        {
            // Beklenen exception
        }

        nextCalled.Should().BeFalse();
    }
}
