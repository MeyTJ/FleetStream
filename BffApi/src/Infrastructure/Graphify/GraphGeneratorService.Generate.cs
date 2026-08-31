using FleetStream.Application.Abstractions;
using FleetStream.Application.Shared.Results;
using Microsoft.Extensions.Logging;

namespace FleetStream.Infrastructure.Graphify;

public sealed partial class GraphGeneratorService
{
    public async Task<Result<GraphGenerationResult>> GenerateProjectGraphAsync(
        GenerateProjectGraphRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectName);

        if (!Directory.Exists(request.ProjectPath))
        {
            return Result<GraphGenerationResult>.Failure(
                "DIRECTORY_NOT_FOUND", $"Project directory not found: {request.ProjectPath}");
        }

        _logger.LogInformation(
            "Generating isolated graph for project: {Name} ({Path})",
            request.ProjectName, request.ProjectPath);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(_options.PerProjectTimeoutMinutes));

        try
        {
            var outputRoot = Path.GetFullPath(
                string.IsNullOrWhiteSpace(request.OutputDirectory)
                    ? _options.OutputDirectory
                    : request.OutputDirectory);

            Directory.CreateDirectory(outputRoot);

            var projectOutputDir = Path.Combine(outputRoot, SanitizeFileName(request.ProjectName));
            if (Directory.Exists(projectOutputDir))
                Directory.Delete(projectOutputDir, recursive: true);
            Directory.CreateDirectory(projectOutputDir);

            if (request.EnableSemanticExtraction)
            {
                _logger.LogWarning(
                    "Semantic extraction requested for {Name} but no AI provider is configured; using AST-only mode.",
                    request.ProjectName);
            }

            var formats = NormalizeExportFormats(request.ExportFormats);
            var pipelineRequest = new GraphifyPipelineRequest(
                request.ProjectPath,
                projectOutputDir,
                request.ProjectName,
                formats);

            var pipelineResult = IsGoProject(request.ProjectPath)
                ? await _goPipelineExecutor.ExecuteAsync(pipelineRequest, timeoutCts.Token)
                    .ConfigureAwait(false)
                : await _pipelineExecutor.ExecuteAsync(pipelineRequest, timeoutCts.Token)
                    .ConfigureAwait(false);

            if (pipelineResult.Nodes == 0 && pipelineResult.Edges == 0)
            {
                _logger.LogWarning(
                    "Graphify produced an empty graph for {Name}; output directory is still created.",
                    request.ProjectName);
            }

            var generatedFiles = pipelineResult.GeneratedFiles
                .Select(Path.GetFileName)
                .Where(f => f is not null)
                .Cast<string>()
                .ToList();

            return Result<GraphGenerationResult>.Success(new GraphGenerationResult
            {
                ProjectName         = request.ProjectName,
                ProjectPath         = request.ProjectPath,
                OutputPath          = projectOutputDir,
                Success             = true,
                NodesGenerated      = pipelineResult.Nodes,
                EdgesGenerated      = pipelineResult.Edges,
                CommunitiesDetected = pipelineResult.Communities,
                Duration            = pipelineResult.Duration,
                GeneratedFiles      = generatedFiles
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result<GraphGenerationResult>.Failure(
                "GENERATION_TIMEOUT",
                $"Graph generation timed out after {_options.PerProjectTimeoutMinutes} minute(s) for {request.ProjectName}.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating graph for {Name}", request.ProjectName);
            return Result<GraphGenerationResult>.Failure("GENERATION_ERROR", ex.Message);
        }
    }

    public Task<IReadOnlyList<ProjectModule>> DiscoverProjectsAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        return _discoveryService.DiscoverAsync(repositoryRoot, cancellationToken);
    }

    private static bool IsGoProject(string projectPath) =>
        File.Exists(Path.Combine(projectPath, "go.mod"));
}
