using FleetStream.Application.Shared.Results;

namespace FleetStream.Application.Abstractions;

/// <summary>
/// Abstraction for code graph generation using Graphify.
/// This interface is intentionally decoupled from Graphify internals
/// to maintain clean architecture boundaries.
/// </summary>
public interface IGraphGeneratorService
{
    /// <summary>
    /// Generates an isolated knowledge graph for a specific project/module.
    /// </summary>
    Task<Result<GraphGenerationResult>> GenerateProjectGraphAsync(
        GenerateProjectGraphRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates isolated knowledge graphs for all discovered projects in the repository.
    /// </summary>
    Task<Result<BatchGraphGenerationResult>> GenerateAllProjectGraphsAsync(
        GenerateAllGraphsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers all analyzable projects/modules in the repository.
    /// </summary>
    Task<IReadOnlyList<ProjectModule>> DiscoverProjectsAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default);
}