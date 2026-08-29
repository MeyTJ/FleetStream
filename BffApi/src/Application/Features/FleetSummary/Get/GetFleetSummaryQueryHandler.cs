using FleetStream.Application.Abstractions;
using FleetStream.Application.Shared.Messaging;
using Microsoft.Extensions.Logging;

namespace FleetStream.Application.Features.FleetSummary.Get;

/// <summary>
/// Reads the fleet-wide summary from the cache when available, falling back
/// to the truck repository and the state store. The handler is intentionally
/// thin: the cache key, the compute, and the in-memory reduction live here so
/// the read path is testable without a real Redis.
/// </summary>
public sealed class GetFleetSummaryQueryHandler : IQueryHandler<GetFleetSummaryQuery, FleetSummaryDto>
{
    private readonly ITruckRepository _trucks;
    private readonly ITruckStateStore _states;
    private readonly ICacheService _cache;
    private readonly TimeProvider _clock;
    private readonly ILogger<GetFleetSummaryQueryHandler> _logger;

    public GetFleetSummaryQueryHandler(
        ITruckRepository trucks,
        ITruckStateStore states,
        ICacheService cache,
        TimeProvider clock,
        ILogger<GetFleetSummaryQueryHandler> logger)
    {
        _trucks = trucks;
        _states = states;
        _cache  = cache;
        _clock  = clock;
        _logger = logger;
    }

    public async Task<FleetSummaryDto> Handle(GetFleetSummaryQuery query, CancellationToken cancellationToken)
    {
        const string cacheKey = "fleet:summary";

        var cached = await _cache.GetAsync<FleetSummaryDto>(cacheKey, cancellationToken);
        if (cached is not null)
            return cached;

        var total   = await _trucks.CountAsync(cancellationToken);
        var online  = await _states.GetOnlineCountAsync(cancellationToken);
        var states  = await _states.GetAllStatesAsync(cancellationToken);
        var moving  = states.Count(s => s.IsMoving);
        var atRisk  = states.Count(s => s.RiskLevel is "High" or "Critical");

        var summary = new FleetSummaryDto(
            TotalTrucks:      total,
            OnlineTrucks:     online,
            MovingTrucks:     moving,
            IdleTrucks:       online - moving,
            AtRiskTrucks:     atRisk,
            AverageSpeed:     states.Count > 0 ? states.Average(s => s.SpeedKmh) : 0d,
            AverageFuelLevel: states.Count > 0 ? states.Average(s => s.FuelLevelPercent) : 0d,
            GeneratedAt:      _clock.GetUtcNow().UtcDateTime);

        await _cache.SetAsync(cacheKey, summary, TimeSpan.FromSeconds(5), cancellationToken);
        _logger.LogDebug("Recomputed fleet summary (total={Total}, online={Online}, moving={Moving})",
            total, online, moving);
        return summary;
    }
}