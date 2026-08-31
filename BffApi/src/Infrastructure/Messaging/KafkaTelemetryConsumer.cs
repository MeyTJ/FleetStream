using System.Diagnostics;
using Confluent.Kafka;
using FleetStream.Application.Abstractions;
using FleetStream.Core.Domain.Entities;
using FleetStream.Infrastructure.Metrics;
using FleetStream.Infrastructure.Options;
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
    private readonly ITruckStateStore _states;
    private readonly ITelemetryHistoryStore _history;
    private readonly INotificationService _notifier;
    private readonly ILogger<KafkaTelemetryConsumer> _logger;

    public KafkaTelemetryConsumer(
        IOptions<KafkaOptions> opts,
        ITruckStateStore states,
        ITelemetryHistoryStore history,
        INotificationService notifier,
        ILogger<KafkaTelemetryConsumer> logger)
    {
        _opts     = opts.Value;
        _states   = states;
        _history  = history;
        _notifier = notifier;
        _logger   = logger;
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

                await _states.SetStateAsync(state, stoppingToken);
                await _history.AppendAsync(telemetry, stoppingToken);
                await _notifier.BroadcastTelemetryUpdateAsync(telemetry, stoppingToken);
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