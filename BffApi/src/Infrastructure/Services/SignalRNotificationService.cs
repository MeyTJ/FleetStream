using FleetStream.Application.Abstractions;
using FleetStream.Core.Domain.Entities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace FleetStream.Infrastructure.Services;

/// <summary>
/// SignalR-based notification service for real-time updates.
/// </summary>
public class SignalRNotificationService : INotificationService
{
    private readonly IHubContext<FleetHub, IFleetHubClient> _hubContext;
    private readonly ILogger<SignalRNotificationService> _logger;

    public SignalRNotificationService(
        IHubContext<FleetHub, IFleetHubClient> hubContext,
        ILogger<SignalRNotificationService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task BroadcastTelemetryUpdateAsync(TruckTelemetry telemetry, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hubContext.Clients.All.OnTelemetryUpdate(telemetry);
            _logger.LogDebug("Broadcasted telemetry update for truck {TruckId}", telemetry.TruckId);
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
            await _hubContext.Clients.All.OnTruckStateUpdate(state);
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
            await _hubContext.Clients.All.OnAlert(alert);
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
            await _hubContext.Clients.All.OnFleetUpdate(stateList);
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
            // Use the untyped IClientProxy for dynamic method names by accessing Clients
            // through the untyped surface (clients typed to IFleetHubClient don't have SendAsync).
            var proxy = (IHubClients)_hubContext.Clients;
            await proxy.Group(groupName).SendAsync(method, payload, cancellationToken);
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
            var proxy = (IHubClients)_hubContext.Clients;
            await proxy.User(userId).SendAsync(method, payload, cancellationToken);
            _logger.LogDebug("Sent message to user {UserId} via method {Method}", userId, method);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to user {UserId}", userId);
        }
    }
}

/// <summary>
/// SignalR hub for fleet real-time updates.
/// </summary>
public class FleetHub : Hub<IFleetHubClient>
{
    private readonly ILogger<FleetHub> _logger;

    public FleetHub(ILogger<FleetHub> logger)
    {
        _logger = logger;
    }

    public async Task JoinFleetGroup()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "fleet");
        _logger.LogInformation("Client {ConnectionId} joined fleet group", Context.ConnectionId);
    }

    public async Task JoinTruckGroup(string truckId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"truck:{truckId}");
        _logger.LogInformation("Client {ConnectionId} joined truck group {TruckId}", Context.ConnectionId, truckId);
    }

    public async Task LeaveTruckGroup(string truckId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"truck:{truckId}");
        _logger.LogInformation("Client {ConnectionId} left truck group {TruckId}", Context.ConnectionId, truckId);
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}

/// <summary>
/// Interface for SignalR client callbacks.
/// </summary>
public interface IFleetHubClient
{
    Task OnTelemetryUpdate(TruckTelemetry telemetry);
    Task OnTruckStateUpdate(TruckState state);
    Task OnAlert(Alert alert);
    Task OnFleetUpdate(IReadOnlyList<TruckState> states);
}
