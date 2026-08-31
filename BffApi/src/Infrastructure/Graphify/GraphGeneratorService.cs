using FleetStream.Application.Abstractions;
using FleetStream.Application.Shared.Results;
using FleetStream.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetStream.Infrastructure.Graphify;

public sealed partial class GraphGeneratorService : IGraphGeneratorService
{
    private readonly ILogger<GraphGeneratorService> _logger;
    private readonly ProjectDiscoveryService _discoveryService;
    private readonly GraphifyPipelineExecutor _pipelineExecutor;
    private readonly GoGraphPipelineExecutor _goPipelineExecutor;
    private readonly GraphifyOptions _options;
    private readonly TimeProvider _timeProvider;

    public GraphGeneratorService(
        ILogger<GraphGeneratorService> logger,
        ProjectDiscoveryService discoveryService,
        GraphifyPipelineExecutor pipelineExecutor,
        GoGraphPipelineExecutor goPipelineExecutor,
        IOptions<GraphifyOptions> options,
        TimeProvider timeProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _discoveryService = discoveryService ?? throw new ArgumentNullException(nameof(discoveryService));
        _pipelineExecutor = pipelineExecutor ?? throw new ArgumentNullException(nameof(pipelineExecutor));
        _goPipelineExecutor = goPipelineExecutor ?? throw new ArgumentNullException(nameof(goPipelineExecutor));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<Result<BatchGraphGenerationResult>> GenerateAllProjectGraphsAsync(
        GenerateAllGraphsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startTime = _timeProvider.GetUtcNow();
        var results = new List<GraphGenerationResult>();
        var errors = new List<Error>();

        _logger.LogInformation(
            "Starting batch graph generation for repository: {Root} (isolated per-project graphs)",
            request.RepositoryRoot);

        if (!Directory.Exists(request.RepositoryRoot))
        {
            return Result<BatchGraphGenerationResult>.Failure(
                "REPOSITORY_NOT_FOUND",
                $"Repository root not found: {request.RepositoryRoot}");
        }

        var projects = await _discoveryService
            .DiscoverAsync(request.RepositoryRoot, cancellationToken)
            .ConfigureAwait(false);

        if (projects.Count == 0)
        {
            _logger.LogWarning("No projects discovered in {Root}", request.RepositoryRoot);
            return Result<BatchGraphGenerationResult>.Failure(
                "NO_PROJECTS",
                $"No analyzable projects found in {request.RepositoryRoot}");
        }

        var filteredProjects = projects.AsEnumerable();
        if (request.ProjectFilter is { Count: > 0 })
        {
            filteredProjects = projects
                .Where(p => request.ProjectFilter.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
                .ToList();

            _logger.LogInformation("Filtered to {Count} project(s)", filteredProjects.Count());

            if (!filteredProjects.Any())
            {
                return Result<BatchGraphGenerationResult>.Failure(
                    "FILTER_NO_MATCH",
                    $"Project filter {string.Join(", ", request.ProjectFilter)} matched no projects.");
            }
        }

        var filteredList = filteredProjects.ToList();
        var outputDirectory = string.IsNullOrWhiteSpace(request.OutputDirectory)
            ? _options.OutputDirectory
            : request.OutputDirectory;

        foreach (var project in filteredList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var projectRequest = new GenerateProjectGraphRequest
            {
                ProjectPath              = project.Path,
                ProjectName              = project.Name,
                OutputDirectory          = outputDirectory,
                ExportFormats            = request.ExportFormats,
                EnableSemanticExtraction = request.EnableSemanticExtraction
            };

            var result = await GenerateProjectGraphAsync(projectRequest, cancellationToken)
                .ConfigureAwait(false);

            if (result.IsSuccess && result.Value is not null)
            {
                results.Add(result.Value);
            }
            else if (result.Error is not null)
            {
                errors.Add(result.Error);
                _logger.LogWarning(
                    "Failed to generate graph for {Name}: {Code} {Message}",
                    project.Name, result.Error.Code, result.Error.Message);

                if (request.FailFast) break;
            }
        }

        return Result<BatchGraphGenerationResult>.Success(new BatchGraphGenerationResult
        {
            Success            = errors.Count == 0,
            Results            = results,
            TotalProjects      = filteredList.Count,
            SuccessfulProjects = results.Count,
            FailedProjects     = filteredList.Count - results.Count,
            TotalDuration      = _timeProvider.GetUtcNow() - startTime,
            Errors             = errors
        });
    }
}
