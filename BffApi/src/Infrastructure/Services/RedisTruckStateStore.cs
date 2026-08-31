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
            await _database.StringSetAsync(key, serialized, TimeSpan.FromHours(24));
            
            // Update online status
            await _database.SetAddAsync(OnlineSetKey, state.TruckId);
            await _database.KeyExpireAsync(OnlineSetKey, TimeSpan.FromHours(24));
            
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
            var truckIds = await _database.SetMembersAsync(OnlineSetKey);
            
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

    private static void RecordRedisOp(string op, string result) =>
        BffMetrics.RedisOperationsTotal.Add(1,
            new KeyValuePair<string, object?>("op", op),
            new KeyValuePair<string, object?>("result", result));
}
