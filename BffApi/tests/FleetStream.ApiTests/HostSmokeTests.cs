using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FleetStream.ApiTests;

/// <summary>
/// Boots the real composition root (Program.cs) through
/// <see cref="WebApplicationFactory{TEntryPoint}"/>.
/// <para>
/// This class exists because four independent defects let the BFF ship in a state where
/// it could never serve traffic while every pre-existing test stayed green:
/// (1) the handler scan registered the open-generic decorators as inner handlers, so
/// BuildServiceProvider threw ArgumentException; (2) each closed decorator was also
/// self-mapped by type, so DI tried to satisfy their unregistered Func&lt;TInner&gt;
/// constructors under ValidateOnBuild; (3) both Kafka BackgroundServices captured scoped
/// services into a singleton, which ValidateScopes rejects; (4) duplicated Redis
/// endpoints made every data endpoint throw 500.
/// </para>
/// <para>
/// Items 1-3 fail while the host is built, so one "the host builds" assertion covers
/// them; item 4 needs a live request. None are reachable from a unit test over an
/// isolated ServiceCollection, because they depend on the real registration set.
/// Development is used deliberately - it turns on ValidateOnBuild and ValidateScopes.
/// Redis and Kafka are absent on purpose: the host must stay healthy without them.
/// </para>
/// </summary>
public sealed class HostSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HostSmokeTests(WebApplicationFactory<Program> factory)
    {
        // The signing key must be set as an environment variable rather than through
        // ConfigureAppConfiguration: under minimal hosting the factory's deferred
        // callbacks run after Program.cs has already read the Jwt section for
        // TokenValidationParameters, so a config override would apply to IOptions
        // (used by DevTokenIssuer) but not to validation, producing 401s.
        Environment.SetEnvironmentVariable("Jwt__SigningKey", DevSigningKey);

        _factory = factory.WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Development");
        });
    }

    private const string DevSigningKey = "host-smoke-test-signing-key-0123456789-abcdefghij";

    /// <summary>
    /// Regression guard for defects 1-3. Touching Services forces the provider to be
    /// built with ValidateOnBuild + ValidateScopes, where the host previously aborted
    /// before it ever listened on a port.
    /// </summary>
    [Fact]
    public void Host_builds_its_service_provider_in_Development()
    {
        _factory.Services.Should().NotBeNull();
    }

    [Fact]
    public async Task Liveness_probe_is_healthy_without_Redis_or_Kafka()
    {
        var response = await CreateClient().GetAsync("/api/v1/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    /// <summary>
    /// Regression guard for defect 4: building the ConnectionMultiplexer threw
    /// ArgumentException("EndPoints must be unique") on first use, surfacing as a 500
    /// from every Redis-backed route even though the host reported healthy.
    /// </summary>
    [Fact]
    public async Task Fleet_endpoints_do_not_throw_when_Redis_is_unreachable()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await GetDevTokenAsync(client));

        foreach (var path in new[]
                 { "/api/v1/fleet/summary", "/api/v1/fleet/trucks?pageSize=5", "/api/v1/fleet/alerts" })
        {
            var response = await client.GetAsync(path);

            // Redis is absent, so the adapters swallow the failure and return
            // empty/zero data. A 5xx means connection setup itself threw.
            response.StatusCode.Should().Be(HttpStatusCode.OK,
                $"{path} must degrade gracefully, not surface an unhandled exception");
        }
    }

    [Fact]
    public async Task Hub_negotiate_is_reachable_and_offers_websockets()
    {
        var client = CreateClient();

        var request = new HttpRequestMessage(
            HttpMethod.Post, "/hubs/v1/fleet/negotiate?negotiateVersion=1")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", await GetDevTokenAsync(client)) },
        };

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("WebSockets");
    }

    private HttpClient CreateClient() =>
        // HTTPS so UseHttpsRedirection does not turn every request into a 307, which
        // would drop the Authorization header and mask auth regressions as 401s.
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost/"),
            AllowAutoRedirect = false,
        });

    private static async Task<string> GetDevTokenAsync(HttpClient client)
    {
        var payload = new { subject = "host-smoke", roles = new[] { "fleet:reader", "alerts:ack" } };
        var response = await client.PostAsJsonAsync("/api/v1/auth/dev-token", payload);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<DevTokenPayload>();
        json!.AccessToken.Should().NotBeNullOrWhiteSpace();
        return json.AccessToken;
    }

    private sealed record DevTokenPayload(string AccessToken, DateTimeOffset ExpiresAt);
}
