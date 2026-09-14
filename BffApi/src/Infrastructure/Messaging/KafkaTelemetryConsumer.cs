using System.Diagnostics;
using Confluent.Kafka;
using FleetStream.Application.Abstractions;
using FleetStream.Core.Domain.Entities;
using FleetStream.Infrastructure.Metrics;
using FleetStream.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetStream.Infrastructure.Messaging;

/// <summary>
/// Background consumer for the <c>fleet.telemetry.processed</c> topic. Every
/// message is deserialised into a <see cref="TruckTelemetry"/> and routed to
/// the state store + notification service. The consumer is intentionally
/// tolerant: a startup-time Kafka outage does not crash the host (Phase 3 does
/// not yet own the streaming-engine's Kafka, so failure is the steady state in
/// local development).
/// </summary>
public sealed class KafkaTelemetryConsumer : BackgroundService
{
    private readonly KafkaOptions _opts;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<KafkaTelemetryConsumer> _logger;

    // §3.3: OnTruckStateUpdate is capped at one broadcast per truck per 2 s
    // (server-side rate limit). Telemetry samples stream to the
    // telemetry:full group at full rate instead.
    private static readonly TimeSpan StateBroadcastMinInterval = TimeSpan.FromSeconds(2);
    private readonly Dictionary<string, DateTime> _lastStateBroadcastAt = new();

    // ITruckStateStore / ITelemetryHistoryStore / INotificationService are all
    // registered as *scoped* (they sit behind the Redis + SignalR adapters), while
    // a BackgroundService is a singleton. Injecting them directly produced a
    // captive dependency that ValidateOnBuild rejected at startup
    // ("Cannot consume scoped service ... from singleton 'IHostedService'").
    // A scope is created per message instead.
    public KafkaTelemetryConsumer(
        IOptions<KafkaOptions> opts,
        IServiceScopeFactory scopeFactory,
        ILogger<KafkaTelemetryConsumer> logger)
    {
        _opts         = opts.Value;
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Soft-start: defer Kafka by a few seconds so the host is "ready" even
        // when the broker is down. Keeps the liveness probe green.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
        catch (OperationCanceledException) { return; }

        var config = new ConsumerConfig
        {
            BootstrapServers   = _opts.Brokers,
            GroupId            = _opts.ConsumerGroup,
            EnableAutoCommit   = false,
            AutoOffsetReset    = AutoOffsetReset.Latest,
            SessionTimeoutMs   = 10_000,
            AllowAutoCreateTopics = true,
        };
        KafkaClientConfig.ApplySecurity(config, _opts);

        using var consumer = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, e) => _logger.LogWarning("Kafka error: {Reason}", e.Reason))
            .Build();

        try
        {
            consumer.Subscribe(_opts.TelemetryTopic);
            _logger.LogInformation("Subscribed to {Topic}", _opts.TelemetryTopic);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to subscribe to {Topic}; consumer will not start.", _opts.TelemetryTopic);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? cr = null;
            try
            {
                cr = consumer.Consume(TimeSpan.FromSeconds(1));
                if (cr is null || cr.Message is null) continue;

                var correlationId = KafkaConsumerHelpers.ExtractCorrelationId(cr.Message.Headers);
                using var activity = new Activity("Kafka.Consume").Start();
                activity?.SetTag("messaging.destination", _opts.TelemetryTopic);
                using var logScope = KafkaConsumerHelpers.BeginConsumerScope(_logger, correlationId);

                var sw = Stopwatch.StartNew();
                var telemetry = System.Text.Json.JsonSerializer.Deserialize<TruckTelemetry>(
                    cr.Message.Value,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (telemetry is null) continue;

                var state = new TruckState
                {
                    TruckId                 = telemetry.TruckId,
                    Timestamp               = telemetry.EventTimestamp,
                    Latitude                = telemetry.Latitude,
                    Longitude               = telemetry.Longitude,
                    SpeedKmh                = telemetry.SpeedKmh,
                    EngineTemperatureCelsius = telemetry.EngineTemperatureCelsius,
                    FuelLevelPercent        = telemetry.FuelLevelPercent,
                    IsMoving                = telemetry.SpeedKmh > 0,
                    IsOnline                = true,
                    RiskLevel               = telemetry.RiskLevel,
                    RiskScore               = telemetry.RiskScore,
                };

                // Scoped services are resolved per message (see ctor comment).
                using var scope = _scopeFactory.CreateScope();
                var states   = scope.ServiceProvider.GetRequiredService<ITruckStateStore>();
                var history  = scope.ServiceProvider.GetRequiredService<ITelemetryHistoryStore>();
                var notifier = scope.ServiceProvider.GetRequiredService<INotificationService>();

                await states.SetStateAsync(state, stoppingToken);
                await history.AppendAsync(telemetry, stoppingToken);
                await notifier.BroadcastTelemetryUpdateAsync(telemetry, stoppingToken);

                // Throttle state broadcasts to one per truck per 2 s (§3.3).
                var nowUtc = DateTime.UtcNow;
                if (!_lastStateBroadcastAt.TryGetValue(state.TruckId, out var lastBroadcast) ||
                    nowUtc - lastBroadcast >= StateBroadcastMinInterval)
                {
                    _lastStateBroadcastAt[state.TruckId] = nowUtc;
                    await notifier.BroadcastTruckStateAsync(state, stoppingToken);
                }

                consumer.Commit(cr);

                sw.Stop();
                BffMetrics.KafkaMessagesTotal.Add(1,
                    new KeyValuePair<string, object?>("topic", _opts.TelemetryTopic),
                    new KeyValuePair<string, object?>("result", "success"));
                BffMetrics.KafkaProcessingDurationSeconds.Record(sw.Elapsed.TotalSeconds,
                    new KeyValuePair<string, object?>("topic", _opts.TelemetryTopic));
            }
            catch (ConsumeException ex)
            {
                BffMetrics.KafkaConsumerErrorsTotal.Add(1,
                    new KeyValuePair<string, object?>("topic", _opts.TelemetryTopic),
                    new KeyValuePair<string, object?>("kind", "consume"));
                _logger.LogWarning(ex, "Consume failed; will retry.");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                BffMetrics.KafkaConsumerErrorsTotal.Add(1,
                    new KeyValuePair<string, object?>("topic", _opts.TelemetryTopic),
                    new KeyValuePair<string, object?>("kind", "processing"));
                BffMetrics.KafkaMessagesTotal.Add(1,
                    new KeyValuePair<string, object?>("topic", _opts.TelemetryTopic),
                    new KeyValuePair<string, object?>("result", "error"));
                _logger.LogError(ex, "Unhandled error in telemetry consumer loop.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }

        try { consumer.Close(); } catch { /* best-effort */ }
    }
}