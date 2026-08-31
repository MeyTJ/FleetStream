using FleetStream.Application.Shared.Decorators;
using FleetStream.Application.Shared.Messaging;
using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FleetStream.UnitTests.Application.Decorators;

public class DecoratorWiringSmokeTest
{
    public sealed record Cmd(int Value);
    public sealed record Res(int Value);

    public sealed class Inner : ICommandHandler<Cmd, Res>
    {
        public Task<Res> Handle(Cmd command, CancellationToken cancellationToken) =>
            Task.FromResult(new Res(command.Value * 2));
    }

    [Fact]
    public async Task Single_decorator_chain_resolves_and_runs()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddScoped<Inner>();

        services.AddScoped<CommandLoggingDecorator<Cmd, Res>>(sp =>
            new CommandLoggingDecorator<Cmd, Res>(
                () => sp.GetRequiredService<Inner>(),
                sp.GetRequiredService<ILogger<CommandLoggingDecorator<Cmd, Res>>>()));

        services.AddScoped<ICommandHandler<Cmd, Res>>(sp =>
            sp.GetRequiredService<CommandLoggingDecorator<Cmd, Res>>());

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<Cmd, Res>>();
        var result = await handler.Handle(new Cmd(21), CancellationToken.None);

        result.Value.Should().Be(42);
    }

    [Fact]
    public async Task Validation_then_logging_chain_does_not_recurse()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddScoped<Inner>();

        services.AddScoped<CommandLoggingDecorator<Cmd, Res>>(sp =>
            new CommandLoggingDecorator<Cmd, Res>(
                () => sp.GetRequiredService<Inner>(),
                sp.GetRequiredService<ILogger<CommandLoggingDecorator<Cmd, Res>>>()));

        services.AddScoped<CommandValidationDecorator<Cmd, Res>>(sp =>
            new CommandValidationDecorator<Cmd, Res>(
                () => sp.GetRequiredService<CommandLoggingDecorator<Cmd, Res>>(),
                Array.Empty<IValidator<Cmd>>()));

        services.AddScoped<ICommandHandler<Cmd, Res>>(sp =>
            sp.GetRequiredService<CommandValidationDecorator<Cmd, Res>>());

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<Cmd, Res>>();
        var result = await handler.Handle(new Cmd(10), CancellationToken.None);

        result.Value.Should().Be(20);
    }
}
