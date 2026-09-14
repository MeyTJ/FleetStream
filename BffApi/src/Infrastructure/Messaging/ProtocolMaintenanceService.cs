using FleetStream.Application.Abstractions;
using FleetStream.Infrastructure.Metrics;
using FleetStream.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetStream.Infrastructure.Messaging;

/// <summary>
/// Drives the SignalR protocol §3.3 server pushes that are not triggered by a single
/// Kafka record:
///
/// <list type="bullet">
///   <item><description><c>OnPresenceChange</c> — sweep for trucks whose telemetry went stale
///   (<see cref="SignalROptions.OnlineThresholdSeconds"/>) and declare them offline.</description></item>
///   <item><description><c>OnAlertsPurged</c> — enforce the shared retention window and tell
///   clients the cutoff they must trim to.</description></item>
///   <item><description><c>OnFleetUpdate</c> — the periodic warm-reload heartbeat, emitted only
///   when at least one state changed inside the window (otherwise omitted, per §3.3).</description></item>
/// </list>
///
/// Lives in Infrastructure and reaches the scoped adapters through
/// <see cref="IServiceScopeFactory"/>, exactly like the Kafka consumers: a singleton
/// BackgroundService cannot hold captive scoped dependencies (ValidateOnBuild rejects that at
/// startup), and Infrastructure must not reference Presentation.
/// </summary>
public sealed class ProtocolMaintenanceService : BackgroundService
{
    /// <summary>
    /// Number of simultaneous offline transitions that stops looking like "the depot
    /// parked" and starts looking like "our ingest stalled".
    /// </summary>
    internal const int BulkPresenceThreshold = 25;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SignalROptions _opts;
    private readonly ILogger<ProtocolMaintenanceService> _logger;

    /// <summary>
    /// Highest <see cref="global::FleetStream.Core.Domain.Entities.TruckState.Timestamp"/>
    /// observed by the fleet-update loop, so the heartbeat is suppressed when the fleet
    /// is genuinely idle.
    /// </summary>
    private DateTime _lastSeenChange = DateTime.MinValue;

    public ProtocolMaintenanceService(
        IServiceScopeFactory scopeFactory,
        IOptions<SignalROptions> opts,
        ILogger<ProtocolMaintenanceService> logger)
    {
        _scopeFactory = scopeFactory;
        _opts         = opts.Value;
        _logger       = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Protocol maintenance started (presence threshold {Threshold}s, sweep {Sweep}s, " +
            "fleet update {FleetUpdate}s, retention {Retention} alerts pruned every {Prune}s)",
            _opts.OnlineThresholdSeconds, _opts.PresenceSweepIntervalSeconds,
            _opts.FleetUpdateIntervalSeconds, _opts.AlertRetentionCount,
            _opts.AlertPruneIntervalSeconds);

        // Independent cadences; a failure in one must not stop the others.
        return Task.WhenAll(
            RunLoop("presence",    _opts.PresenceSweepIntervalSeconds, SweepPresenceAsync,    stoppingToken),
            RunLoop("fleet-update", _opts.FleetUpdateIntervalSeconds,  BroadcastFleetUpdateAsync, stoppingToken),
            RunLoop("alert-prune", _opts.AlertPruneIntervalSeconds,   PruneAlertsAsync,       stoppingToken));
    }

    private async Task RunLoop(
        string name, int intervalSeconds,
        Func<CancellationToken, Task> body, CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, intervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Soft-start: mirrors the Kafka consumers so a Redis outage during boot
                // yields warnings rather than a crashing host.
                await Task.Delay(interval, stoppingToken);
                await body(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Protocol maintenance loop {Loop} failed; continuing.", name);
            }
        }
    }

    /// <summary>
    /// §3.3 OnPresenceChange. Marking the truck offline removes it from the online
    /// index, so a subsequent sweep no longer sees it — the transition is emitted once
    /// without needing a separate dedupe set.
    /// </summary>
    internal async Task SweepPresenceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var states   = scope.ServiceProvider.GetRequiredService<ITruckStateStore>();
        var notifier = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var stale = await states.GetStaleOnlineStatesAsync(
            TimeSpan.FromSeconds(_opts.OnlineThresholdSeconds), cancellationToken);

        if (stale.Count == 0) return;

        foreach (var state in stale)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var silence = DateTime.UtcNow - state.Timestamp;
            await states.MarkOfflineAsync(state.TruckId, cancellationToken);
            await notifier.BroadcastPresenceChangeAsync(state.TruckId, isOnline: false, cancellationToken);

            BffMetrics.PresenceTransitionsTotal.Add(1,
                new KeyValuePair<string, object?>("transition", "offline"));

            _logger.LogInformation(
                "Truck {TruckId} declared offline: no telemetry for {Silence}",
                state.TruckId, silence);
        }

        // §3.3/§3.8: a bulk transition is an ops event worth an explicit banner, since it
        // usually means the pipeline stalled rather than every truck parking at once.
        if (stale.Count >= BulkPresenceThreshold)
        {
            await notifier.BroadcastSystemMessageAsync(
                "warn",
                "presence.bulk_offline",
                $"{stale.Count} trucks stopped reporting telemetry. Positions shown are the last known.",
                cancellationToken);
        }
    }

    /// <summary>
    /// §3.3 OnAlertsPurged — trim the canonical store to the retention window and
    /// publish the cutoff so client ring buffers converge with it.
    /// </summary>
    internal async Task PruneAlertsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var alerts   = scope.ServiceProvider.GetRequiredService<IAlertService>();
        var notifier = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var result = await alerts.PruneToCapacityAsync(_opts.AlertRetentionCount, cancellationToken);
        if (!result.HasWork) return;

        await notifier.BroadcastAlertsPurgedAsync(
            result.PurgedCount, result.CutoffTimestamp, cancellationToken);

        BffMetrics.AlertsPurgedTotal.Add(result.PurgedCount);
    }

    /// <summary>
    /// §3.3 OnFleetUpdate — the dashboard's warm reload after a reconnect. Only
    /// broadcast when a state timestamp advanced inside the window.
    /// </summary>
    /// <remarks>
    /// Cost note: this reads every known truck state per tick (one Redis GET each),
    /// which makes it the dominant cost of this service at fleet scale. The interval is
    /// therefore configurable and the broadcast is skipped when nothing changed. A
    /// Redis-side change fingerprint would remove the fan-out read if this ever shows
    /// up in a profile.
    /// </remarks>
    internal async Task BroadcastFleetUpdateAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var states   = scope.ServiceProvider.GetRequiredService<ITruckStateStore>();
        var notifier = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var snapshot = await states.GetAllStatesAsync(cancellationToken);
        if (snapshot.Count == 0) return;

        var newest = DateTime.MinValue;
        foreach (var state in snapshot)
            if (state.Timestamp > newest) newest = state.Timestamp;

        if (newest <= _lastSeenChange) return;
        _lastSeenChange = newest;

        await notifier.BroadcastFleetUpdateAsync(snapshot, cancellationToken);
        BffMetrics.FleetUpdatesTotal.Add(1);
    }
}
