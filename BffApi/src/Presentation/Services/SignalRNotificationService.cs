using FleetStream.Application.Abstractions;
using FleetStream.Core.Domain.Entities;
using FleetStream.Infrastructure.Metrics;
using FleetStream.Presentation.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace FleetStream.Presentation.Services;

/// <summary>
/// Routes hub events to their protocol §3.5 target groups. Typed sends go
/// through IHubContext&lt;FleetHub, IFleetHubClient&gt;; the untyped context backs
/// SendToGroupAsync/SendToUserAsync where the method name is dynamic.
/// </summary>
public sealed class SignalRNotificationService : INotificationService
{
    private readonly IHubContext<FleetHub, IFleetHubClient> _typedHub;
    private readonly IHubContext<FleetHub> _hub;
    private readonly ILogger<SignalRNotificationService> _logger;

    public SignalRNotificationService(
        IHubContext<FleetHub, IFleetHubClient> typedHub,
        IHubContext<FleetHub> hub,
        ILogger<SignalRNotificationService> logger)
    {
        _typedHub = typedHub;
        _hub      = hub;
        _logger   = logger;
    }

    public async Task BroadcastTelemetryUpdateAsync(TruckTelemetry telemetry, CancellationToken cancellationToken = default)
    {
        try
        {
            // §3.3 OnTelemetrySample — high-rate stream for the telemetry:full group (admins).
            await _typedHub.Clients.Group(FleetHubGroups.TelemetryFull).OnTelemetrySample(telemetry);
            RecordOutbound(nameof(IFleetHubClient.OnTelemetrySample));
            _logger.LogDebug("Broadcasted telemetry sample for truck {TruckId}", telemetry.TruckId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting telemetry update for truck {TruckId}", telemetry.TruckId);
        }
    }

    public async Task BroadcastTruckStateAsync(TruckState state, CancellationToken cancellationToken = default)
    {
        try
        {
            // §3.3 OnTruckStateUpdate — fleet (all clients) + truck:{truckId} subscribers.
            await _typedHub.Clients.Group(FleetHubGroups.Fleet).OnTruckStateUpdate(state);
            await _typedHub.Clients.Group(FleetHubGroups.Truck(state.TruckId)).OnTruckStateUpdate(state);
            RecordOutbound(nameof(IFleetHubClient.OnTruckStateUpdate), 2);
            _logger.LogDebug("Broadcasted state update for truck {TruckId}", state.TruckId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting state update for truck {TruckId}", state.TruckId);
        }
    }

    public async Task BroadcastAlertAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        try
        {
            // §3.3 OnAlert — alerts group (auto-joined by every client, §3.4).
            await _typedHub.Clients.Group(FleetHubGroups.Alerts).OnAlert(alert);
            RecordOutbound(nameof(IFleetHubClient.OnAlert));
            _logger.LogInformation("Broadcasted alert {AlertId} for truck {TruckId}", alert.Id, alert.TruckId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting alert for truck {TruckId}", alert.TruckId);
        }
    }

    public async Task BroadcastFleetUpdateAsync(IEnumerable<TruckState> states, CancellationToken cancellationToken = default)
    {
        try
        {
            var stateList = states.ToList();
            await _typedHub.Clients.Group(FleetHubGroups.Fleet).OnFleetUpdate(stateList);
            RecordOutbound(nameof(IFleetHubClient.OnFleetUpdate));
            _logger.LogDebug("Broadcasted fleet update with {Count} states", stateList.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting fleet update");
        }
    }

    public async Task SendToGroupAsync(string groupName, string method, object payload, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hub.Clients.Group(groupName).SendAsync(method, payload, cancellationToken);
            _logger.LogDebug("Sent message to group {Group} via method {Method}", groupName, method);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to group {Group}", groupName);
        }
    }

    public async Task SendToUserAsync(string userId, string method, object payload, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hub.Clients.User(userId).SendAsync(method, payload, cancellationToken);
            _logger.LogDebug("Sent message to user {UserId} via method {Method}", userId, method);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to user {UserId}", userId);
        }
    }

    /// <summary>
    /// §3.3 OnAlertsPurged — the alerts group is auto-joined by every connection
    /// (§3.4), so the whole client fleet is trimmed in lock-step with the server.
    /// </summary>
    public async Task BroadcastAlertsPurgedAsync(
        int count, DateTime beforeTimestamp, CancellationToken cancellationToken = default)
    {
        try
        {
            await _typedHub.Clients.Group(FleetHubGroups.Alerts)
                .OnAlertsPurged(count, AsUtc(beforeTimestamp));
            RecordOutbound(nameof(IFleetHubClient.OnAlertsPurged));
            _logger.LogInformation("Broadcasted alert purge: {Count} before {Cutoff:O}",
                count, AsUtc(beforeTimestamp));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting alerts-purged notification");
        }
    }

    /// <summary>§3.3 OnPresenceChange — broadcast to the fleet group.</summary>
    public async Task BroadcastPresenceChangeAsync(
        string truckId, bool isOnline, CancellationToken cancellationToken = default)
    {
        try
        {
            await _typedHub.Clients.Group(FleetHubGroups.Fleet)
                .OnPresenceChange(truckId, isOnline);
            RecordOutbound(nameof(IFleetHubClient.OnPresenceChange));
            _logger.LogInformation("Broadcasted presence change for truck {TruckId}: {IsOnline}",
                truckId, isOnline);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting presence change for truck {TruckId}", truckId);
        }
    }

    /// <summary>
    /// §3.3 OnSystemMessage — ops notice to every connected client. Severity is
    /// normalized so the UI never has to branch on free-form casing.
    /// </summary>
    public async Task BroadcastSystemMessageAsync(
        string severity, string code, string message, CancellationToken cancellationToken = default)
    {
        try
        {
            var timestamp = AsUtc(DateTime.UtcNow);
            await _typedHub.Clients.All
                .OnSystemMessage(NormalizeSeverity(severity), code, message, timestamp);
            RecordOutbound(nameof(IFleetHubClient.OnSystemMessage));
            _logger.LogInformation("Broadcasted system message {Code} ({Severity})",
                code, NormalizeSeverity(severity));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting system message {Code}", code);
        }
    }

    /// <summary>
    /// SignalR serializes DateTime via System.Text.Json, which emits the kind it
    /// carries. An Unspecified value would produce a timestamp the client parses in
    /// local time, silently skewing the purge cutoff, so force UTC before sending.
    /// </summary>
    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc   => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _                  => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private static string NormalizeSeverity(string severity) => severity?.ToLowerInvariant() switch
    {
        "warn" or "warning" => "warn",
        "error" or "err"    => "error",
        _                   => "info",
    };

    private static void RecordOutbound(string method, int messages = 1) =>
        BffMetrics.SignalRMessagesTotal.Add(messages,
            new KeyValuePair<string, object?>("direction", "outbound"),
            new KeyValuePair<string, object?>("method", method));
}
