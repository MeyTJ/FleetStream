using FleetStream.Application.Features.FleetAlerts.Acknowledge;
using FleetStream.Application.Features.FleetTrucks.GetState;
using FleetStream.Application.Features.FleetTrucks.GetTruck;
using FleetStream.Application.Features.FleetTrucks.List;
using FluentAssertions;

namespace FleetStream.UnitTests.Application.Features;

public class GetTruckStatesQueryValidatorTests
{
    private readonly GetTruckStatesQueryValidator _sut = new();

    [Theory]
    [InlineData(null, 1)]
    [InlineData(null, 100)]
    [InlineData(null, 200)]
    public void Valid_inputs_pass(string? cursor, int pageSize)
    {
        var r = _sut.Validate(new GetTruckStatesQuery(cursor, pageSize));

        r.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(null, 201)]
    public void Invalid_inputs_fail(string? cursor, int pageSize)
    {
        var r = _sut.Validate(new GetTruckStatesQuery(cursor, pageSize));

        r.IsValid.Should().BeFalse();
        r.Errors.Should().NotBeEmpty();
    }
}

public class GetTruckStateQueryValidatorTests
{
    private readonly GetTruckStateQueryValidator _sut = new();

    [Fact]
    public void Empty_id_fails()
    {
        var r = _sut.Validate(new GetTruckStateQuery(string.Empty));

        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Whitespace_id_fails()
    {
        var r = _sut.Validate(new GetTruckStateQuery("   "));

        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Non_empty_id_passes()
    {
        var r = _sut.Validate(new GetTruckStateQuery("TAC-00001"));

        r.IsValid.Should().BeTrue();
    }
}

public class GetTruckQueryValidatorTests
{
    private readonly GetTruckQueryValidator _sut = new();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_or_whitespace_id_fails(string id)
    {
        _sut.Validate(new GetTruckQuery(id)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Non_empty_id_passes()
    {
        _sut.Validate(new GetTruckQuery("TAC-00001")).IsValid.Should().BeTrue();
    }
}

public class AcknowledgeAlertCommandValidatorTests
{
    private readonly AcknowledgeAlertCommandValidator _sut = new();

    [Fact]
    public void Empty_alertId_fails()
    {
        var r = _sut.Validate(new AcknowledgeAlertCommand(string.Empty, "alice"));

        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.PropertyName == nameof(AcknowledgeAlertCommand.AlertId));
    }

    [Fact]
    public void Empty_acknowledgedBy_fails()
    {
        var r = _sut.Validate(new AcknowledgeAlertCommand("alert-1", string.Empty));

        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.PropertyName == nameof(AcknowledgeAlertCommand.AcknowledgedBy));
    }

    [Fact]
    public void Valid_command_passes()
    {
        var r = _sut.Validate(new AcknowledgeAlertCommand("alert-1", "alice"));

        r.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Over_64_chars_fails()
    {
        var big = new string('x', 65);
        var r = _sut.Validate(new AcknowledgeAlertCommand(big, "alice"));

        r.IsValid.Should().BeFalse();
    }
}