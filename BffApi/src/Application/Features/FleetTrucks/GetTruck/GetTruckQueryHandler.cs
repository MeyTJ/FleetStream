using FleetStream.Application.Abstractions;
using FleetStream.Application.Shared.Messaging;
using FleetStream.Core.Domain.Entities;

namespace FleetStream.Application.Features.FleetTrucks.GetTruck;

public sealed class GetTruckQueryHandler : IQueryHandler<GetTruckQuery, Truck?>
{
    private readonly ITruckRepository _trucks;

    public GetTruckQueryHandler(ITruckRepository trucks) => _trucks = trucks;

    public Task<Truck?> Handle(GetTruckQuery query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.TruckId))
            throw new ArgumentException("TruckId is required.", nameof(query));
        return _trucks.GetByIdAsync(query.TruckId, cancellationToken);
    }
}