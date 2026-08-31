using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FleetStream.Infrastructure.Options;

public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    private readonly IHostEnvironment _env;

    public JwtOptionsValidator(IHostEnvironment env) => _env = env;

    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        if (_env.IsDevelopment())
        {
            if (string.IsNullOrWhiteSpace(options.SigningKey) || options.SigningKey.Length < 32)
                return ValidateOptionsResult.Fail("Jwt:SigningKey must be at least 32 characters in Development.");
            return ValidateOptionsResult.Success;
        }

        if (string.IsNullOrWhiteSpace(options.JwksUri))
            return ValidateOptionsResult.Fail("Jwt:JwksUri is required in non-Development environments.");

        if (!Uri.TryCreate(options.JwksUri, UriKind.Absolute, out _))
            return ValidateOptionsResult.Fail("Jwt:JwksUri must be an absolute URI.");

        return ValidateOptionsResult.Success;
    }
}
