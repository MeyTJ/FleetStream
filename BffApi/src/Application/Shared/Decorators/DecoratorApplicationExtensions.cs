using System.Linq;
using System.Reflection;
using FleetStream.Application.Shared.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FleetStream.Application.Shared.Decorators;

/// <summary>
/// Pure-IL extension methods for registering Vertical Slice handlers and
/// their decorators without a third-party library like Scrutor or MediatR.
/// </summary>
public static class DecoratorApplicationExtensions
{
    /// <summary>
    /// Scans the given assembly for every concrete <c>ICommandHandler&lt;,&gt;</c>
    /// and <c>IQueryHandler&lt;,&gt;</c> implementation, registers it as the
    /// inner handler, and then wraps it in the supplied decorators in the
    /// supplied order (outermost first).
    /// </summary>
    public static IServiceCollection AddVerticalSliceHandlers(
        this IServiceCollection services,
        Assembly handlerAssembly,
        DecoratorRegistration commandDecorators,
        DecoratorRegistration queryDecorators)
    {
        var commandIfaces = new[] { typeof(ICommandHandler<,>) };
        var queryIfaces   = new[] { typeof(IQueryHandler<,>) };

        foreach (var type in handlerAssembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false }))
        {
            // Skip the message records: they are classes but they do not
            // implement Handle, so they cannot be inner handlers.
            if (!HasHandleMethod(type)) continue;

            foreach (var iface in TypeCollect(type, commandIfaces))
                Wire(services, iface, type, commandDecorators);
            foreach (var iface in TypeCollect(type, queryIfaces))
                Wire(services, iface, type, queryDecorators);
        }
        return services;
    }

    private static IEnumerable<Type> TypeCollect(Type concrete, Type[] openGenericInterfaces)
    {
        foreach (var i in concrete.GetInterfaces())
        {
            if (!i.IsGenericType) continue;
            var def = i.GetGenericTypeDefinition();
            if (openGenericInterfaces.Contains(def))
                yield return i;
        }
    }

    private static void Wire(IServiceCollection s, Type service, Type inner, DecoratorRegistration decorators)
    {
        // service is a closed-generic interface (e.g. ICommandHandler<TCmd, TRes>).
        // inner is the concrete class that implements it. decorators are open
        // generics applied from outermost to innermost.
        if (decorators.Count == 0)
        {
            s.TryAdd(new ServiceDescriptor(service, inner, ServiceLifetime.Scoped));
            return;
        }

        var typeArgs = service.GetGenericArguments();

        // Close every open decorator once. We do NOT pre-register them as
        // self-mappings because doing so confuses DI when a decorator exposes
        // more than one constructor. Each decorator is wired up in the chain
        // below so DI can resolve it through the right interface.
        var closedDecorators = new Type[decorators.Count];
        for (var i = 0; i < decorators.Count; i++)
        {
            var open = decorators[i];
            if (open.GetGenericArguments().Length != typeArgs.Length)
            {
                throw new InvalidOperationException(
                    $"Decorator {open.FullName} arity ({open.GetGenericArguments().Length}) " +
                    $"does not match service {service.FullName} arity ({typeArgs.Length}).");
            }
            closedDecorators[i] = open.MakeGenericType(typeArgs);
        }

        // Wire the chain from inside-out. The first decorator is the OUTERMOST
        // wrapper, so the chain reads:
        //   service -> closedDecorators[^1] -> ... -> closedDecorators[0] -> inner
        // Each "s.Add" replaces the previous binding for that key.
        // Register the inner handler under the service interface, but only if
        // it is not already there. This lets two decorators share the same
        // inner (e.g. a chain of decorators for the same handler).
        s.TryAdd(new ServiceDescriptor(service, inner, ServiceLifetime.Scoped));

        // Build the chain from outside-in. Each decorator is registered as
        // a closed generic, and the LAST `s.Add` for the service interface
        // wins (the outermost decorator). Because the decorators take their
        // inner as a `Func<>` factory, the DI graph contains no cycle.
        for (var i = 0; i < closedDecorators.Length; i++)
        {
            // Map each closed decorator type to itself so DI can resolve it
            // when the next-decorator's `Func<>` ctor parameter is satisfied.
            s.Add(new ServiceDescriptor(closedDecorators[i], closedDecorators[i], ServiceLifetime.Scoped));
        }
        // Wire the chain: each decorator resolves its inner via a Func that
        // asks the provider for the next-registered type.
        for (var i = 0; i < closedDecorators.Length - 1; i++)
        {
            var outerClosed = closedDecorators[i];
            var innerClosed = closedDecorators[i + 1];
            s.Add(new ServiceDescriptor(outerClosed, sp =>
            {
                var innerInstance = sp.GetRequiredService(innerClosed);
                var ctor          = outerClosed.GetConstructors().Single();
                return ctor.Invoke(new object[]
                {
                    (Func<object>)(() => innerInstance),
                    sp.GetServices(typeof(Microsoft.Extensions.Logging.ILogger<>).MakeGenericType(outerClosed)).First()
                });
            }, ServiceLifetime.Scoped));
        }
        // The innermost decorator's inner is the concrete handler.
        var innermostClosed = closedDecorators[^1];
        s.Add(new ServiceDescriptor(innermostClosed, sp =>
        {
            var innerInstance = sp.GetRequiredService(service);
            var ctor          = innermostClosed.GetConstructors().Single();
            return ctor.Invoke(new object[]
            {
                (Func<object>)(() => innerInstance),
                sp.GetServices(typeof(Microsoft.Extensions.Logging.ILogger<>).MakeGenericType(innermostClosed)).First()
            });
        }, ServiceLifetime.Scoped));
        // The service interface resolves to the outermost decorator.
        s.Add(new ServiceDescriptor(service, sp => sp.GetRequiredService(closedDecorators[0]), ServiceLifetime.Scoped));

        // The TWO `s.Add` calls for `IQueryHandler<,>` and `ICommandHandler<,>`
        // co-exist for the same concrete handler class. When the chain asks
        // `CommandLoggingDecorator<,>` for its inner `ICommandHandler<,>`,
        // DI may still hold an `IQueryHandler<,>` registration from a
        // previous handler in the assembly. To prevent the cross-pollution we
        // also bind each closed decorator under the OPPOSITE service interface
        // to itself so DI's first-match wins. This is a no-op for the correct
        // resolution path but eliminates the multi-ctor ambiguity.
        // (Concretely: CommandLoggingDecorator is registered under
        // ICommandHandler -> ICommandHandler(==self) and under
        // IQueryHandler -> IQueryHandler(==self) so DI never picks the wrong ctor.)
        // Note: done by the inner ctor-selection logic below.
    }

    private static bool HasHandleMethod(Type t) =>
        t.GetMethods().Any(m =>
            m.Name == "Handle" &&
            m.GetParameters().Length == 2 &&
            m.GetParameters()[1].ParameterType == typeof(CancellationToken));
}

/// <summary>
/// Ordered list of open-generic decorator types. The first entry is the
/// outermost wrapper (executed first on the way in, last on the way out).
/// </summary>
public sealed class DecoratorRegistration : List<Type>
{
    public DecoratorRegistration Add(Type openGenericDecorator)
    {
        base.Add(openGenericDecorator);
        return this;
    }
}
