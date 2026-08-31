using FleetStream.Application.Shared.Results;
using FluentAssertions;

namespace FleetStream.UnitTests.Application.Results;

public class ResultTests
{
    [Fact]
    public void Generic_Success_carries_value_and_no_error()
    {
        var r = Result<int>.Success(42);

        r.IsSuccess.Should().BeTrue();
        r.Value.Should().Be(42);
        r.Error.Should().BeNull();
    }

    [Fact]
    public void Generic_Failure_carries_error_and_default_value()
    {
        var r = Result<int>.Failure("boom", "kaboom");

        r.IsSuccess.Should().BeFalse();
        // Value-type T means Result<T>.Failure cannot carry a real null —
        // the contract is "default(T)" plus IsSuccess=false plus Error!=null.
        r.Value.Should().Be(0);
        r.Error.Should().NotBeNull();
        r.Error!.Code.Should().Be("boom");
        r.Error.Message.Should().Be("kaboom");
    }

    [Fact]
    public void Generic_Failure_for_reference_type_carries_null_value()
    {
        var r = Result<string>.Failure("boom", "kaboom");

        r.IsSuccess.Should().BeFalse();
        r.Value.Should().BeNull();
        r.Error.Should().NotBeNull();
    }

    [Fact]
    public void Void_Success_is_success_and_error_free()
    {
        var r = Result.Success();

        r.IsSuccess.Should().BeTrue();
        r.Error.Should().BeNull();
    }

    [Fact]
    public void Void_Failure_carries_error()
    {
        var r = Result.Failure("x", "y");

        r.IsSuccess.Should().BeFalse();
        r.Error!.Code.Should().Be("x");
        r.Error.Message.Should().Be("y");
    }
}