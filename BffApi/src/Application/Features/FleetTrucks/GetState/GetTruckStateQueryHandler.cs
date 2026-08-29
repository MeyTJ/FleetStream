using FleetStream.Application.Abstractions;
using FleetStream.Application.Shared.Messaging;
using FleetStream.Core.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FleetStream.Application.Features.FleetTrucks.GetState;

public sealed class GetTruckStateQueryHandler : IQueryHandler<GetTruckStateQuery, TruckState?>
{
    private readonly ITruckStateStore _states;
    private readonly ICacheService _cache;
    private readonly ILogger<GetTruckStateQueryHandler> _logger;

    public GetTruckStateQueryHandler(
        ITruckStateStore states,
        ICacheService cache,
        ILogger<GetTruckStateQueryHandler> logger)
    {
        _states = states;
        _cache  = cache;
        _logger = logger;
    }

    public async Task<TruckState?> Handle(GetTruckStateQuery query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.TruckId))
            throw new ArgumentException("TruckId is required.", nameof(query));

        var cacheKey = $"truck:state:{query.TruckId}";
        var cached = await _cache.GetAsync<TruckState>(cacheKey, cancellationToken);
        if (cached is not null)
            return cached;

        var state = await _states.GetStateAsync(query.TruckId, cancellationToken);
        if (state is null)
        {
            _logger.LogWarning("Truck state not found: {TruckId}", query.TruckId);
            return null;
        }

        await _cache.SetAsync(cacheKey, state, TimeSpan.FromSeconds(10), cancellationToken);
        return state;
    }
}