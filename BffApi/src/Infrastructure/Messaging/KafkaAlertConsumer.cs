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

public sealed class KafkaAlertConsumer : BackgroundService
{
    private readonly KafkaOptions _opts;
    private readonly INotificationService _notifier;
    private readonly ILogger<KafkaAlertConsumer> _logger;

    public KafkaAlertConsumer(
        IOptions<KafkaOptions> opts,
        INotificationService notifier,
        ILogger<KafkaAlertConsumer> logger)
    {
        _opts     = opts.Value;
        _notifier = notifier;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
        catch (OperationCanceledException) { return; }

        var config = new ConsumerConfig
        {
            BootstrapServers   = _opts.Brokers,
            GroupId            = _opts.ConsumerGroup + ".alerts",
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
            consumer.Subscribe(_opts.AlertTopic);
            _logger.LogInformation("Subscribed to {Topic}", _opts.AlertTopic);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to subscribe to {Topic}; consumer will not start.", _opts.AlertTopic);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cr = consumer.Consume(TimeSpan.FromSeconds(1));
                if (cr is null || cr.Message is null) continue;

                var correlationId = KafkaConsumerHelpers.ExtractCorrelationId(cr.Message.Headers);
                using var activity = new Activity("Kafka.Consume").Start();
                activity?.SetTag("messaging.destination", _opts.AlertTopic);
                using var logScope = KafkaConsumerHelpers.BeginConsumerScope(_logger, correlationId);

                var sw = Stopwatch.StartNew();
                var alert = System.Text.Json.JsonSerializer.Deserialize<Alert>(
                    cr.Message.Value,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (alert is null) continue;

                await _notifier.BroadcastAlertAsync(alert, stoppingToken);
                consumer.Commit(cr);

                sw.Stop();
                BffMetrics.KafkaMessagesTotal.Add(1,
                    new KeyValuePair<string, object?>("topic", _opts.AlertTopic),
                    new KeyValuePair<string, object?>("result", "success"));
                BffMetrics.KafkaProcessingDurationSeconds.Record(sw.Elapsed.TotalSeconds,
                    new KeyValuePair<string, object?>("topic", _opts.AlertTopic));
            }
            catch (ConsumeException ex)
            {
                BffMetrics.KafkaConsumerErrorsTotal.Add(1,
                    new KeyValuePair<string, object?>("topic", _opts.AlertTopic),
                    new KeyValuePair<string, object?>("kind", "consume"));
                _logger.LogWarning(ex, "Consume failed; will retry.");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                BffMetrics.KafkaConsumerErrorsTotal.Add(1,
                    new KeyValuePair<string, object?>("topic", _opts.AlertTopic),
                    new KeyValuePair<string, object?>("kind", "processing"));
                BffMetrics.KafkaMessagesTotal.Add(1,
                    new KeyValuePair<string, object?>("topic", _opts.AlertTopic),
                    new KeyValuePair<string, object?>("result", "error"));
                _logger.LogError(ex, "Unhandled error in alert consumer loop.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }

        try { consumer.Close(); } catch { /* best-effort */ }
    }
}
