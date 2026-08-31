using FleetStream.Application.Abstractions;
using FleetStream.Infrastructure.Graphify;
using FleetStream.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var root = args.FirstOrDefault(a => !a.StartsWith('-'))
    ?? Directory.GetCurrentDirectory();

var output = GetOption(args, "--output", "-o") ?? "outputs/graphs";
var filter = GetOption(args, "--filter", "-f");
var failFast = args.Contains("--fail-fast");
var discoverOnly = args.Contains("--discover");

if (args.Contains("--help") || args.Contains("-h"))
{
    PrintUsage();
    return 0;
}

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((_, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: true);
        config.AddJsonFile("appsettings.local.json", optional: true);
    })
    .ConfigureServices((context, services) =>
    {
        var section = context.Configuration.GetSection(GraphifyOptions.SectionName);
        var options = section.Get<GraphifyOptions>() ?? new GraphifyOptions();

        if (string.IsNullOrWhiteSpace(options.RepositoryRoot))
            options.RepositoryRoot = root;

        services.AddGraphifyServices(options);
    })
    .Build();

var graphService = host.Services.GetRequiredService<IGraphGeneratorService>();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

if (discoverOnly)
{
    var projects = await graphService.DiscoverProjectsAsync(root);
    foreach (var project in projects)
        Console.WriteLine($"{project.Name}\t{project.Type}\t{project.Path}");

    Console.WriteLine($"Total: {projects.Count}");
    return 0;
}

var formats = (GetOption(args, "--format") ?? "json,html,report")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

var request = new GenerateAllGraphsRequest
{
    RepositoryRoot           = root,
    OutputDirectory          = output,
    ExportFormats            = formats,
    EnableSemanticExtraction = args.Contains("--semantic"),
    FailFast                 = failFast,
    ProjectFilter            = filter is null
        ? null
        : filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
};

logger.LogInformation("Generating isolated graphs for repository: {Root}", root);

var result = await graphService.GenerateAllProjectGraphsAsync(request);

if (!result.IsSuccess || result.Value is null)
{
    Console.Error.WriteLine($"FAILED: {result.Error?.Code} {result.Error?.Message}");
    return 1;
}

var batch = result.Value;
foreach (var item in batch.Results)
{
    Console.WriteLine(
        $"{item.ProjectName}: nodes={item.NodesGenerated}, edges={item.EdgesGenerated}, output={item.OutputPath}");
}

foreach (var error in batch.Errors)
    Console.Error.WriteLine($"ERROR {error.Code}: {error.Message}");

Console.WriteLine(
    $"Done: {batch.SuccessfulProjects}/{batch.TotalProjects} succeeded in {batch.TotalDuration.TotalSeconds:F1}s");

return batch.Success ? 0 : 1;

static string? GetOption(string[] args, params string[] names)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (names.Any(n => string.Equals(args[i], n, StringComparison.OrdinalIgnoreCase)))
            return args[i + 1];
    }

    return null;
}

static void PrintUsage()
{
    Console.WriteLine("""
        FleetStream Graph Generator — isolated per-project Graphify graphs

        Usage:
          dotnet run --project BffApi/tools/FleetStream.GraphGenerator -- [repo-root] [options]

        Options:
          --discover              List discovered projects and exit
          --output, -o <dir>      Output root (default: outputs/graphs)
          --format <list>         Export formats (default: json,html,report)
          --filter, -f <names>    Comma-separated project name filter
          --semantic              Request semantic extraction (requires AI provider)
          --fail-fast             Stop on first project failure
          --help, -h              Show this help
        """);
}
