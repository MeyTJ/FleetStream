namespace FleetStream.Application.Shared.Results;

/// <summary>
/// Lightweight Result type for the Application layer. Keeps the Application
/// stack free of HTTP-typed return values (no <c>ActionResult</c>).
/// </summary>
public readonly record struct Result<T>(bool IsSuccess, T? Value, Error? Error)
{
    public static Result<T> Success(T value) => new(true, value, null);

    public static Result<T> Failure(string code, string message) =>
        new(false, default, new Error(code, message));
}

public sealed record Error(string Code, string Message);

/// <summary>Non-generic Result for void commands.</summary>
public readonly record struct Result(bool IsSuccess, Error? Error)
{
    public static Result Success() => new(true, null);
    public static Result Failure(string code, string message) =>
        new(false, new Error(code, message));
}