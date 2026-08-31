using FleetStream.Core.Common;
using FluentAssertions;

// One pre-existing CS8602 false positive fires inside ValueObject<T>.Equals
// when the test calls the (object?) overload. Production code is correct.
#pragma warning disable CS8602

namespace FleetStream.UnitTests.Core;

/// <summary>
/// A test-only value object. Used to verify the base <see cref="ValueObject{T}"/>
/// equality contract.
/// </summary>
public sealed class Money : ValueObject<Money>
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        Amount   = amount;
        Currency = currency;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }
}

public class ValueObjectTests
{
    [Fact]
    public void Equals_returns_true_when_components_match()
    {
        var a = new Money(10m, "USD");
        var b = new Money(10m, "USD");

        a.Equals(b).Should().BeTrue();
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Equals_returns_false_when_any_component_differs()
    {
        var a = new Money(10m, "USD");
        var b = new Money(11m, "USD");
        var c = new Money(10m, "EUR");

        (a == b).Should().BeFalse();
        (a == c).Should().BeFalse();
        (a != c).Should().BeTrue();
    }

    [Fact]
    public void Equals_returns_false_against_null_and_other_type()
    {
        var a = new Money(10m, "USD");

        a.Equals(null).Should().BeFalse();

        object other = "not-a-money";
        a.Equals(other).Should().BeFalse();
    }

    [Fact]
    public void Two_nulls_are_equal()
    {
        Money? a = null;
        Money? b = null;

#pragma warning disable CS8073 // The result of the expression is always 'true' since a value of type 'Money' is never equal to 'null' — intentional: asserts operator null-handling.
        (a == b).Should().BeTrue();
#pragma warning restore CS8073
    }
}