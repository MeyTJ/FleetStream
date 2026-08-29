using System.Diagnostics;
using FleetStream.Application.Shared.Messaging;
using Microsoft.Extensions.Logging;

namespace FleetStream.Application.Shared.Decorators;

/// <summary>
/// Generic structured-logging decorator for <see cref="ICommandHandler{TCommand, TResult}"/>.
/// The inner handler is injected as a factory <c>Func&lt;&gt;</c> so the
/// decorator chain does not create a DI resolution cycle. Emits one
/// Information log on entry and another on successful exit (Error on
/// failure) with elapsed milliseconds.
/// </summary>
public sealed class CommandLoggingDecorator<TCommand, TResult> : ICommandHandler<TCommand, TResult>
    where TCommand : notnull
{
    private readonly Func<ICommandHandler<TCommand, TResult>> _inner;
    private readonly ILogger<CommandLoggingDecorator<TCommand, TResult>> _logger;

    public CommandLoggingDecorator(
        Func<ICommandHandler<TCommand, TResult>> inner,
        ILogger<CommandLoggingDecorator<TCommand, TResult>> logger)
    {
        _inner  = inner;
        _logger = logger;
    }

    public async Task<TResult> Handle(TCommand command, CancellationToken cancellationToken)
    {
        var name = typeof(TCommand).Name;
        _logger.LogInformation("Handling {Request}", name);
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _inner().Handle(command, cancellationToken);
            sw.Stop();
            _logger.LogInformation("Handled {Request} in {ElapsedMs} ms", name, sw.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Failed {Request} after {ElapsedMs} ms", name, sw.ElapsedMilliseconds);
            throw;
        }
    }
}

/// <summary>
/// Read-only mirror of <see cref="CommandLoggingDecorator{TCommand, TResult}"/>
/// for <see cref="IQueryHandler{TQuery, TResult}"/>.
/// </summary>
public sealed class QueryLoggingDecorator<TQuery, TResult> : IQueryHandler<TQuery, TResult>
    where TQuery : notnull
{
    private readonly Func<IQueryHandler<TQuery, TResult>> _inner;
    private readonly ILogger<QueryLoggingDecorator<TQuery, TResult>> _logger;

    public QueryLoggingDecorator(
        Func<IQueryHandler<TQuery, TResult>> inner,
        ILogger<QueryLoggingDecorator<TQuery, TResult>> logger)
    {
        _inner  = inner;
        _logger = logger;
    }

    public async Task<TResult> Handle(TQuery query, CancellationToken cancellationToken)
    {
        var name = typeof(TQuery).Name;
        _logger.LogInformation("Handling {Request}", name);
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _inner().Handle(query, cancellationToken);
            sw.Stop();
            _logger.LogInformation("Handled {Request} in {ElapsedMs} ms", name, sw.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Failed {Request} after {ElapsedMs} ms", name, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
