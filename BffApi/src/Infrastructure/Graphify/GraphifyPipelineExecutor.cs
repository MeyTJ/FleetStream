using System.Collections.Concurrent;
using System.Diagnostics;
using Graphify.Export;
using Graphify.Graph;
using Graphify.Models;
using Graphify.Pipeline;
using Graphify.Security;
using Microsoft.Extensions.Logging;

namespace FleetStream.Infrastructure.Graphify;

/// <summary>
/// Runs an isolated Graphify pipeline for a single project directory.
/// Each invocation creates fresh pipeline stage instances — no shared graph state.
/// </summary>
public sealed class GraphifyPipelineExecutor
{
    private readonly ILogger<GraphifyPipelineExecutor> _logger;

    public GraphifyPipelineExecutor(ILogger<GraphifyPipelineExecutor> logger)
    {
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
        var generatedFiles = new List<string>();

        var outputValidation = new InputValidator().ValidatePath(request.OutputDirectory);
        if (!outputValidation.IsValid)
        {
            throw new ArgumentException(
                $"Invalid output directory: {string.Join("; ", outputValidation.Errors)}");
        }

        Directory.CreateDirectory(request.OutputDirectory);

        var fileDetector = new FileDetector();
        var detectedFiles = await fileDetector.ExecuteAsync(
            new FileDetectorOptions(
                RootPath: request.SourcePath,
                MaxFileSizeBytes: 1024 * 1024,
                RespectGitIgnore: true),
            cancellationToken).ConfigureAwait(false);

        if (detectedFiles.Count == 0)
        {
            _logger.LogWarning("No analyzable files found under {Path}", request.SourcePath);
            return new GraphifyPipelineResult(0, 0, 0, generatedFiles, stopwatch.Elapsed);
        }

        var extractor = new Extractor();
        var extractionBag = new ConcurrentBag<ExtractionResult>();

        await Parallel.ForEachAsync(
            detectedFiles,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken
            },
            async (file, ct) =>
            {
                try
                {
                    var result = await extractor.ExecuteAsync(file, ct).ConfigureAwait(false);
                    if (result.Nodes.Count > 0 || result.Edges.Count > 0)
                        extractionBag.Add(result);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Skipped file during extraction: {Path}", file.RelativePath);
                }
            }).ConfigureAwait(false);

        var extractionResults = extractionBag.ToList();
        if (extractionResults.Count == 0)
        {
            _logger.LogWarning("Extraction produced no nodes or edges for {Path}", request.SourcePath);
            return new GraphifyPipelineResult(0, 0, 0, generatedFiles, stopwatch.Elapsed);
        }

        var graphBuilder = new GraphBuilder(new GraphBuilderOptions
        {
            CreateFileNodes = true,
            MinEdgeWeight = 0.1,
            MergeStrategy = MergeStrategy.MostRecent
        });

        var graph = await graphBuilder.ExecuteAsync(extractionResults, cancellationToken)
            .ConfigureAwait(false);

        var clusterEngine = new ClusterEngine(new ClusterOptions
        {
            MaxIterations = 100,
            Resolution = 1.0,
            MinSplitSize = 5,
            MaxCommunityFraction = 0.2
        });

        graph = await clusterEngine.ExecuteAsync(graph, cancellationToken).ConfigureAwait(false);

        var analyzer = new Analyzer(new AnalyzerOptions
        {
            TopGodNodesCount = 10,
            TopSurprisingConnections = 5,
            MaxSuggestedQuestions = 10
        });

        var analysis = await analyzer.ExecuteAsync(graph, cancellationToken).ConfigureAwait(false);
        var communityLabels = BuildCommunityLabels(graph);
        var cohesionScores = CalculateCohesionScores(graph);

        foreach (var format in request.ExportFormats)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var path = await ExportFormatAsync(
                    format,
                    graph,
                    analysis,
                    communityLabels,
                    cohesionScores,
                    request,
                    cancellationToken).ConfigureAwait(false);

