using System.ComponentModel.DataAnnotations;
using FleetStream.Application.Abstractions;
using FleetStream.Application.Shared.Messaging;
using FleetStream.Application.Shared.Pagination;
using FleetStream.Core.Domain.Entities;

namespace FleetStream.Application.Features.FleetAlerts.List;

public sealed record ListAlertsQuery(
    string? Cursor = null,
    [property: Range(1, 500)] int PageSize = 100,
    string? Severity = null,
    string? TruckId = null,
    bool OnlyActive = true);

public sealed class ListAlertsQueryHandler : IQueryHandler<ListAlertsQuery, Page<Alert>>
{
    private readonly IAlertService _alerts;

    public ListAlertsQueryHandler(IAlertService alerts) => _alerts = alerts;

    public async Task<Page<Alert>> Handle(ListAlertsQuery query, CancellationToken cancellationToken)
    {
        var severities = ParseSeverities(query.Severity);
        var all = !string.IsNullOrWhiteSpace(query.TruckId)
            ? await _alerts.GetAlertsForTruckAsync(query.TruckId, 0, 10_000, cancellationToken)
            : await _alerts.GetActiveAlertsAsync(0, 10_000, cancellationToken);

        var filtered = all
            .Where(a => !query.OnlyActive || !a.IsAcknowledged)
            .Where(a => severities.Count == 0 || severities.Contains(a.Severity, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(a => a.Timestamp)
            .ThenByDescending(a => a.Id)
            .ToList();

        var startIndex = 0;
        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            var decoded = CursorEncoder.Decode<CursorEncoder.AlertCursor>(query.Cursor);
            if (decoded is not null)
            {
                var cursorTs = DateTime.Parse(decoded.Ts, null, System.Globalization.DateTimeStyles.RoundtripKind);
                startIndex = filtered.FindIndex(a =>
                    a.Timestamp < cursorTs ||
                    (a.Timestamp == cursorTs && string.CompareOrdinal(a.Id, decoded.Id) < 0));
                if (startIndex < 0) startIndex = filtered.Count;
            }
        }

        var take = query.PageSize;
        var slice = filtered.Skip(startIndex).Take(take + 1).ToList();
        var hasMore = slice.Count > take;
        if (hasMore) slice.RemoveAt(slice.Count - 1);

        string? nextCursor = null;
        if (hasMore && slice.Count > 0)
        {
            var last = slice[^1];
            nextCursor = CursorEncoder.Encode(new CursorEncoder.AlertCursor(
                last.Id,
                last.Timestamp.ToUniversalTime().ToString("O")));
        }

        return new Page<Alert>
        {
            Items      = slice,
            PageSize   = query.PageSize,
            HasMore    = hasMore,
            NextCursor = nextCursor,
        };
    }

    private static HashSet<string> ParseSeverities(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
