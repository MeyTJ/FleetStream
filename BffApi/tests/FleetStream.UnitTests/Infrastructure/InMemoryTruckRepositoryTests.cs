using FleetStream.Core.Domain.Entities;
using FleetStream.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FleetStream.UnitTests.Infrastructure;

public class InMemoryTruckRepositoryTests
{
    private readonly InMemoryTruckRepository _sut =
        new(NullLogger<InMemoryTruckRepository>.Instance);

    [Fact]
    public async Task Seeds_five_demo_trucks_on_first_use()
    {
        var all = await _sut.GetAllAsync();

        all.Should().HaveCount(5);
        all.Select(t => t.Id).Should()
           .BeEquivalentTo(new[] { "TAC-00001", "TAC-00002", "TAC-00003", "TAC-00004", "TAC-00005" });
    }

    [Fact]
    public async Task GetByIdAsync_returns_seeded_truck()
    {
        var t = await _sut.GetByIdAsync("TAC-00001");

        t.Should().NotBeNull();
        t!.Name.Should().Be("Truck TAC-00001");
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_for_missing()
    {
        var t = await _sut.GetByIdAsync("does-not-exist");

        t.Should().BeNull();
    }

    [Fact]
    public async Task Add_assigns_id_and_increments_count()
    {
        var truck = new Truck { Id = "", Name = "New", LicensePlate = "AAA-000", Status = "Active" };

        var added = await _sut.AddAsync(truck);
        var count = await _sut.CountAsync();

        added.Id.Should().NotBeNullOrEmpty();
        (await _sut.ExistsAsync(added.Id)).Should().BeTrue();
        count.Should().Be(6);
    }

    [Fact]
    public async Task Delete_removes_truck()
    {
        await _sut.DeleteAsync("TAC-00001");

        (await _sut.ExistsAsync("TAC-00001")).Should().BeFalse();
        (await _sut.CountAsync()).Should().Be(4);
    }

    [Fact]
    public async Task GetAllAsync_paginates()
    {
        var page = await _sut.GetAllAsync(skip: 2, take: 2);

        page.Select(t => t.Id).Should().Equal("TAC-00003", "TAC-00004");
    }

    [Fact]
    public async Task GetActiveTrucksAsync_returns_all_seeded_when_all_active()
    {
        var active = await _sut.GetActiveTrucksAsync();

        active.Should().HaveCount(5);
    }
}