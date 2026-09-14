using System.Text.Json;
using FleetStream.Application.Abstractions;
using FleetStream.Core.Domain.Entities;
using FleetStream.Infrastructure.Metrics;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace FleetStream.Infrastructure.Services;

/// <summary>
/// Redis-based truck state store implementation.
/// </summary>
public class RedisTruckStateStore : ITruckStateStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _database;
    private readonly ILogger<RedisTruckStateStore> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    private const string KeyPrefix = "truck:state:";
    private const string OnlineSetKey = "trucks:online";
    private const string MovingSetKey = "trucks:moving";

    /// <summary>
    /// Durable index of every truck we hold a state for. Snapshots read this rather
    /// than <see cref="OnlineSetKey"/> so a truck that has gone quiet still appears
    /// on the map, greyed out (§3.8), instead of vanishing entirely.
    /// </summary>
    private const string KnownSetKey = "trucks:known";

    /// <summary>
    /// Lifetime of a truck state key. Refreshed on every telemetry tick, so a truck
    /// that has genuinely disappeared drops out of the store after 24 h.
    /// </summary>
    private static readonly TimeSpan StateTtl = TimeSpan.FromHours(24);

    public RedisTruckStateStore(
        IConnectionMultiplexer redis,
        ILogger<RedisTruckStateStore> logger)
    {
        _redis = redis;
        _database = redis.GetDatabase();
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<TruckState?> GetStateAsync(string truckId, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = KeyPrefix + truckId;
            var value = await _database.StringGetAsync(key);
            
            if (value.IsNullOrEmpty)
                return null;

            RecordRedisOp("get", "success");
            return JsonSerializer.Deserialize<TruckState>((string)value!, _jsonOptions);
        }
        catch (Exception ex)
        {
            RecordRedisOp("get", "error");
            _logger.LogError(ex, "Error getting truck state for {TruckId}", truckId);
            return null;
        }
    }

    public async Task SetStateAsync(TruckState state, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = KeyPrefix + state.TruckId;
            var serialized = JsonSerializer.Serialize(state, _jsonOptions);
            
            // Set with 24-hour TTL for inactive trucks
            await _database.StringSetAsync(key, serialized, StateTtl);
            
            // Update online status
            await _database.SetAddAsync(OnlineSetKey, state.TruckId);
            await _database.KeyExpireAsync(OnlineSetKey, StateTtl);

            // Track the truck for snapshots even after it goes quiet (§3.8).
            await _database.SetAddAsync(KnownSetKey, state.TruckId);
            await _database.KeyExpireAsync(KnownSetKey, StateTtl);
            
            // Update moving status
            if (state.IsMoving)
            {
                await _database.SetAddAsync(MovingSetKey, state.TruckId);
            }
            else
            {
                await _database.SetRemoveAsync(MovingSetKey, state.TruckId);
            }

            RecordRedisOp("set", "success");
            _logger.LogDebug("Updated state for truck {TruckId}", state.TruckId);
        }
        catch (Exception ex)
        {
            RecordRedisOp("set", "error");
            _logger.LogError(ex, "Error setting truck state for {TruckId}", state.TruckId);
            throw;
        }
    }

    public async Task<IReadOnlyList<TruckState>> GetAllStatesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var states = new List<TruckState>();
            // Read the durable index: offline trucks must still appear (greyed out).
            var truckIds = await _database.SetMembersAsync(KnownSetKey);
            
            foreach (var truckId in truckIds)
            {
                var state = await GetStateAsync(truckId.ToString(), cancellationToken);
                if (state != null)
                    states.Add(state);
            }

            return states;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all truck states");
            return Array.Empty<TruckState>();
        }
    }

    public async Task<IReadOnlyList<TruckState>> GetStatesByIdsAsync(IEnumerable<string> truckIds, CancellationToken cancellationToken = default)
    {
        try
        {
            var tasks = truckIds.Select(id => GetStateAsync(id, cancellationToken));
            var results = await Task.WhenAll(tasks);
            return results.Where(s => s != null).Cast<TruckState>().ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting truck states by IDs");
            return Array.Empty<TruckState>();
        }
    }

    public async Task RemoveStateAsync(string truckId, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = KeyPrefix + truckId;
            await _database.KeyDeleteAsync(key);
            await _database.SetRemoveAsync(OnlineSetKey, truckId);
            await _database.SetRemoveAsync(MovingSetKey, truckId);
            await _database.SetRemoveAsync(KnownSetKey, truckId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing truck state for {TruckId}", truckId);
        }
    }

    public async Task<bool> ExistsAsync(string truckId, CancellationToken cancellationToken = default)
    {
        return await _database.KeyExistsAsync(KeyPrefix + truckId);
    }

    public async Task<long> GetOnlineCountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _database.SetLengthAsync(OnlineSetKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting online truck count");
            return 0;
        }
    }

    /// <summary>
    /// §3.3 OnPresenceChange — the online SET is written on every telemetry tick, so
    /// a truck whose state has aged past <paramref name="olderThan"/> has gone quiet.
    /// </summary>
    public async Task<IReadOnlyList<TruckState>> GetStaleOnlineStatesAsync(
        TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        try
        {
            var cutoff = DateTime.UtcNow - olderThan;
            var stale  = new List<TruckState>();
            var truckIds = await _database.SetMembersAsync(OnlineSetKey);

            foreach (var raw in truckIds)
            {
                var truckId = raw.ToString();
                var state = await GetStateAsync(truckId, cancellationToken);

                // A missing key means the 24 h TTL lapsed: the truck is gone, not stale.
                if (state is null) continue;

                if (state.Timestamp < cutoff)
                    stale.Add(state);
            }

            return stale;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning stale online trucks");
            return Array.Empty<TruckState>();
        }
    }

    /// <summary>
    /// Flip a truck offline while keeping its last position, so the map greys it out
    /// instead of dropping it (§3.8).
    /// </summary>
    public async Task MarkOfflineAsync(string truckId, CancellationToken cancellationToken = default)
    {
        try
        {
            var state = await GetStateAsync(truckId, cancellationToken);
            if (state is null) return;

            state.IsOnline = false;
            state.IsMoving = false;

            var key        = KeyPrefix + truckId;
            var serialized = JsonSerializer.Serialize(state, _jsonOptions);

            var tx = _database.CreateTransaction();
            _ = tx.StringSetAsync(key, serialized, StateTtl);
            _ = tx.SetRemoveAsync(OnlineSetKey, truckId);
            _ = tx.SetRemoveAsync(MovingSetKey, truckId);
            await tx.ExecuteAsync();

            RecordRedisOp("mark_offline", "success");
        }
        catch (Exception ex)
        {
            RecordRedisOp("mark_offline", "error");
            _logger.LogError(ex, "Error marking truck {TruckId} offline", truckId);
        }
    }

    private static void RecordRedisOp(string op, string result) =>
        BffMetrics.RedisOperationsTotal.Add(1,
            new KeyValuePair<string, object?>("op", op),
            new KeyValuePair<string, object?>("result", result));
}
