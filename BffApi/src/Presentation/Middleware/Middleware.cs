using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using FleetStream.Infrastructure.Metrics;
using FleetStream.Infrastructure.Options;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetStream.Presentation.Middleware;

/// <summary>
/// Reads (or generates) a correlation id for every request and stores it in
/// <c>HttpContext.Items["CorrelationId"]</c> and as the <c>X-Correlation-Id</c>
/// response header. Downstream code (loggers, SignalR, Kafka) uses it to
/// stitch together the full lifecycle of a single user action.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey    = "CorrelationId";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;
    private readonly string _serviceName;
    private readonly string _serviceVersion;

    public CorrelationIdMiddleware(
        RequestDelegate next,
        ILogger<CorrelationIdMiddleware> logger,
        IOptions<OpenTelemetryOptions> otel)
    {
        _next            = next;
        _logger          = logger;
        _serviceName     = otel.Value.ServiceName;
        _serviceVersion  = otel.Value.ServiceVersion;
    }

    public async Task Invoke(HttpContext context)
    {
        string id;
        if (context.Request.Headers.TryGetValue(HeaderName, out var incoming) &&
            !string.IsNullOrWhiteSpace(incoming.ToString()))
        {
            id = incoming.ToString();
        }
        else
        {
            id = "c-" + Guid.NewGuid().ToString("N")[..8];
        }

        context.Items[ItemKey] = id;
        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey(HeaderName))
                context.Response.Headers[HeaderName] = id;
            return Task.CompletedTask;
        });

        var activity = Activity.Current;
        using (_logger.BeginScope(new Dictionary<string, object?>
        {
            ["correlationId"] = id,
            ["traceId"]       = activity?.TraceId.ToString() ?? string.Empty,
            ["spanId"]        = activity?.SpanId.ToString() ?? string.Empty,
            ["service"]       = _serviceName,
            ["version"]       = _serviceVersion,
        }))
        {
            await _next(context);
        }
    }
}

/// <summary>
/// Catches any unhandled exception and translates it to a stable RFC 7807
/// <c>application/problem+json</c> envelope. Validation errors map to 422,
/// everything else to 500. Stack traces are NEVER exposed to clients.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ValidationException vex)
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status422UnprocessableEntity,
                "Validation failed",
                "One or more validation errors occurred.",
                vex.Errors
                    .Select(e => new { field = e.PropertyName, message = e.ErrorMessage })
                    .ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
            await WriteProblemAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "Internal server error",
                _env.IsDevelopment() ? ex.Message : "An unexpected error occurred.",
                Array.Empty<object>());
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, int status, string title, string detail, object errors)
    {
        if (context.Response.HasStarted)
            return;

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        var correlationId = context.Items[CorrelationIdMiddleware.ItemKey] as string ?? string.Empty;
        var activity = Activity.Current;
        var body = new
        {
            type = $"https://fleetstream.example.com/errors/{TitleToKebab(title)}",
            title,
            status,
            detail,
            instance = context.Request.Path.ToString(),
            traceId = activity?.TraceId.ToString() ?? string.Empty,
            correlationId,
            errors,
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonOptions));
    }

    private static string TitleToKebab(string title) =>
        string.Concat(title.Select((c, i) => i > 0 && char.IsUpper(c) ? "-" + char.ToLowerInvariant(c) : c.ToString().ToLowerInvariant()))
              .Replace("--", "-")
              .Trim('-');
}

public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IHostEnvironment _env;

    public SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment env)
    {
        _next = next;
        _env  = env;
    }

    public async Task Invoke(HttpContext context)
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"]        = "no-referrer";
        context.Response.Headers["X-Frame-Options"]        = "DENY";

        if (!_env.IsDevelopment())
            context.Response.Headers["Strict-Transport-Security"] =
                "max-age=31536000; includeSubDomains; preload";

        await _next(context);
    }
}

public sealed class AuditLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger _auditLogger;

    public AuditLoggingMiddleware(RequestDelegate next, ILoggerFactory loggerFactory)
    {
        _next        = next;
        _auditLogger = loggerFactory.CreateLogger("FleetStream.Audit");
    }

    public async Task Invoke(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        await _next(context);
        sw.Stop();

        if (context.User?.Identity?.IsAuthenticated != true)
            return;

        var correlationId = context.Items[CorrelationIdMiddleware.ItemKey] as string ?? string.Empty;
        var activity      = Activity.Current;
        var roles         = context.User.FindAll("roles").Select(c => c.Value).ToArray();

        _auditLogger.LogInformation(
            "Request {Method} {Path} {Status} {DurationMs} {Subject} {Roles} {CorrelationId} {TraceId}",
            context.Request.Method,
            context.Request.Path.Value,
            context.Response.StatusCode,
            sw.ElapsedMilliseconds,
            context.User.FindFirst("sub")?.Value ?? string.Empty,
            roles,
            correlationId,
            activity?.TraceId.ToString() ?? string.Empty);
    }
}

public static class RateLimitExemptions
{
    public static bool IsExempt(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        return path.StartsWith("/api/v1/health", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/metrics", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/negotiate", StringComparison.OrdinalIgnoreCase);
    }
}
