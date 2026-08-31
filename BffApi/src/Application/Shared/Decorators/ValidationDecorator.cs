using FleetStream.Application.Shared.Messaging;
using FluentValidation;

namespace FleetStream.Application.Shared.Decorators;

/// <summary>
/// FluentValidation decorator for <see cref="ICommandHandler{TCommand, TResult}"/>.
/// Runs every <see cref="IValidator{T}"/> registered for the command and
/// throws <see cref="ValidationException"/> if any rule fails. The exception is
/// translated to HTTP 422 by <c>ExceptionHandlingMiddleware</c>.
/// </summary>
public sealed class CommandValidationDecorator<TCommand, TResult> : ICommandHandler<TCommand, TResult>
    where TCommand : notnull
{
    private readonly Func<ICommandHandler<TCommand, TResult>> _inner;
    private readonly IEnumerable<IValidator<TCommand>> _validators;

    public CommandValidationDecorator(
        Func<ICommandHandler<TCommand, TResult>> inner,
        IEnumerable<IValidator<TCommand>> validators)
    {
        _inner      = inner;
        _validators = validators;
    }

    public Task<TResult> Handle(TCommand command, CancellationToken cancellationToken)
    {
        var failures = _validators
            .Select(v => v.Validate(new ValidationContext<TCommand>(command)))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count > 0)
            throw new ValidationException(failures);

        return _inner().Handle(command, cancellationToken);
    }
}

/// <summary>
/// Read-only mirror of <see cref="CommandValidationDecorator{TCommand, TResult}"/>
/// for <see cref="IQueryHandler{TQuery, TResult}"/>.
/// </summary>
public sealed class QueryValidationDecorator<TQuery, TResult> : IQueryHandler<TQuery, TResult>
    where TQuery : notnull
{
    private readonly Func<IQueryHandler<TQuery, TResult>> _inner;
    private readonly IEnumerable<IValidator<TQuery>> _validators;

    public QueryValidationDecorator(
        Func<IQueryHandler<TQuery, TResult>> inner,
        IEnumerable<IValidator<TQuery>> validators)
    {
        _inner      = inner;
        _validators = validators;
    }

    public Task<TResult> Handle(TQuery query, CancellationToken cancellationToken)
    {
        var failures = _validators
            .Select(v => v.Validate(new ValidationContext<TQuery>(query)))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count > 0)
            throw new ValidationException(failures);

        return _inner().Handle(query, cancellationToken);
    }
}