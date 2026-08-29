using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using FleetStream.Presentation.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FleetStream.Presentation.Controllers;

/// <summary>
/// Development-only endpoints. The class is registered only when
/// <see cref="IHostEnvironment.IsDevelopment"/> is true; in any other
/// environment the route returns 404 because no controller action is mapped.
/// </summary>
[ApiController]
[AllowAnonymous]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly DevTokenIssuer _issuer;
    private readonly IHostEnvironment _env;

    public AuthController(DevTokenIssuer issuer, IHostEnvironment env)
    {
        _issuer = issuer;
        _env = env;
    }

    [HttpPost("dev-token", Name = "IssueDevToken")]
    [ProducesResponseType(typeof(DevTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<DevTokenResponse> IssueDevToken([FromBody] DevTokenRequest req)
    {
        if (!_env.IsDevelopment())
            return NotFound();

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var (token, expires) = _issuer.Issue(req.Subject, req.Roles ?? Array.Empty<string>());
        return Ok(new DevTokenResponse(token, expires));
    }
}

public sealed class DevTokenRequest
{
    [Required(AllowEmptyStrings = false, ErrorMessage = "Subject is required.")]
    [StringLength(64, MinimumLength = 1, ErrorMessage = "Subject must be 1-64 chars.")]
    public string Subject { get; set; } = "dev";

    public string[]? Roles { get; set; }
}

public sealed record DevTokenResponse(string AccessToken, DateTime ExpiresAt);