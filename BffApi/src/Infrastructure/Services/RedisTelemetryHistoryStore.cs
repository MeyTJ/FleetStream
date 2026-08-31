using System.Text.Json;
using FleetStream.Application.Abstractions;
using FleetStream.Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace FleetStream.Infrastructure.Services;

public sealed class RedisTelemetryHistoryStore : ITelemetryHistoryStore
{
    private const int MaxSamplesPerTruck = 1_000;
    private static readonly TimeSpan IndexTtl = TimeSpan.FromHours(1);

    private readonly IDatabase _db;
    private readonly ILogger<RedisTelemetryHistoryStore> _logger;
    private readonly JsonSerializerOptions _json;

    public RedisTelemetryHistoryStore(IConnectionMultiplexer redis, ILogger<RedisTelemetryHistoryStore> logger)
    {
        _db     = redis.GetDatabase();
        _logger = logger;
        _json   = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    }

    public async Task AppendAsync(TruckTelemetry sample, CancellationToken cancellationToken = default)
    {
        try
        {
            var sampleKey = SampleKey(sample.TruckId, sample.Id);
            var indexKey  = IndexKey(sample.TruckId);
            var json      = JsonSerializer.Serialize(sample, _json);

            var tx = _db.CreateTransaction();
            _ = tx.StringSetAsync(sampleKey, json, IndexTtl);
            _ = tx.ListLeftPushAsync(indexKey, sample.Id);
            _ = tx.KeyExpireAsync(indexKey, IndexTtl);
            await tx.ExecuteAsync();

            await _db.ListTrimAsync(indexKey, 0, MaxSamplesPerTruck - 1);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AppendAsync({TruckId}) failed", sample.TruckId);
        }
    }

    public async Task<IReadOnlyList<TruckTelemetry>> GetHistoryAsync(
        string truckId,
        DateTime from,
        DateTime to,
        int limit,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var ids = await _db.ListRangeAsync(IndexKey(truckId), 0, MaxSamplesPerTruck - 1);
            if (ids.Length == 0) return Array.Empty<TruckTelemetry>();

            var samples = new List<TruckTelemetry>();
            foreach (var id in ids)
            {
                var raw = await _db.StringGetAsync(SampleKey(truckId, (string)id!));
                if (raw.IsNullOrEmpty) continue;
                var sample = JsonSerializer.Deserialize<TruckTelemetry>((string)raw!, _json);
                if (sample is null) continue;
                if (sample.EventTimestamp >= from && sample.EventTimestamp < to)
                    samples.Add(sample);
            }

            return samples
                .OrderByDescending(s => s.EventTimestamp)
                .Take(limit)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetHistoryAsync({TruckId}) failed", truckId);
            return Array.Empty<TruckTelemetry>();
        }
    }

    private static string IndexKey(string truckId) => $"truck:telemetry:idx:{truckId}";

    private static string SampleKey(string truckId, string sampleId) => $"truck:telemetry:{truckId}:{sampleId}";
}
