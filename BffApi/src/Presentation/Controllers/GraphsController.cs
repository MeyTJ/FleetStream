using Asp.Versioning;
using FleetStream.Application.Abstractions;
using FleetStream.Infrastructure.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FleetStream.Presentation.Controllers;

/// <summary>
/// Triggers isolated per-project knowledge graph generation via Graphify.
/// Graph logic stays behind <see cref="IGraphGeneratorService"/>.
/// </summary>
[ApiController]
[Authorize(Policy = "FleetAdmin")]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/graphs")]
[Produces("application/json")]
public sealed class GraphsController : ControllerBase
{
    private readonly IGraphGeneratorService _graphService;
    private readonly GraphifyOptions _options;
    private readonly ILogger<GraphsController> _logger;

    public GraphsController(
        IGraphGeneratorService graphService,
        IOptions<GraphifyOptions> options,
        ILogger<GraphsController> logger)
    {
        _graphService = graphService;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Discovers analyzable projects/modules under the configured repository root.</summary>
    [HttpGet("projects")]
    [ProducesResponseType(typeof(IReadOnlyList<ProjectModule>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DiscoverProjects(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ServiceDisabledProblem());

        var projects = await _graphService
            .DiscoverProjectsAsync(_options.RepositoryRoot, cancellationToken)
            .ConfigureAwait(false);

        return Ok(projects);
    }

    /// <summary>Generates isolated graphs for every discovered project/module.</summary>
    [HttpPost("generate")]
    [ProducesResponseType(typeof(BatchGraphGenerationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GenerateAll(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ServiceDisabledProblem());

        var formats = _options.ExportFormats
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var request = new GenerateAllGraphsRequest
        {
            RepositoryRoot         = _options.RepositoryRoot,
            OutputDirectory        = _options.OutputDirectory,
            ExportFormats          = formats,
            EnableSemanticExtraction = _options.EnableSemanticExtraction,
            FailFast               = _options.FailFast,
            ProjectFilter          = _options.ProjectFilter.Count > 0 ? _options.ProjectFilter : null
        };

        var result = await _graphService
            .GenerateAllProjectGraphsAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess || result.Value is null)
        {
            return BadRequest(new ProblemDetails
            {
                Title  = "Graph generation failed",
                Detail = result.Error?.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }

        _logger.LogInformation(
            "Graph batch completed: {Success}/{Total} projects",
            result.Value.SuccessfulProjects, result.Value.TotalProjects);

        return Ok(result.Value);
    }

    /// <summary>Generates an isolated graph for a single project/module.</summary>
    [HttpPost("generate/{projectName}")]
    [ProducesResponseType(typeof(GraphGenerationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GenerateProject(
        string projectName,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ServiceDisabledProblem());

        var projects = await _graphService
            .DiscoverProjectsAsync(_options.RepositoryRoot, cancellationToken)
            .ConfigureAwait(false);

        var project = projects.FirstOrDefault(p =>
            string.Equals(p.Name, projectName, StringComparison.OrdinalIgnoreCase));

        if (project is null)
        {
            return NotFound(new ProblemDetails
            {
                Title  = "Project not found",
                Detail = $"No analyzable project named '{projectName}' was discovered.",
                Status = StatusCodes.Status404NotFound
            });
        }

        var formats = _options.ExportFormats
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var result = await _graphService.GenerateProjectGraphAsync(
            new GenerateProjectGraphRequest
            {
                ProjectPath              = project.Path,
                ProjectName              = project.Name,
                OutputDirectory          = _options.OutputDirectory,
                ExportFormats            = formats,
                EnableSemanticExtraction = _options.EnableSemanticExtraction
            },
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess || result.Value is null)
        {
            return BadRequest(new ProblemDetails
            {
                Title  = "Graph generation failed",
                Detail = result.Error?.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }

        return Ok(result.Value);
    }

    private static ProblemDetails ServiceDisabledProblem() => new()
    {
        Title  = "Graph generation disabled",
        Detail = "Set Graphify:Enabled to true in configuration to use this endpoint.",
        Status = StatusCodes.Status503ServiceUnavailable
    };
}
