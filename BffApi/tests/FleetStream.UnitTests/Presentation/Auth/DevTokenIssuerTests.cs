using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FleetStream.Infrastructure.Options;
using FleetStream.Presentation.Auth;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace FleetStream.UnitTests.Presentation.Auth;

public class DevTokenIssuerTests
{
    private static IOptions<JwtOptions> Opts(string signingKey = "this-is-a-signing-key-at-least-32-chars-long!!")
        => Microsoft.Extensions.Options.Options.Create(new JwtOptions
        {
            SigningKey = signingKey,
            Issuer     = "https://auth.test",
            Audience   = "test-aud",
        });

    [Fact]
    public void Issue_writes_a_well_formed_jwt()
    {
        var issuer = new DevTokenIssuer(Opts(), TimeProvider.System);

        var (token, expires) = issuer.Issue("alice", Array.Empty<string>());

        token.Should().NotBeNullOrEmpty();
        token.Split('.').Should().HaveCount(3); // header.payload.signature
        expires.Should().BeAfter(DateTime.UtcNow);

        var parsed = new JwtSecurityTokenHandler().ReadJwtToken(token);
        parsed.Issuer.Should().Be("https://auth.test");
        parsed.Audiences.Should().Contain("test-aud");
        parsed.Claims.Should().Contain(c => c.Type == "sub" && c.Value == "alice");
    }

    [Fact]
    public void Issue_adds_role_claims()
    {
        var issuer = new DevTokenIssuer(Opts(), TimeProvider.System);

        var (token, _) = issuer.Issue("bob", new[] { "fleet:admin", "fleet:reader" });

        var parsed = new JwtSecurityTokenHandler().ReadJwtToken(token);
        // ClaimTypes.Role short-circuits to the long URI on round-trip; either is acceptable.
        var roles = parsed.Claims
            .Where(c => c.Type == ClaimTypes.Role || c.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Value)
            .ToList();

        roles.Should().BeEquivalentTo(new[] { "fleet:admin", "fleet:reader" });
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("too-short")]
    public void Issue_throws_when_signing_key_is_missing_or_too_short(string? key)
    {
        var issuer = new DevTokenIssuer(Opts(key!), TimeProvider.System);

        var act = () => issuer.Issue("alice", Array.Empty<string>());

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*SigningKey*");
    }
}