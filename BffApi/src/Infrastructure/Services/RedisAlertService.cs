using System.Text.Json;
using FleetStream.Application.Abstractions;
using FleetStream.Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace FleetStream.Infrastructure.Services;

/// <summary>
/// Phase-3 implementation of <see cref="IAlertService"/>. Uses Redis strings
/// keyed by alert id as the canonical store. Active alerts are tracked in a
/// Redis SET; per-truck indexes live as JSON arrays in a HASH.
/// </summary>
public sealed class RedisAlertService : IAlertService
{
    private const string KeyPrefix     = "alert:";
    private const string ActiveSetKey  = "alerts:active";
    private const string TruckIndexKey = "alerts:by-truck";

    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ILogger<RedisAlertService> _logger;
    private readonly JsonSerializerOptions _json;

    public RedisAlertService(IConnectionMultiplexer redis, ILogger<RedisAlertService> logger)
    {
        _redis  = redis;
        _db     = redis.GetDatabase();
        _logger = logger;
        _json   = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented        = false,
        };
    }

    public async Task<Alert?> GetAlertAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var raw = await _db.StringGetAsync(KeyPrefix + id);
            if (raw.IsNullOrEmpty) return null;
            return JsonSerializer.Deserialize<Alert>((string)raw!, _json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetAlertAsync({AlertId}) failed", id);
            return null;
        }
    }

    public async Task<IReadOnlyList<Alert>> GetActiveAlertsAsync(
        int skip = 0, int take = 100, CancellationToken cancellationToken = default)
    {
        try
        {
            var ids = await _db.SetMembersAsync(ActiveSetKey);
            if (ids.Length == 0) return Array.Empty<Alert>();
            var ordered = ids.Select(r => (string)r!).OrderByDescending(s => s).ToArray();
            var slice = ordered.Skip(skip).Take(take);
            var results = await Task.WhenAll(slice.Select(id => GetAlertAsync(id, cancellationToken)));
            return results.Where(a => a is not null).Cast<Alert>().ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetActiveAlertsAsync failed");
            return Array.Empty<Alert>();
        }
    }

    public async Task<IReadOnlyList<Alert>> GetAlertsForTruckAsync(
        string truckId, int skip = 0, int take = 100, CancellationToken cancellationToken = default)
    {
        try
        {
            var ids = await _db.HashGetAsync(TruckIndexKey, truckId);
            if (ids.IsNullOrEmpty) return Array.Empty<Alert>();
            var parsed = JsonSerializer.Deserialize<string[]>((string)ids!, _json) ?? Array.Empty<string>();
            var slice = parsed.Skip(skip).Take(take);
            var results = await Task.WhenAll(slice.Select(id => GetAlertAsync(id, cancellationToken)));
            return results.Where(a => a is not null).Cast<Alert>().ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetAlertsForTruckAsync({TruckId}) failed", truckId);
            return Array.Empty<Alert>();
        }
    }

    public async Task CreateAlertAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(alert, _json);
            var tx = _db.CreateTransaction();
            _ = tx.StringSetAsync(KeyPrefix + alert.Id, json, TimeSpan.FromDays(7));
            _ = tx.SetAddAsync(ActiveSetKey, alert.Id);
            await tx.ExecuteAsync();

            var existing = await _db.HashGetAsync(TruckIndexKey, alert.TruckId);
            var idList = existing.IsNullOrEmpty
                ? new List<string> { alert.Id }
                : JsonSerializer.Deserialize<List<string>>((string)existing!, _json) ?? new List<string>();
            if (!idList.Contains(alert.Id)) idList.Insert(0, alert.Id);
            await _db.HashSetAsync(TruckIndexKey, alert.TruckId, JsonSerializer.Serialize(idList, _json));
            _logger.LogInformation("Created alert {AlertId} for truck {TruckId}", alert.Id, alert.TruckId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateAlertAsync({AlertId}) failed", alert.Id);
            throw;
        }
    }

    public async Task AcknowledgeAlertAsync(
        string id, string acknowledgedBy, CancellationToken cancellationToken = default)
    {
        var alert = await GetAlertAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Alert '{id}' not found.");
        if (alert.IsAcknowledged)
            throw new InvalidOperationException(
                $"Alert '{id}' is already acknowledged by '{alert.AcknowledgedBy}'.");

        alert.IsAcknowledged = true;
        alert.AcknowledgedBy = acknowledgedBy;
        alert.AcknowledgedAt = DateTime.UtcNow;

        var json = JsonSerializer.Serialize(alert, _json);
        var tx = _db.CreateTransaction();
        _ = tx.StringSetAsync(KeyPrefix + alert.Id, json, TimeSpan.FromDays(7));
        _ = tx.SetRemoveAsync(ActiveSetKey, alert.Id);
        await tx.ExecuteAsync();
        _logger.LogInformation("Alert {AlertId} acknowledged by {Subject}", id, acknowledgedBy);
    }

    public async Task<int> GetActiveAlertCountAsync(CancellationToken cancellationToken = default)
    {
        try { return (int)await _db.SetLengthAsync(ActiveSetKey); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetActiveAlertCountAsync failed");
            return 0;
        }
    }
}

