namespace FleetStream.Application.Shared.Messaging;

/// <summary>
/// Pure-DI query handler. A query is read-only and returns a value.
/// Implementations are resolved by ASP.NET Core's built-in container and
/// composed at runtime by the <c>LoggingDecorator</c> generic decorator.
/// </summary>
/// <typeparam name="TQuery">The query type.</typeparam>
/// <typeparam name="TResult">The result type.</typeparam>
public interface IQueryHandler<in TQuery, TResult>
{
    Task<TResult> Handle(TQuery query, CancellationToken cancellationToken);
}