using System.Diagnostics.Metrics;

namespace FleetStream.Infrastructure.Metrics;

public static class BffMetrics
{
    public const string MeterName = "fleetstream.bff";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    public static readonly Counter<long> KafkaMessagesTotal =
        Meter.CreateCounter<long>("fleetstream_bff_kafka_messages_total", description: "Kafka messages consumed");

    public static readonly Counter<long> KafkaConsumerErrorsTotal =
        Meter.CreateCounter<long>("fleetstream_bff_kafka_consumer_errors_total", description: "Kafka consumer errors");

    public static readonly Counter<long> RedisOperationsTotal =
        Meter.CreateCounter<long>("fleetstream_bff_redis_operations_total", description: "Redis operations");

    public static readonly Counter<long> SignalRMessagesTotal =
        Meter.CreateCounter<long>("fleetstream_bff_signalr_messages_total", description: "SignalR messages");

    public static readonly Counter<long> SignalRMessagesDroppedTotal =
        Meter.CreateCounter<long>("fleetstream_bff_signalr_messages_dropped_total", description: "SignalR messages dropped");

    public static readonly Counter<long> AlertsAcknowledgedTotal =
        Meter.CreateCounter<long>("fleetstream_bff_alerts_acknowledged_total", description: "Alerts acknowledged");

    public static readonly Counter<long> CacheHitsTotal =
        Meter.CreateCounter<long>("fleetstream_bff_cache_hits_total", description: "Cache hits");

    public static readonly Counter<long> CacheMissesTotal =
        Meter.CreateCounter<long>("fleetstream_bff_cache_misses_total", description: "Cache misses");

    public static readonly Histogram<double> KafkaProcessingDurationSeconds =
        Meter.CreateHistogram<double>("fleetstream_bff_kafka_processing_duration_seconds", unit: "s",
            description: "Kafka message processing duration");

    private static long _activeConnections;

    static BffMetrics()
    {
        Meter.CreateObservableGauge(
            "fleetstream_bff_signalr_connections_active",
            () => new Measurement<long>(Interlocked.Read(ref _activeConnections)),
            description: "Active SignalR connections");
    }

    public static void IncrementConnections() => Interlocked.Increment(ref _activeConnections);

    public static void DecrementConnections() => Interlocked.Decrement(ref _activeConnections);
}
