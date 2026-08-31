using System.ComponentModel.DataAnnotations;

namespace FleetStream.Infrastructure.Options;

/// <summary>Strongly-typed configuration for the Graphify graph generator.</summary>
public sealed class GraphifyOptions
{
    public const string SectionName = "Graphify";

    /// <summary>Root directory of the repository to analyze.</summary>
    [Required, MinLength(1)]
    public string RepositoryRoot { get; set; } = string.Empty;

    /// <summary>Output directory for all generated graph files.</summary>
    [Required, MinLength(1)]
    public string OutputDirectory { get; set; } = "outputs/graphs";

    /// <summary>Export formats to generate.</summary>
    public string ExportFormats { get; set; } = "json,html,report";

    /// <summary>Whether to enable AI-powered semantic extraction.</summary>
    public bool EnableSemanticExtraction { get; set; } = false;

    /// <summary>Whether to fail fast on first error.</summary>
    public bool FailFast { get; set; } = false;

    /// <summary>Optional filter to only generate graphs for specific projects.</summary>
    public List<string> ProjectFilter { get; set; } = new();

    /// <summary>Timeout in minutes per project graph generation.</summary>
    [Range(1, 120)]
    public int PerProjectTimeoutMinutes { get; set; } = 10;

    /// <summary>Whether the graph generator service is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Relative or absolute path to the Go graph-generator module used for Go projects.
    /// </summary>
    public string GoGraphGeneratorPath { get; set; } = "tools/graph-generator";
}