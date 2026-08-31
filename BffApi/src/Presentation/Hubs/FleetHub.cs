using FleetStream.Application.Abstractions;
using FleetStream.Core.Domain.Entities;
using FleetStream.Infrastructure.Metrics;
using FleetStream.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace FleetStream.Presentation.Hubs;

public interface IFleetHubClient
{
    Task OnTelemetryUpdate(TruckTelemetry telemetry);
    Task OnTruckStateUpdate(TruckState state);
    Task OnAlert(Alert alert);
    Task OnFleetUpdate(IReadOnlyList<TruckState> states);
}

[Authorize]
public sealed class FleetHub : Hub<IFleetHubClient>
{
    private readonly ILogger<FleetHub> _logger;

    public FleetHub(ILogger<FleetHub> logger) => _logger = logger;

    [Authorize(Policy = "FleetReader")]
    public async Task JoinFleetGroup()
    {
        BffMetrics.SignalRMessagesTotal.Add(1,
            new KeyValuePair<string, object?>("direction", "inbound"),
            new KeyValuePair<string, object?>("method", nameof(JoinFleetGroup)));

        await Groups.AddToGroupAsync(Context.ConnectionId, "fleet");
        _logger.LogInformation("Client {ConnectionId} joined fleet group", Context.ConnectionId);
    }

    [Authorize(Policy = "FleetReader")]
    public async Task JoinTruckGroup(string truckId)
    {
        BffMetrics.SignalRMessagesTotal.Add(1,
            new KeyValuePair<string, object?>("direction", "inbound"),
            new KeyValuePair<string, object?>("method", nameof(JoinTruckGroup)));

        if (!TruckIdValidation.IsValid(truckId))
        {
            BffMetrics.SignalRMessagesDroppedTotal.Add(1,
                new KeyValuePair<string, object?>("reason", "invalid_truck_id"));
            throw new HubException("Invalid truck ID.");
        }

        if (!TruckIdValidation.IsAllowedForUser(truckId, Context.User))
        {
            BffMetrics.SignalRMessagesDroppedTotal.Add(1,
                new KeyValuePair<string, object?>("reason", "forbidden_truck_id"));
            throw new HubException("Access denied for truck.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"truck:{truckId}");
        _logger.LogInformation("Client {ConnectionId} joined truck group {TruckId}", Context.ConnectionId, truckId);
    }

    [Authorize(Policy = "FleetReader")]
    public async Task LeaveTruckGroup(string truckId)
    {
        BffMetrics.SignalRMessagesTotal.Add(1,
            new KeyValuePair<string, object?>("direction", "inbound"),
            new KeyValuePair<string, object?>("method", nameof(LeaveTruckGroup)));

        if (!TruckIdValidation.IsValid(truckId))
            throw new HubException("Invalid truck ID.");

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"truck:{truckId}");
        _logger.LogInformation("Client {ConnectionId} left truck group {TruckId}", Context.ConnectionId, truckId);
    }

    public override async Task OnConnectedAsync()
    {
        BffMetrics.IncrementConnections();
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        BffMetrics.DecrementConnections();
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
