using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FleetStream.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FleetStream.Presentation.Auth;

/// <summary>
/// Issues short-lived HS256 JWTs for local development only. Disabled in
/// non-Development environments (the <c>POST /api/v1/auth/dev-token</c>
/// endpoint returns 404 outside Development).
/// </summary>
public sealed class DevTokenIssuer
{
    private readonly JwtOptions _opts;
    private readonly TimeProvider _clock;

    public DevTokenIssuer(IOptions<JwtOptions> opts, TimeProvider clock)
    {
        _opts = opts.Value;
        _clock = clock;
    }

    public (string Token, DateTime ExpiresAt) Issue(string subject, IEnumerable<string> roles)
    {
        if (string.IsNullOrWhiteSpace(_opts.SigningKey) || _opts.SigningKey.Length < 32)
            throw new InvalidOperationException(
                "Jwt:SigningKey must be set (>= 32 chars) to issue dev tokens. " +
                "Configure it via env var Jwt__SigningKey or `dotnet user-secrets`.");

        var now = _clock.GetUtcNow().UtcDateTime;
        var expires = now.AddHours(1);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        foreach (var role in roles ?? Array.Empty<string>())
            claims.Add(new Claim(ClaimTypes.Role, role));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _opts.Issuer,
            audience: _opts.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
