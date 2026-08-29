namespace FleetStream.Application.Shared.Messaging;

/// <summary>
/// Pure-DI command handler. A command mutates state and returns a result.
/// Implementations are resolved by ASP.NET Core's built-in container and
/// composed at runtime by the <c>ValidationDecorator</c> and
/// <c>LoggingDecorator</c> generic decorators (see
/// <c>FleetStream.Application.Shared.Decorators</c>).
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <typeparam name="TResult">The result type.</typeparam>
public interface ICommandHandler<in TCommand, TResult>
{
    Task<TResult> Handle(TCommand command, CancellationToken cancellationToken);
}