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

    /// <summary>
    /// Regression guard for a startup crash that no previous test covered: the
    /// decorators themselves implement ICommandHandler&lt;,&gt;/IQueryHandler&lt;,&gt; and
    /// expose a matching Handle method, so the assembly scan in
    /// AddVerticalSliceHandlers picked them up as *inner handlers*. Because their
    /// interface is an open generic there, Wire registered an open-generic service
    /// type against a non-matching implementation, and BuildServiceProvider threw
    /// "Open generic service type ... requires registering an open generic
    /// implementation type" — which killed the host during
    /// WebApplicationBuilder.Build() before it ever listened on a port.
    ///
    /// The hand-wired tests above never exercised the scan, so the suite was green
    /// while the application could not boot at all.
    /// </summary>
    [Fact]
    public void Scanning_the_real_Assembly_builds_a_provider_and_resolves_handlers()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        // Exactly what Program.cs does.
        services.AddVerticalSliceHandlers(
            typeof(ICommandHandler<,>).Assembly,
            new DecoratorRegistration
            {
                typeof(CommandValidationDecorator<,>),
                typeof(CommandLoggingDecorator<,>),
            },
            new DecoratorRegistration
            {
                typeof(QueryValidationDecorator<,>),
                typeof(QueryLoggingDecorator<,>),
            });

        // This is the call that threw ArgumentException before the fix. It throws
        // regardless of validation options, so a plain build is enough here. Note
        // that ValidateOnBuild/ValidateScopes are deliberately NOT used: the real
        // handlers depend on Redis/SignalR adapters this isolated unit test does
        // not register, so validating the whole graph belongs in ApiTests
        // (HostSmokeTests), which boots the actual Program.cs composition.
        Action act = () => services.BuildServiceProvider();
        act.Should().NotThrow();

        using var provider = services.BuildServiceProvider();
        provider.Should().NotBeNull();

        // The scan must still wire the genuine (closed) handlers.
        var handlers = typeof(ICommandHandler<,>).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && !t.ContainsGenericParameters)
            .SelectMany(t => t.GetInterfaces())
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))
            .Distinct()
            .ToList();

        handlers.Should().NotBeEmpty(
            "the Application assembly is expected to contain concrete command handlers");

        var registeredServices = services.Select(d => d.ServiceType).ToHashSet();
        foreach (var service in handlers)
        {
            registeredServices.Should().Contain(service,
                $"handler {service.Name} must be registered through the decorator chain");
        }

        // The open-generic decorators themselves must never be scanned as handlers.
        registeredServices.Should().NotContain(typeof(ICommandHandler<,>),
            "the scan must skip open-generic decorator types");
        registeredServices.Should().NotContain(typeof(IQueryHandler<,>),
            "the scan must skip open-generic decorator types");
    }

    /// <summary>
    /// The open-generic decorators must be closable per handler but must never
    /// appear as registered *service* types, since DI cannot satisfy an open
    /// generic registration without a matching open generic implementation.
    /// </summary>
    [Fact]
    public void No_open_generic_service_types_are_registered()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        // Only the descriptors the scan itself contributes are of interest — the
        // ILogger<> registration above is a legitimate open-generic pairing.
        var before = services.Count;

        services.AddVerticalSliceHandlers(
            typeof(ICommandHandler<,>).Assembly,
            new DecoratorRegistration { typeof(CommandLoggingDecorator<,>) },
            new DecoratorRegistration { typeof(QueryLoggingDecorator<,>) });

        var offenders = services.Skip(before)
            .Where(d => d.ServiceType.ContainsGenericParameters)
            .Select(d => d.ServiceType.FullName)
            .ToList();

        offenders.Should().BeEmpty(
            "AddVerticalSliceHandlers must only register closed-generic service types");
    }
}
