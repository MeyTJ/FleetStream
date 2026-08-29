using FleetStream.Application.Abstractions;
using FleetStream.Application.Shared.Messaging;
using FleetStream.Core.Domain.Entities;

namespace FleetStream.Application.Features.FleetTrucks.List;

public sealed class GetTruckStatesQueryHandler : IQueryHandler<GetTruckStatesQuery, IReadOnlyList<TruckState>>
{
    private readonly ITruckStateStore _states;

    public GetTruckStatesQueryHandler(ITruckStateStore states) => _states = states;

    public async Task<IReadOnlyList<TruckState>> Handle(
        GetTruckStatesQuery query,
        CancellationToken cancellationToken)
    {
        var all = await _states.GetAllStatesAsync(cancellationToken);
        return all.OrderBy(s => s.TruckId)
                  .Skip(query.Skip)
                  .Take(query.Take)
                  .ToList();
    }
}