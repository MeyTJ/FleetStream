namespace FleetStream.Application.Features.FleetSummary.Get;

/// <summary>
/// Marker record for the <c>GET /api/v1/fleet/summary</c> query. Carries no
/// state today; introduced as a record so the call site reads as a request
/// type and future filters (region, time window, …) can be added without
/// changing the handler signature.
/// </summary>
public sealed record GetFleetSummaryQuery;