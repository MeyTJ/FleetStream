using System.Security.Claims;
using FleetStream.Application.Abstractions;
using FleetStream.Core.Domain.Entities;
using FleetStream.Infrastructure.Metrics;
using FleetStream.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace FleetStream.Presentation.Hubs;

/// <summary>
/// The complete server-to-client contract of docs/03-signalr-protocol.md §3.3.
/// Parameter *order* is wire-visible: the TypeScript client registers positional
/// handlers (see frontend/src/lib/hooks/signalr-events.ts), so reordering an
/// existing signature is a breaking change.
/// </summary>
public interface IFleetHubClient
{
    Task OnTelemetrySample(TruckTelemetry telemetry);
    Task OnTruckStateUpdate(TruckState state);
    Task OnAlert(Alert alert);
    Task OnFleetUpdate(IReadOnlyList<TruckState> states);

    /// <summary>
    /// §3.3 — the server trimmed its alert retention to <paramref name="count"/>
    /// newest entries; everything strictly older than
    /// <paramref name="beforeTimestamp"/> was evicted.
    /// </summary>
    Task OnAlertsPurged(int count, DateTime beforeTimestamp);

    /// <summary>§3.3 — the sweeper observed an online/offline transition for a truck.</summary>
    Task OnPresenceChange(string truckId, bool isOnline);

    /// <summary>
    /// §3.3 — ops notice. <paramref name="severity"/> is one of info | warn | error;
    /// <paramref name="code"/> is a stable machine-readable key clients can branch on.
    /// </summary>
    Task OnSystemMessage(string severity, string code, string message, DateTime timestamp);
}

/// <summary>
/// Canonical SignalR group names and claim-based helpers
/// (docs/03-signalr-protocol.md §3.4–§3.5). Group membership is managed
/// server-side; clients cannot enumerate other group members.
/// </summary>
public static class FleetHubGroups
{
    public const string Fleet         = "fleet";
    public const string Alerts        = "alerts";
    public const string TelemetryFull = "telemetry:full";
    public const string SystemOps     = "system:ops";

    public const string RegionClaimType  = "region";
    public const string SubjectClaimType = "sub";
    public const string AdminRole        = "fleet:admin";

    public static string Truck(string truckId) => $"truck:{truckId}";
    public static string Region(string regionCode) => $"region:{regionCode}";
    public static string User(string userId) => $"user:{userId}";

    public static bool IsAdmin(ClaimsPrincipal? user) =>
        user?.IsInRole(AdminRole) == true;

    /// <summary>Region claim driving the region:{regionCode} dashboards (§3.5).</summary>
    public static string? RegionOf(ClaimsPrincipal? user)
    {
        var region = user?.FindFirst(RegionClaimType)?.Value;
        return string.IsNullOrWhiteSpace(region) ? null : region;
    }

    /// <summary>Subject claim; falls back to the (possibly mapped) name identifier.</summary>
    public static string? SubjectOf(ClaimsPrincipal? user, string? userIdentifier = null)
    {
        if (!string.IsNullOrWhiteSpace(userIdentifier))
            return userIdentifier;

        var sub = user?.FindFirst(SubjectClaimType)?.Value;
        if (!string.IsNullOrWhiteSpace(sub))
            return sub;

        var nameId = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return string.IsNullOrWhiteSpace(nameId) ? null : nameId;
    }
}

[Authorize]
public sealed class FleetHub : Hub<IFleetHubClient>
{
    private readonly ILogger<FleetHub> _logger;
    private readonly ITruckStateStore _states;

    public FleetHub(
        ILogger<FleetHub> logger,
        ITruckStateStore states)
    {
        _logger = logger;
        _states = states;
    }

    [Authorize(Policy = "FleetReader")]
    public async Task JoinFleetGroup()
    {
        BffMetrics.SignalRMessagesTotal.Add(1,
            new KeyValuePair<string, object?>("direction", "inbound"),
            new KeyValuePair<string, object?>("method", nameof(JoinFleetGroup)));

        await Groups.AddToGroupAsync(Context.ConnectionId, FleetHubGroups.Fleet, Context.ConnectionAborted);
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

        await Groups.AddToGroupAsync(Context.ConnectionId, FleetHubGroups.Truck(truckId), Context.ConnectionAborted);
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

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, FleetHubGroups.Truck(truckId), Context.ConnectionAborted);
        _logger.LogInformation("Client {ConnectionId} left truck group {TruckId}", Context.ConnectionId, truckId);
    }

    /// <summary>§3.4 — adds the connection to the alerts group (default-on; kept for explicit re-join).</summary>
    [Authorize(Policy = "FleetReader")]
    public async Task JoinAlertsGroup()
    {
        BffMetrics.SignalRMessagesTotal.Add(1,
            new KeyValuePair<string, object?>("direction", "inbound"),
            new KeyValuePair<string, object?>("method", nameof(JoinAlertsGroup)));

        await Groups.AddToGroupAsync(Context.ConnectionId, FleetHubGroups.Alerts, Context.ConnectionAborted);
        _logger.LogInformation("Client {ConnectionId} joined alerts group", Context.ConnectionId);
    }

    /// <summary>§3.4 — the server replies to the caller with OnFleetUpdate (current truck states).</summary>
    [Authorize(Policy = "FleetReader")]
    public async Task RequestSnapshot()
    {
        BffMetrics.SignalRMessagesTotal.Add(1,
            new KeyValuePair<string, object?>("direction", "inbound"),
            new KeyValuePair<string, object?>("method", nameof(RequestSnapshot)));

        var states = await _states.GetAllStatesAsync(Context.ConnectionAborted);
        await Clients.Caller.OnFleetUpdate(states);

        _logger.LogDebug("Fleet snapshot ({Count} states) sent to {ConnectionId}", states.Count, Context.ConnectionId);
    }

    /// <summary>§3.4 — no-op liveness probe for client-side monitoring.</summary>
    public Task Ping()
    {
        BffMetrics.SignalRMessagesTotal.Add(1,
            new KeyValuePair<string, object?>("direction", "inbound"),
            new KeyValuePair<string, object?>("method", nameof(Ping)));

        return Task.CompletedTask;
    }

    public override async Task OnConnectedAsync()
    {
        BffMetrics.IncrementConnections();

        // §3.4 default subscriptions — every connection is auto-joined.
        await Groups.AddToGroupAsync(Context.ConnectionId, FleetHubGroups.Fleet, Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, FleetHubGroups.Alerts, Context.ConnectionAborted);

        var subject = FleetHubGroups.SubjectOf(Context.User, Context.UserIdentifier);
        if (subject is not null)
            await Groups.AddToGroupAsync(Context.ConnectionId, FleetHubGroups.User(subject), Context.ConnectionAborted);

        // §3.5 claim-based groups: region dashboards + the admin telemetry stream.
        var region = FleetHubGroups.RegionOf(Context.User);
        if (region is not null)
            await Groups.AddToGroupAsync(Context.ConnectionId, FleetHubGroups.Region(region), Context.ConnectionAborted);

        var isAdmin = FleetHubGroups.IsAdmin(Context.User);
        if (isAdmin)
            await Groups.AddToGroupAsync(Context.ConnectionId, FleetHubGroups.TelemetryFull, Context.ConnectionAborted);

        _logger.LogInformation(
            "Client connected: {ConnectionId} (subject {Subject}, region {Region}, admin {IsAdmin})",
            Context.ConnectionId, subject ?? "<none>", region ?? "<none>", isAdmin);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        BffMetrics.DecrementConnections();
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
