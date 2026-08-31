using System.Diagnostics;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace FleetStream.Infrastructure.Messaging;

internal static class KafkaConsumerHelpers
{
    public static string ExtractCorrelationId(Headers headers)
    {
        if (headers.TryGetLastBytes("X-Correlation-Id", out var bytes))
            return System.Text.Encoding.UTF8.GetString(bytes);
        return "c-" + Guid.NewGuid().ToString("N")[..8];
    }

    public static IDisposable BeginConsumerScope(ILogger logger, string correlationId)
    {
        var activity = Activity.Current;
        return logger.BeginScope(new Dictionary<string, object?>
        {
            ["correlationId"] = correlationId,
            ["traceId"]       = activity?.TraceId.ToString() ?? string.Empty,
            ["spanId"]        = activity?.SpanId.ToString() ?? string.Empty,
        }) ?? EmptyDisposable.Instance;
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }
}
