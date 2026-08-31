using System.ComponentModel.DataAnnotations;
using FleetStream.Application.Abstractions;
using FleetStream.Application.Shared.Messaging;
using FleetStream.Application.Shared.Pagination;
using FleetStream.Core.Domain.Entities;

namespace FleetStream.Application.Features.FleetTrucks.List;

public sealed record GetTruckStatesQuery(
    string? Cursor = null,
    [property: Range(1, 200)] int PageSize = 50,
    string? Status = null);

public sealed class GetTruckStatesQueryHandler : IQueryHandler<GetTruckStatesQuery, Page<TruckState>>
{
    private readonly ITruckStateStore _states;

    public GetTruckStatesQueryHandler(ITruckStateStore states) => _states = states;

    public async Task<Page<TruckState>> Handle(
        GetTruckStatesQuery query,
        CancellationToken cancellationToken)
    {
        var all = await _states.GetAllStatesAsync(cancellationToken);
        var ordered = all.OrderBy(s => s.TruckId).ToList();

        var startIndex = 0;
        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            var decoded = CursorEncoder.Decode<CursorEncoder.TruckCursor>(query.Cursor);
            if (decoded is not null)
            {
                var idx = ordered.FindIndex(s => string.CompareOrdinal(s.TruckId, decoded.Id) > 0);
                startIndex = idx >= 0 ? idx : ordered.Count;
            }
        }

        var take = query.PageSize;
        var slice = ordered.Skip(startIndex).Take(take + 1).ToList();
        var hasMore = slice.Count > take;
        if (hasMore) slice.RemoveAt(slice.Count - 1);

        string? nextCursor = null;
        if (hasMore && slice.Count > 0)
            nextCursor = CursorEncoder.Encode(new CursorEncoder.TruckCursor(slice[^1].TruckId));

        return new Page<TruckState>
        {
            Items      = slice,
            PageSize   = query.PageSize,
            HasMore    = hasMore,
            NextCursor = nextCursor,
        };
    }
}
