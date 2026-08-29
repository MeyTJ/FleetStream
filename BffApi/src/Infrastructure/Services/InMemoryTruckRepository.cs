using FleetStream.Application.Abstractions;
using FleetStream.Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace FleetStream.Infrastructure.Services;

/// <summary>
/// In-memory truck repository implementation for development and testing.
/// In production (M5 per the roadmap), this is replaced with EF Core.
/// On first construction the repository seeds five demo trucks so the dashboard
/// has something to display before the Streaming Engine has produced any state.
/// </summary>
public class InMemoryTruckRepository : ITruckRepository
{
    private readonly ConcurrentDictionary<string, Truck> _trucks = new();
    private readonly ILogger<InMemoryTruckRepository> _logger;

    public InMemoryTruckRepository(ILogger<InMemoryTruckRepository> logger)
    {
        _logger = logger;
        Seed();
    }

    private void Seed()
    {
        if (!_trucks.IsEmpty) return;
        var seedIds = new[] { "TAC-00001", "TAC-00002", "TAC-00003", "TAC-00004", "TAC-00005" };
        foreach (var id in seedIds)
        {
            _trucks.TryAdd(id, new Truck
            {
                Id           = id,
                Name         = $"Truck {id}",
                LicensePlate = $"ABC-{id[^3..]}",
                Status       = "Active",
                CreatedAt    = DateTime.UtcNow,
                UpdatedAt    = DateTime.UtcNow,
            });
        }
        _logger.LogInformation("Seeded {Count} demo trucks", _trucks.Count);
    }

    public Task<Truck?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        _trucks.TryGetValue(id, out var truck);
        return Task.FromResult(truck);
    }

    public Task<IReadOnlyList<Truck>> GetAllAsync(int skip = 0, int take = 100, CancellationToken cancellationToken = default)
    {
        var trucks = _trucks.Values
            .OrderBy(t => t.Id)
            .Skip(skip)
            .Take(take)
            .ToList();
        return Task.FromResult<IReadOnlyList<Truck>>(trucks);
    }

    public Task<IReadOnlyList<Truck>> GetActiveTrucksAsync(CancellationToken cancellationToken = default)
    {
        var trucks = _trucks.Values
            .Where(t => t.Status == "Active")
            .ToList();
        return Task.FromResult<IReadOnlyList<Truck>>(trucks);
    }

    public Task<Truck> AddAsync(Truck truck, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(truck.Id))
            truck.Id = Guid.NewGuid().ToString();

        truck.CreatedAt = DateTime.UtcNow;
        truck.UpdatedAt = DateTime.UtcNow;

        _trucks.TryAdd(truck.Id, truck);
        _logger.LogInformation("Added truck {TruckId}", truck.Id);
        return Task.FromResult(truck);
    }

    public Task UpdateAsync(Truck truck, CancellationToken cancellationToken = default)
    {
        truck.UpdatedAt = DateTime.UtcNow;
        _trucks.TryUpdate(truck.Id, truck, _trucks[truck.Id]);
        _logger.LogInformation("Updated truck {TruckId}", truck.Id);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        _trucks.TryRemove(id, out _);
        _logger.LogInformation("Deleted truck {TruckId}", id);
        return Task.CompletedTask;
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_trucks.Count);
    }

    public Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_trucks.ContainsKey(id));
    }
}