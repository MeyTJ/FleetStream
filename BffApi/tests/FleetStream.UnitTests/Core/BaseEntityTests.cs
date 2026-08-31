using FleetStream.Core.Common;
using FluentAssertions;

namespace FleetStream.UnitTests.Core;

/// <summary>
/// Anonymous concrete entity used to exercise the abstract base class.
/// </summary>
public sealed class Widget : BaseEntity
{
    public string Name { get; set; } = string.Empty;
}

public sealed class SoftWidget : SoftDeletableEntity
{
    public string Name { get; set; } = string.Empty;
}

public class BaseEntityTests
{
    [Fact]
    public void New_entity_has_a_non_empty_id()
    {
        var w = new Widget();

        w.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Two_new_entities_have_distinct_ids()
    {
        var a = new Widget();
        var b = new Widget();

        a.Id.Should().NotBe(b.Id);
    }

    [Fact]
    public void Soft_deletable_defaults_to_not_deleted()
    {
        var s = new SoftWidget();

        s.IsDeleted.Should().BeFalse();
        s.DeletedAt.Should().BeNull();
    }
}