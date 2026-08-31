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
        // Register the concrete handler by its own type so the innermost
        // decorator resolves the real handler, not the decorated service
        // interface (which is later bound to the outermost wrapper).
        s.TryAdd(new ServiceDescriptor(inner, inner, ServiceLifetime.Scoped));

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
                CreateDecorator(sp, outerClosed, innerClosed), ServiceLifetime.Scoped));
        }
        var innermostClosed = closedDecorators[^1];
        s.Add(new ServiceDescriptor(innermostClosed, sp =>
            CreateDecorator(sp, innermostClosed, inner), ServiceLifetime.Scoped));
        s.Add(new ServiceDescriptor(service, sp => sp.GetRequiredService(closedDecorators[0]), ServiceLifetime.Scoped));
    }

    private static object CreateDecorator(IServiceProvider sp, Type decoratorClosed, Type innerServiceType)
    {
        var ctor = decoratorClosed.GetConstructors().Single();
        var args = new object[ctor.GetParameters().Length];
        for (var i = 0; i < ctor.GetParameters().Length; i++)
        {
            var param = ctor.GetParameters()[i];
            if (IsFunc(param.ParameterType))
                args[i] = CreateInnerFactory(sp, innerServiceType, param.ParameterType);
            else
                args[i] = ResolveParameter(sp, param.ParameterType);
        }
        return ctor.Invoke(args)!;
    }

    private static bool IsFunc(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Func<>);

    private static object CreateInnerFactory(IServiceProvider sp, Type innerServiceType, Type funcType)
    {
        var innerType = funcType.GetGenericArguments()[0];
        var resolve = () => sp.GetRequiredService(innerServiceType);
        var method  = typeof(DecoratorApplicationExtensions)
            .GetMethod(nameof(DelegateFactory), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(innerType);
        return method.Invoke(null, new object[] { resolve })!;
    }

    private static Func<T> DelegateFactory<T>(Func<object> resolve) where T : notnull =>
        () => (T)resolve();

    private static object ResolveParameter(IServiceProvider sp, Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            var elementType = type.GetGenericArguments()[0];
            var listType    = typeof(List<>).MakeGenericType(elementType);
            var list        = Activator.CreateInstance(listType)!;
            var add         = listType.GetMethod("Add")!;
            foreach (var service in sp.GetServices(elementType))
                add.Invoke(list, new[] { service });
            return list;
        }
        return sp.GetRequiredService(type);
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
