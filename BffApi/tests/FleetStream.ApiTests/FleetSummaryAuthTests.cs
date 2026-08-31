using FluentAssertions;

namespace FleetStream.ApiTests;

public sealed class ApiTestProjectSmokeTests
{
    [Fact]
    public void Presentation_entry_point_is_reachable()
    {
        typeof(Program).Assembly.GetName().Name.Should().Be("FleetStream.Presentation");
    }
}
