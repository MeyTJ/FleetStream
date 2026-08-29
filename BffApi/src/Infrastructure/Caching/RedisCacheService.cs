using System.Text.Json;
using FleetStream.Application.Abstractions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace FleetStream.Infrastructure.Caching;

/// <summary>
/// Redis-based cache service for high-performance data access.
/// </summary>
public class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _database;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public RedisCacheService(
        IConnectionMultiplexer redis,
        ILogger<RedisCacheService> logger)
    {
        _redis = redis;
        _database = redis.GetDatabase();
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            var value = await _database.StringGetAsync(key);
            if (value.IsNullOrEmpty)
            {
                _logger.LogDebug("Cache miss for key: {Key}", key);
                return null;
            }

            _logger.LogDebug("Cache hit for key: {Key}", key);
            return JsonSerializer.Deserialize<T>((string)value!, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache key: {Key}", key);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            var serialized = JsonSerializer.Serialize(value, _jsonOptions);
            await _database.StringSetAsync(key, serialized, expiration ?? TimeSpan.FromMinutes(5));
            _logger.LogDebug("Cache set for key: {Key}, expiration: {Expiration}", key, expiration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting cache key: {Key}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await _database.KeyDeleteAsync(key);
            _logger.LogDebug("Cache removed for key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache key: {Key}", key);
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _database.KeyExistsAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking cache key existence: {Key}", key);
            return false;
        }
    }

    public async Task RefreshAsync(string key, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await _database.KeyExpireAsync(key, expiration ?? TimeSpan.FromMinutes(5));
            _logger.LogDebug("Cache refreshed for key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing cache key: {Key}", key);
        }
    }

    public async Task<IReadOnlyList<string>> GetKeysAsync(string pattern, CancellationToken cancellationToken = default)
    {
        try
        {
            var keys = new List<string>();
            var endpoints = _redis.GetEndPoints();
            var server = _redis.GetServer(endpoints.First());
            
            await foreach (var key in server.KeysAsync(pattern: pattern))
            {
                keys.Add(key.ToString());
            }

            return keys;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache keys with pattern: {Pattern}", pattern);
            return Array.Empty<string>();
        }
    }
}
