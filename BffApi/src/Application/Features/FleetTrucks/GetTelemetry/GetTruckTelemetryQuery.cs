using System.ComponentModel.DataAnnotations;
using FleetStream.Application.Abstractions;
using FleetStream.Application.Shared.Messaging;
using FleetStream.Core.Domain.Entities;

namespace FleetStream.Application.Features.FleetTrucks.GetTelemetry;

public sealed record GetTruckTelemetryQuery(
    string TruckId,
    DateTime? From = null,
    DateTime? To = null,
    [property: Range(1, 1000)] int Limit = 200);

public sealed class GetTruckTelemetryQueryHandler : IQueryHandler<GetTruckTelemetryQuery, IReadOnlyList<TruckTelemetry>>
{
    private static readonly TimeSpan MaxWindow = TimeSpan.FromHours(24);

    private readonly ITelemetryHistoryStore _history;

    public GetTruckTelemetryQueryHandler(ITelemetryHistoryStore history) => _history = history;

    public Task<IReadOnlyList<TruckTelemetry>> Handle(
        GetTruckTelemetryQuery query,
        CancellationToken cancellationToken)
    {
        var to = query.To ?? DateTime.UtcNow;
        var from = query.From ?? to.AddHours(-1);

        if (from >= to)
            throw new ArgumentException("from must be before to.");

        if (to - from > MaxWindow)
            throw new ArgumentException("time window must not exceed 24 hours.");

        return _history.GetHistoryAsync(query.TruckId, from, to, query.Limit, cancellationToken);
    }
}
