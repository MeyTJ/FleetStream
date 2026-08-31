using System.Diagnostics;
using System.Text.Json;
using FleetStream.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetStream.Infrastructure.Graphify;

/// <summary>
/// Runs graph generation for Go modules via the FleetStream Go graph-generator tool.
/// Uses go/ast extraction (CGO disabled) for richer Go-specific graphs than the .NET pipeline.
/// </summary>
public sealed class GoGraphPipelineExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly GraphifyOptions _options;
    private readonly ILogger<GoGraphPipelineExecutor> _logger;

    public GoGraphPipelineExecutor(
        IOptions<GraphifyOptions> options,
        ILogger<GoGraphPipelineExecutor> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<GraphifyPipelineResult> ExecuteAsync(
        GraphifyPipelineRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputDirectory);

        var stopwatch = Stopwatch.StartNew();
        var modulePath = ResolveGoGraphGeneratorPath(request.SourcePath);

        if (!Directory.Exists(modulePath))
        {
            throw new DirectoryNotFoundException(
                $"Go graph-generator module not found: {modulePath}. " +
                $"Set Graphify:GoGraphGeneratorPath or place tools/graph-generator in the repository.");
        }

        var projectName = request.ProjectName
            ?? Path.GetFileName(Path.GetFullPath(request.SourcePath));

        var formatArg = string.Join(',', request.ExportFormats);
        var args = string.Join(' ',
            "run", "./cmd/graph-generator", "--",
            "--project-path", Quote(request.SourcePath),
            "--project-name", Quote(projectName),
            "--output", Quote(Path.GetDirectoryName(request.OutputDirectory) ?? request.OutputDirectory),
            "--format", Quote(formatArg),
            "--json");

        _logger.LogDebug(
            "Invoking Go graph-generator for {Project} from {Module}",
            projectName, modulePath);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "go",
                Arguments = args,
                WorkingDirectory = modulePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.StartInfo.Environment["CGO_ENABLED"] = "0";

        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(
                $"Go graph generation failed for {projectName} (exit {process.ExitCode}): {detail.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(stderr))
            _logger.LogDebug("Go graph-generator stderr: {Stderr}", stderr.Trim());

        var result = TryParseJsonResult(stdout);
        if (result is not null)
        {
            return new GraphifyPipelineResult(
                result.Nodes,
                result.Edges,
                result.Communities,
                result.GeneratedFiles.Select(f => Path.Combine(request.OutputDirectory, f)).ToList(),
                stopwatch.Elapsed);
        }

        return ReadResultFromOutputDirectory(request.OutputDirectory, stopwatch.Elapsed);
    }

    private string ResolveGoGraphGeneratorPath(string projectPath)
    {
        var configured = _options.GoGraphGeneratorPath;
        if (Path.IsPathRooted(configured) && Directory.Exists(configured))
            return Path.GetFullPath(configured);

        var repoRoot = FindRepositoryRoot(projectPath);
        var candidate = Path.GetFullPath(Path.Combine(repoRoot, configured));
        if (Directory.Exists(candidate))
            return candidate;

        var fromCwd = Path.GetFullPath(configured);
        if (Directory.Exists(fromCwd))
            return fromCwd;

        return candidate;
    }

    private static string FindRepositoryRoot(string startPath)
    {
        var dir = Directory.Exists(startPath)
            ? Path.GetFullPath(startPath)
            : Path.GetFullPath(Path.GetDirectoryName(startPath) ?? startPath);

        while (true)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;

            var parent = Directory.GetParent(dir)?.FullName;
            if (parent is null || string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase))
                return dir;

            dir = parent;
        }
    }

    private static GoProjectResult? TryParseJsonResult(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
            return null;

        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith("{", StringComparison.Ordinal))
                continue;

            try
            {
                return JsonSerializer.Deserialize<GoProjectResult>(line, JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        return null;
    }

    private static GraphifyPipelineResult ReadResultFromOutputDirectory(
        string outputDirectory,
        TimeSpan elapsed)
    {
        var generatedFiles = new List<string>();
        var nodes = 0;
        var edges = 0;
        var communities = 0;

        var jsonPath = Path.Combine(outputDirectory, "graph.json");
        if (File.Exists(jsonPath))
        {
            generatedFiles.Add(jsonPath);
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
                if (doc.RootElement.TryGetProperty("nodes", out var nodesEl))
                    nodes = nodesEl.GetArrayLength();
                if (doc.RootElement.TryGetProperty("links", out var linksEl))
                    edges = linksEl.GetArrayLength();
                else if (doc.RootElement.TryGetProperty("edges", out var edgesEl))
                    edges = edgesEl.GetArrayLength();
            }
            catch (JsonException)
            {
                // Output files exist; counts remain zero.
            }
        }

        foreach (var file in new[] { "graph.html", "GRAPH_REPORT.md" })
        {
            var path = Path.Combine(outputDirectory, file);
            if (File.Exists(path))
                generatedFiles.Add(path);
        }

        return new GraphifyPipelineResult(nodes, edges, communities, generatedFiles, elapsed);
    }

    private static string Quote(string value) =>
        OperatingSystem.IsWindows() ? $"\"{value}\"" : $"'{value}'";

    private sealed record GoProjectResult
    {
        public int Nodes { get; init; }
        public int Edges { get; init; }
        public int Communities { get; init; }
        public List<string> GeneratedFiles { get; init; } = [];
        public bool Success { get; init; }
        public string? Error { get; init; }
    }
}
