namespace FleetStream.Application.Features.FleetSummary.Get;

/// <summary>
/// Wire-shape DTO for the <c>GET /api/v1/fleet/summary</c> response. The
/// Application layer owns the schema; the Presentation layer is a thin
/// pass-through that adds HTTP concerns.
/// </summary>
public sealed record FleetSummaryDto(
    int    TotalTrucks,
    long   OnlineTrucks,
    int    MovingTrucks,
    long   IdleTrucks,
    int    AtRiskTrucks,
    double AverageSpeed,
    double AverageFuelLevel,
    DateTime GeneratedAt);