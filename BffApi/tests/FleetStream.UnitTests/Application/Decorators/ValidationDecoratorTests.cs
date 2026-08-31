using FleetStream.Application.Shared.Decorators;
using FleetStream.Application.Shared.Messaging;
using FluentAssertions;
using FluentValidation;
using NSubstitute;

namespace FleetStream.UnitTests.Application.Decorators;

public class CommandValidationDecoratorTests
{
    public sealed record Cmd(string Name);

    public sealed class FailingValidator : AbstractValidator<Cmd>
    {
        public FailingValidator() => RuleFor(c => c.Name).NotEmpty();
    }

    private static ICommandHandler<Cmd, int> InnerReturning(int value)
    {
        var sub = Substitute.For<ICommandHandler<Cmd, int>>();
        sub.Handle(Arg.Any<Cmd>(), Arg.Any<CancellationToken>()).Returns(value);
        return sub;
    }

    [Fact]
    public async Task Does_not_call_inner_when_validation_fails()
    {
        var inner = InnerReturning(0);
        var sut = new CommandValidationDecorator<Cmd, int>(
            () => inner,
            new IValidator<Cmd>[] { new FailingValidator() });

        var act = () => sut.Handle(new Cmd(string.Empty), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        await inner.DidNotReceive().Handle(Arg.Any<Cmd>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Calls_inner_when_no_validators_registered()
    {
        var inner = InnerReturning(42);
        var sut = new CommandValidationDecorator<Cmd, int>(() => inner, Array.Empty<IValidator<Cmd>>());

        var result = await sut.Handle(new Cmd("anything"), CancellationToken.None);

        await inner.Received(1).Handle(Arg.Any<Cmd>(), Arg.Any<CancellationToken>());
        result.Should().Be(42);
    }

    [Fact]
    public async Task Calls_inner_when_validation_passes()
    {
        var inner = InnerReturning(7);
        var sut = new CommandValidationDecorator<Cmd, int>(
            () => inner,
            new IValidator<Cmd>[] { new FailingValidator() });

        var result = await sut.Handle(new Cmd("ok"), CancellationToken.None);

        await inner.Received(1).Handle(Arg.Any<Cmd>(), Arg.Any<CancellationToken>());
        result.Should().Be(7);
    }
}

public class QueryValidationDecoratorTests
{
    public sealed record Q(string Term);
    public sealed class QValidator : AbstractValidator<Q>
    {
        public QValidator() => RuleFor(q => q.Term).MinimumLength(3);
    }

    [Fact]
    public async Task Throws_when_invalid()
    {
        var inner = Substitute.For<IQueryHandler<Q, int>>();
        var sut = new QueryValidationDecorator<Q, int>(() => inner, new IValidator<Q>[] { new QValidator() });

        var act = () => sut.Handle(new Q("a"), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        await inner.DidNotReceive().Handle(Arg.Any<Q>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Passes_when_valid()
    {
        var inner = Substitute.For<IQueryHandler<Q, int>>();
        inner.Handle(Arg.Any<Q>(), Arg.Any<CancellationToken>()).Returns(99);

        var qd = new QueryValidationDecorator<Q, int>(() => inner, new IValidator<Q>[] { new QValidator() });
        var result = await qd.Handle(new Q("hello"), CancellationToken.None);

        result.Should().Be(99);
    }
}