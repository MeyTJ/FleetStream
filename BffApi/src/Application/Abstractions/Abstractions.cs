using FleetStream.Core.Domain.Entities;

namespace FleetStream.Application.Abstractions;

// =============================================================================
//  Storage / cache abstractions
//
//  These are the ports the Application layer needs. They are implemented in
//  Infrastructure (Redis-backed in production, in-memory for tests). The
//  Application layer must NOT know about Redis, Kafka, SignalR or any
//  infrastructure concern — only these contracts.
// =============================================================================

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class;
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
    Task RefreshAsync(string key, TimeSpan? expiration = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetKeysAsync(string pattern, CancellationToken cancellationToken = default);
}

public interface ITruckRepository
{
    Task<Truck?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Truck>> GetAllAsync(int skip = 0, int take = 100, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Truck>> GetActiveTrucksAsync(CancellationToken cancellationToken = default);
    Task<Truck> AddAsync(Truck truck, CancellationToken cancellationToken = default);
    Task UpdateAsync(Truck truck, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task<int> CountAsync(CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default);
}

public interface ITruckStateStore
{
    Task<TruckState?> GetStateAsync(string truckId, CancellationToken cancellationToken = default);
    Task SetStateAsync(TruckState state, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TruckState>> GetAllStatesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TruckState>> GetStatesByIdsAsync(IEnumerable<string> truckIds, CancellationToken cancellationToken = default);
    Task RemoveStateAsync(string truckId, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string truckId, CancellationToken cancellationToken = default);
    Task<long> GetOnlineCountAsync(CancellationToken cancellationToken = default);
}

public interface INotificationService
{
    Task BroadcastTelemetryUpdateAsync(TruckTelemetry telemetry, CancellationToken cancellationToken = default);
    Task BroadcastTruckStateAsync(TruckState state, CancellationToken cancellationToken = default);
    Task BroadcastAlertAsync(Alert alert, CancellationToken cancellationToken = default);
    Task BroadcastFleetUpdateAsync(IEnumerable<TruckState> states, CancellationToken cancellationToken = default);
    Task SendToGroupAsync(string groupName, string method, object payload, CancellationToken cancellationToken = default);
    Task SendToUserAsync(string userId, string method, object payload, CancellationToken cancellationToken = default);
}

public interface IAlertService
{
    Task<Alert?> GetAlertAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Alert>> GetActiveAlertsAsync(int skip = 0, int take = 100, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Alert>> GetAlertsForTruckAsync(string truckId, int skip = 0, int take = 100, CancellationToken cancellationToken = default);
    Task CreateAlertAsync(Alert alert, CancellationToken cancellationToken = default);
    Task AcknowledgeAlertAsync(string id, string acknowledgedBy, CancellationToken cancellationToken = default);
    Task<int> GetActiveAlertCountAsync(CancellationToken cancellationToken = default);
}

public interface ITelemetryHistoryStore
{
    Task AppendAsync(TruckTelemetry sample, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TruckTelemetry>> GetHistoryAsync(
        string truckId,
        DateTime from,
        DateTime to,
        int limit,
        CancellationToken cancellationToken = default);
}