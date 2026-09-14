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

    private static void RecordOutbound(string method, int messages = 1) =>
        BffMetrics.SignalRMessagesTotal.Add(messages,
            new KeyValuePair<string, object?>("direction", "outbound"),
            new KeyValuePair<string, object?>("method", method));
}
