using FleetStream.Application.Abstractions;
using FleetStream.Application.Shared.Pagination;
using FleetStream.Application.Features.FleetTrucks.List;
using FleetStream.Core.Domain.Entities;
using FluentAssertions;
using NSubstitute;

namespace FleetStream.UnitTests.Application.Features;

public class GetTruckStatesQueryHandlerTests
{
    private readonly ITruckStateStore _states = Substitute.For<ITruckStateStore>();

    [Fact]
    public async Task Returns_states_sorted_and_paged_with_cursor()
    {
        _states.GetAllStatesAsync(Arg.Any<CancellationToken>())
               .Returns(new[]
               {
                   new TruckState { TruckId = "TAC-00003" },
                   new TruckState { TruckId = "TAC-00001" },
                   new TruckState { TruckId = "TAC-00002" },
                   new TruckState { TruckId = "TAC-00005" },
                   new TruckState { TruckId = "TAC-00004" },
               });

        var sut = new GetTruckStatesQueryHandler(_states);
        var page = await sut.Handle(new GetTruckStatesQuery(PageSize: 2), CancellationToken.None);

        page.Items.Select(s => s.TruckId).Should().Equal("TAC-00001", "TAC-00002");
        page.HasMore.Should().BeTrue();
        page.NextCursor.Should().NotBeNullOrWhiteSpace();

        var next = await sut.Handle(new GetTruckStatesQuery(page.NextCursor, PageSize: 2), CancellationToken.None);
        next.Items.Select(s => s.TruckId).Should().Equal("TAC-00003", "TAC-00004");
    }

    [Fact]
    public async Task Cursor_past_end_returns_empty_page()
    {
        _states.GetAllStatesAsync(Arg.Any<CancellationToken>())
               .Returns(new[] { new TruckState { TruckId = "TAC-00001" } });

        var cursor = CursorEncoder.Encode(new CursorEncoder.TruckCursor("TAC-99999"));
        var sut = new GetTruckStatesQueryHandler(_states);
        var page = await sut.Handle(new GetTruckStatesQuery(cursor, PageSize: 10), CancellationToken.None);

        page.Items.Should().BeEmpty();
        page.HasMore.Should().BeFalse();
    }
}
