using FleetStream.Application.Shared.Results;

namespace FleetStream.Application.Abstractions;

/// <summary>
/// Request to generate a graph for a single project.
/// </summary>
public sealed record GenerateProjectGraphRequest
{
    public required string ProjectPath { get; init; }
    public required string ProjectName { get; init; }
    public string OutputDirectory { get; init; } = "outputs/graphs";
    public IReadOnlyList<string> ExportFormats { get; init; } = new[] { "json", "html", "report" };
    public bool EnableSemanticExtraction { get; init; } = false;
    public IReadOnlyList<string> IncludePatterns { get; init; } = new[] { "*.cs", "*.go", "*.ts", "*.js", "*.py", "*.yaml", "*.json" };
    public IReadOnlyList<string> ExcludePatterns { get; init; } = new[] { "**/bin/**", "**/obj/**", "**/.git/**", "**/node_modules/**", "**/vendor/**" };
}

/// <summary>
/// Request to generate graphs for all projects.
/// </summary>
public sealed record GenerateAllGraphsRequest
{
    public required string RepositoryRoot { get; init; }
    public string OutputDirectory { get; init; } = "outputs/graphs";
    public IReadOnlyList<string> ExportFormats { get; init; } = new[] { "json", "html", "report" };
    public bool EnableSemanticExtraction { get; init; } = false;
    public bool FailFast { get; init; } = false;
    public IReadOnlyList<string>? ProjectFilter { get; init; }
}

/// <summary>
/// Result of a single project graph generation.
/// </summary>
public sealed record GraphGenerationResult
{
    public required string ProjectName { get; init; }
    public required string ProjectPath { get; init; }
    public required string OutputPath { get; init; }
    public required bool Success { get; init; }
    public int NodesGenerated { get; init; }
    public int EdgesGenerated { get; init; }
    public int CommunitiesDetected { get; init; }
    public string? ErrorMessage { get; init; }
    public TimeSpan Duration { get; init; }
    public IReadOnlyList<string> GeneratedFiles { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Result of batch graph generation for all projects.
/// </summary>
public sealed record BatchGraphGenerationResult
{
    public required bool Success { get; init; }
    public required IReadOnlyList<GraphGenerationResult> Results { get; init; }
    public int TotalProjects { get; init; }
    public int SuccessfulProjects { get; init; }
    public int FailedProjects { get; init; }
    public TimeSpan TotalDuration { get; init; }
    public IReadOnlyList<Error> Errors { get; init; } = Array.Empty<Error>();
}

/// <summary>
/// Represents a discovered project/module in the repository.
/// </summary>
public sealed record ProjectModule
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required ProjectType Type { get; init; }
    public string? Language { get; init; }
    public string? BuildFile { get; init; }
    public bool IsAnalyzable { get; init; }
}

/// <summary>
/// Type of project/module discovered.
/// </summary>
public enum ProjectType
{
    Unknown,
    DotNet,
    Go,
    Python,
    TypeScript,
    Java,
    Rust,
    Mixed
}