                if (path is not null)
                    generatedFiles.Add(path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to export format {Format} for {Path}",
                    format, request.SourcePath);
            }
        }

        var communities = graph.GetNodes()
            .Where(n => n.Community.HasValue)
            .Select(n => n.Community!.Value)
            .Distinct()
            .Count();

        return new GraphifyPipelineResult(
            graph.NodeCount,
            graph.EdgeCount,
            communities,
            generatedFiles,
            stopwatch.Elapsed);
    }

    private static async Task<string?> ExportFormatAsync(
        string format,
        KnowledgeGraph graph,
        AnalysisResult analysis,
        IReadOnlyDictionary<int, string> communityLabels,
        IReadOnlyDictionary<int, double> cohesionScores,
        GraphifyPipelineRequest request,
        CancellationToken cancellationToken)
    {
        switch (format.Trim().ToLowerInvariant())
        {
            case "json":
                var jsonPath = Path.Combine(request.OutputDirectory, "graph.json");
                await new JsonExporter().ExportAsync(graph, jsonPath, cancellationToken)
                    .ConfigureAwait(false);
                return jsonPath;

            case "html":
                var htmlPath = Path.Combine(request.OutputDirectory, "graph.html");
                await new HtmlExporter().ExportAsync(graph, htmlPath, communityLabels, cancellationToken)
                    .ConfigureAwait(false);
                return htmlPath;

            case "svg":
                var svgPath = Path.Combine(request.OutputDirectory, "graph.svg");
                await new SvgExporter().ExportAsync(graph, svgPath, cancellationToken)
                    .ConfigureAwait(false);
                return svgPath;

            case "neo4j":
                var cypherPath = Path.Combine(request.OutputDirectory, "graph.cypher");
                await new Neo4jExporter().ExportAsync(graph, cypherPath, cancellationToken)
                    .ConfigureAwait(false);
                return cypherPath;

            case "ladybug":
                var ladybugPath = Path.Combine(request.OutputDirectory, "graph.ladybug.cypher");
                await new LadybugExporter().ExportAsync(graph, ladybugPath, cancellationToken)
                    .ConfigureAwait(false);
                return ladybugPath;

            case "obsidian":
                var obsidianPath = Path.Combine(request.OutputDirectory, "obsidian");
                await new ObsidianExporter().ExportAsync(graph, obsidianPath, cancellationToken)
                    .ConfigureAwait(false);
                return obsidianPath;

            case "wiki":
                var wikiPath = Path.Combine(request.OutputDirectory, "wiki");
                await new WikiExporter().ExportAsync(graph, wikiPath, cancellationToken)
                    .ConfigureAwait(false);
                return wikiPath;

            case "report":
                var projectName = request.ProjectName
                    ?? Path.GetFileName(Path.GetFullPath(request.SourcePath));
                var reportMarkdown = new ReportGenerator().Generate(
                    graph, analysis, communityLabels, cohesionScores, projectName);
                var reportPath = Path.Combine(request.OutputDirectory, "GRAPH_REPORT.md");
                await File.WriteAllTextAsync(reportPath, reportMarkdown, cancellationToken)
                    .ConfigureAwait(false);
                return reportPath;

            default:
                return null;
        }
    }

    private static Dictionary<int, string> BuildCommunityLabels(KnowledgeGraph graph)
    {
        var result = new Dictionary<int, string>();
        var communities = graph.GetNodes()
            .Where(n => n.Community.HasValue)
            .GroupBy(n => n.Community!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (commId, nodes) in communities)
        {
            var commonType = nodes
                .GroupBy(n => n.Type)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key ?? "Mixed";

            result[commId] = $"{commonType} (Community {commId})";
        }

        return result;
    }

    private static Dictionary<int, double> CalculateCohesionScores(KnowledgeGraph graph)
    {
        var communities = graph.GetNodes()
            .Where(n => n.Community.HasValue)
            .GroupBy(n => n.Community!.Value)
            .ToDictionary(g => g.Key, g => g.Select(n => n.Id).ToList());

        var result = new Dictionary<int, double>();
        foreach (var (commId, nodeIds) in communities)
            result[commId] = CalculateCohesion(graph, nodeIds);

        return result;
    }

    private static double CalculateCohesion(KnowledgeGraph graph, List<string> nodeIds)
    {
        if (nodeIds.Count < 2) return 0.0;

        var nodeSet = nodeIds.ToHashSet();
        var internalEdges = nodeIds.Sum(nodeId =>
            graph.GetEdges(nodeId).Count(e =>
                nodeSet.Contains(e.Source.Id) && nodeSet.Contains(e.Target.Id)));

        var possibleEdges = nodeIds.Count * (nodeIds.Count - 1);
        return possibleEdges > 0 ? (double)internalEdges / possibleEdges : 0.0;
    }
}

public sealed record GraphifyPipelineRequest(
    string SourcePath,
    string OutputDirectory,
    string? ProjectName,
    IReadOnlyList<string> ExportFormats);

public sealed record GraphifyPipelineResult(
    int Nodes,
    int Edges,
    int Communities,
    IReadOnlyList<string> GeneratedFiles,
    TimeSpan Duration);
