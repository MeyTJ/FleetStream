using System.Reflection;
using FleetStream.Core.Domain.Entities;
using FleetStream.Presentation.Hubs;
using FluentAssertions;

namespace FleetStream.UnitTests.Presentation.Hubs;

/// <summary>
/// Pins the server-to-client shape of docs/03-signalr-protocol.md §3.3.
///
/// SignalR binds hub events by *method name*, and the TypeScript client registers
/// positional handlers (frontend/src/lib/hooks/signalr-events.ts). A rename or a
/// parameter reorder therefore breaks the wire contract with no compile error on
/// either side — which is precisely the accident this test exists to turn into a
/// build failure (§3.9 acceptance criterion: "a snapshot test pins the
/// IFleetHubClient shape so accidental renames break the build").
///
/// Implemented with reflection rather than the Verify snapshot library so the gate
/// runs in the ordinary unit-test lane, without Docker or approved-file ceremony.
/// </summary>
public sealed class FleetHubClientContractTests
{
    /// <summary>
    /// The complete expected contract: method name, then parameter types in wire
    /// order. Update deliberately, alongside a §3.3 doc change and a client bump.
    /// </summary>
    private static readonly (string Method, Type[] Parameters)[] ExpectedContract =
    [
        ("OnTelemetrySample",   [typeof(TruckTelemetry)]),
        ("OnTruckStateUpdate",  [typeof(TruckState)]),
        ("OnAlert",             [typeof(Alert)]),
        ("OnFleetUpdate",       [typeof(IReadOnlyList<TruckState>)]),
        ("OnAlertsPurged",      [typeof(int), typeof(DateTime)]),
        ("OnPresenceChange",    [typeof(string), typeof(bool)]),
        ("OnSystemMessage",     [typeof(string), typeof(string), typeof(string), typeof(DateTime)]),
    ];

    [Fact]
    public void IFleetHubClient_exposes_exactly_the_documented_methods()
    {
        var actual = typeof(IFleetHubClient)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal);

        var expected = ExpectedContract.Select(c => c.Method).OrderBy(n => n, StringComparer.Ordinal);

        actual.Should().Equal(expected);
    }

    [Theory]
    [MemberData(nameof(ContractCases))]
    public void Each_method_matches_its_documented_signature(string method, Type[] parameters)
    {
        var found = typeof(IFleetHubClient)
            .GetMethod(method, BindingFlags.Public | BindingFlags.Instance, binder: null,
                types: parameters, modifiers: null);

        found.Should().NotBeNull(
            because: $"§3.3 documents {method}({string.Join(", ", parameters.Select(p => p.Name))})");

        // Every hub method must be awaitable, or SignalR will not surface faults.
        found!.ReturnType.Should().Be(typeof(Task));
    }

    public static TheoryData<string, Type[]> ContractCases()
    {
        var data = new TheoryData<string, Type[]>();
        foreach (var (method, parameters) in ExpectedContract)
            data.Add(method, parameters);
        return data;
    }

    /// <summary>
    /// The purge cutoff is compared against client-side Date values, so an
    /// unspecified-kind DateTime would be serialized without a "Z" and parsed in
    /// browser-local time. The parameter must stay a DateTime (not a string) for the
    /// JSON converter to apply ISO-8601 formatting.
    /// </summary>
    [Fact]
    public void OnAlertsPurged_keeps_its_cutoff_as_a_DateTime()
    {
        var param = typeof(IFleetHubClient)
            .GetMethod("OnAlertsPurged")!
            .GetParameters();

        param.Select(p => p.ParameterType).Should().Equal(typeof(int), typeof(DateTime));
        param[0].Name.Should().Be("count");
        param[1].Name.Should().Be("beforeTimestamp");
    }
}
