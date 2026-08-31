using FleetStream.Application.Abstractions;
using FleetStream.Application.Features.FleetSummary.Get;
using FleetStream.Core.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace FleetStream.UnitTests.Application.Features;

public class FleetSummaryHandlerTests
{
    private static readonly DateTimeOffset FixedTime =
        new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly ITruckRepository _trucks  = Substitute.For<ITruckRepository>();
    private readonly ITruckStateStore _states  = Substitute.For<ITruckStateStore>();
    private readonly ICacheService    _cache   = Substitute.For<ICacheService>();
    private readonly TimeProvider     _clock   = new FixedTimeProvider(FixedTime);

    private GetFleetSummaryQueryHandler CreateSut() => new(
        _trucks, _states, _cache, _clock, NullLogger<GetFleetSummaryQueryHandler>.Instance);

    private static IEnumerable<TruckState> SampleStates(int count) =>
        Enumerable.Range(0, count).Select(i => new TruckState
        {
            TruckId      = $"TAC-{i:00000}",
            IsMoving     = i % 2 == 0,
            IsOnline     = true,
            SpeedKmh     = 60 + i,
            FuelLevelPercent = 50f + i,
            RiskLevel    = i == 0 ? "High" : "Low",
        });

    [Fact]
    public async Task Returns_cached_summary_without_querying_stores()
    {
        var dto = new FleetSummaryDto(5, 5, 2, 3, 0, 60, 50, FixedTime.UtcDateTime);
        _cache.GetAsync<FleetSummaryDto>("fleet:summary", Arg.Any<CancellationToken>())
              .Returns(dto);

        var sut = CreateSut();
        var result = await sut.Handle(new GetFleetSummaryQuery(), CancellationToken.None);

        result.Should().BeSameAs(dto);
        await _trucks.DidNotReceive().CountAsync(Arg.Any<CancellationToken>());
        await _states.DidNotReceive().GetAllStatesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Computes_summary_and_caches_it_when_cache_misses()
    {
        _cache.GetAsync<FleetSummaryDto>("fleet:summary", Arg.Any<CancellationToken>())
              .Returns((FleetSummaryDto?)null);
        _trucks.CountAsync(Arg.Any<CancellationToken>()).Returns(5);
        _states.GetOnlineCountAsync(Arg.Any<CancellationToken>()).Returns(4L);
        _states.GetAllStatesAsync(Arg.Any<CancellationToken>()).Returns(SampleStates(4).ToList());

        var sut = CreateSut();
        var result = await sut.Handle(new GetFleetSummaryQuery(), CancellationToken.None);

        result.TotalTrucks.Should().Be(5);
        result.OnlineTrucks.Should().Be(4);
        result.MovingTrucks.Should().Be(2);   // 4 states, half are moving (i % 2 == 0)
        result.IdleTrucks.Should().Be(2);     // online - moving
        result.AtRiskTrucks.Should().Be(1);   // i == 0 is "High"
        result.AverageSpeed.Should().BeApproximately(61.5, 0.01);
        result.GeneratedAt.Should().Be(FixedTime.UtcDateTime);

        await _cache.Received(1).SetAsync(
            "fleet:summary",
            Arg.Is<FleetSummaryDto>(d => d.TotalTrucks == 5),
            TimeSpan.FromSeconds(5),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Empty_state_set_returns_zero_averages_not_NaN()
    {
        _cache.GetAsync<FleetSummaryDto>(Arg.Any<string>(), Arg.Any<CancellationToken>())
              .Returns((FleetSummaryDto?)null);
        _trucks.CountAsync(Arg.Any<CancellationToken>()).Returns(5);
        _states.GetOnlineCountAsync(Arg.Any<CancellationToken>()).Returns(0L);
        _states.GetAllStatesAsync(Arg.Any<CancellationToken>())
              .Returns(Array.Empty<TruckState>());

        var sut = CreateSut();
        var result = await sut.Handle(new GetFleetSummaryQuery(), CancellationToken.None);

        result.AverageSpeed.Should().Be(0);
        result.AverageFuelLevel.Should().Be(0);
        result.MovingTrucks.Should().Be(0);
        result.AtRiskTrucks.Should().Be(0);
    }
}

/// <summary>Minimal <see cref="TimeProvider"/> for deterministic tests.</summary>
internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;
    public FixedTimeProvider(DateTimeOffset now) => _now = now;
    public override DateTimeOffset GetUtcNow() => _now;
}