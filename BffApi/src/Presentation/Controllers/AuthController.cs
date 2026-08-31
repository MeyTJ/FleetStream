using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using FleetStream.Infrastructure.Options;
using FleetStream.Presentation.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.FeatureManagement;

namespace FleetStream.Presentation.Controllers;

[ApiController]
[AllowAnonymous]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly DevTokenIssuer _issuer;
    private readonly IFeatureManager _features;

    public AuthController(DevTokenIssuer issuer, IFeatureManager features)
    {
        _issuer    = issuer;
        _features  = features;
    }

    [HttpPost("dev-token", Name = "IssueDevToken")]
    [ProducesResponseType(typeof(DevTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DevTokenResponse>> IssueDevToken([FromBody] DevTokenRequest req)
    {
        if (!await _features.IsEnabledAsync(nameof(FeaturesOptions.DevToken)))
            return NotFound();

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var (token, expires) = _issuer.Issue(req.Subject, req.Roles ?? Array.Empty<string>());
        return Ok(new DevTokenResponse(token, expires));
    }
}

public sealed record DevTokenRequest : IValidatableObject
{
    [Required(AllowEmptyStrings = false, ErrorMessage = "subject is required.")]
    [StringLength(64, MinimumLength = 1, ErrorMessage = "subject must be 1-64 chars.")]
    public required string Subject { get; init; }

    public string[]? Roles { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Subject is not null && string.IsNullOrWhiteSpace(Subject))
            yield return new ValidationResult(
                "subject must not be whitespace.",
                new[] { nameof(Subject) });
    }
}

public sealed record DevTokenResponse(string AccessToken, DateTime ExpiresAt);
