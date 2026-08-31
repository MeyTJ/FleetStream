using Microsoft.IdentityModel.Tokens;

namespace FleetStream.Presentation.Auth;

internal sealed class JwksSigningKeyProvider
{
    private readonly string _jwksUri;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private JsonWebKeySet? _jwks;
    private DateTime _refreshedAt = DateTime.MinValue;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(15);

    public JwksSigningKeyProvider(string jwksUri, HttpClient http)
    {
        _jwksUri = jwksUri;
        _http    = http;
    }

    public IEnumerable<SecurityKey> Resolve(string? kid)
    {
        EnsureLoadedAsync(CancellationToken.None).GetAwaiter().GetResult();
        var keys = _jwks!.GetSigningKeys();
        if (string.IsNullOrEmpty(kid))
            return keys;

        var match = keys.Where(k => k.KeyId == kid).ToList();
        return match.Count > 0 ? match : keys;
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_jwks is not null && DateTime.UtcNow - _refreshedAt < RefreshInterval)
            return;

        await _lock.WaitAsync(ct);
        try
        {
            if (_jwks is not null && DateTime.UtcNow - _refreshedAt < RefreshInterval)
                return;

            var json = await _http.GetStringAsync(_jwksUri, ct);
            _jwks        = new JsonWebKeySet(json);
            _refreshedAt = DateTime.UtcNow;
        }
        finally
        {
            _lock.Release();
        }
    }
}
