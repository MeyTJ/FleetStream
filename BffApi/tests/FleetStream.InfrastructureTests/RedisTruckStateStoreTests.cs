using FleetStream.Core.Domain.Entities;
using FleetStream.Infrastructure.Services;
using FluentAssertions;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace FleetStream.InfrastructureTests;

public sealed class RedisTruckStateStoreTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder().Build();
    private IConnectionMultiplexer _mux = null!;
    private RedisTruckStateStore _sut = null!;

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();
        _mux = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
        _sut = new RedisTruckStateStore(_mux, Microsoft.Extensions.Logging.Abstractions.NullLogger<RedisTruckStateStore>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _mux.DisposeAsync();
        await _redis.DisposeAsync();
    }

    [Fact]
    public async Task SetAndGet_round_trips()
    {
        var state = new TruckState
        {
            TruckId  = "TAC-00001",
            Timestamp = DateTime.UtcNow,
            Latitude = 1,
            Longitude = 2,
            IsOnline = true,
        };

        await _sut.SetStateAsync(state);
        var loaded = await _sut.GetStateAsync("TAC-00001");

        loaded.Should().NotBeNull();
        loaded!.TruckId.Should().Be("TAC-00001");
        loaded.Latitude.Should().Be(1);
    }
}
