using FleetStream.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace FleetStream.Infrastructure.Graphify;

public sealed class ProjectDiscoveryService
{
    private static readonly HashSet<string> DotNetProjectFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csproj", ".fsproj", ".vbproj"
    };

    private static readonly HashSet<string> GoFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "go.mod"
    };

    private static readonly HashSet<string> PythonFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "pyproject.toml", "setup.py", "requirements.txt", "Pipfile"
    };

    private static readonly HashSet<string> TypeScriptFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "package.json"
    };

    private static readonly HashSet<string> JavaFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "pom.xml", "build.gradle", "build.gradle.kts"
    };

    private static readonly HashSet<string> RustFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cargo.toml"
    };

    private static readonly HashSet<string> DefaultExclusions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "bin", "obj", "vendor", "dist", "build",
        ".vs", ".idea", ".vscode", "target", "outputs", "graphify-out"
    };

    private readonly ILogger<ProjectDiscoveryService> _logger;

    public ProjectDiscoveryService(ILogger<ProjectDiscoveryService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<IReadOnlyList<ProjectModule>> DiscoverAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        if (!Directory.Exists(repositoryRoot))
            throw new DirectoryNotFoundException($"Repository root not found: {repositoryRoot}");

        var modules = new Dictionary<string, ProjectModule>(StringComparer.OrdinalIgnoreCase);
        var rootInfo = new DirectoryInfo(repositoryRoot);

        DiscoverDotNetProjects(rootInfo, repositoryRoot, modules, cancellationToken);
        DiscoverGoProjects(rootInfo, repositoryRoot, modules, cancellationToken);
        DiscoverRootModules(rootInfo, modules, cancellationToken);

        _logger.LogInformation(
            "Discovered {Count} project module(s) in {Root}",
            modules.Count, repositoryRoot);

        return Task.FromResult<IReadOnlyList<ProjectModule>>(modules.Values.ToList());
    }

    private void DiscoverRootModules(
        DirectoryInfo root,
        Dictionary<string, ProjectModule> modules,
        CancellationToken cancellationToken)
    {
        foreach (var subDir in root.EnumerateDirectories())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ShouldExcludeDirectory(subDir.Name))
                continue;

            if (ContainsDotNetProject(subDir))
                continue;

            var module = DetectModuleFromDirectory(subDir);
            if (module is not null)
                TryAddModule(modules, module);
        }
    }

    private void DiscoverGoProjects(
        DirectoryInfo root,
        string repositoryRoot,
        Dictionary<string, ProjectModule> modules,
        CancellationToken cancellationToken)
    {
        foreach (var goMod in root.EnumerateFiles("go.mod", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsUnderExcludedDirectory(goMod.FullName, repositoryRoot))
                continue;

            var projectDir = goMod.Directory;
            if (projectDir is null)
                continue;

            var moduleName = TryReadGoModuleName(goMod.FullName);
            var name = projectDir.Name;

            var module = new ProjectModule
            {
                Name = name,
                Path = projectDir.FullName,
                Type = ProjectType.Go,
                Language = "Go",
                BuildFile = goMod.Name,
                IsAnalyzable = true
            };

            if (!string.IsNullOrWhiteSpace(moduleName))
            {
                _logger.LogDebug(
                    "Discovered Go module {Name} ({Module}) at {Path}",
                    name, moduleName, projectDir.FullName);
            }

            TryAddModule(modules, module);
        }
    }

    private void DiscoverDotNetProjects(
        DirectoryInfo root,
        string repositoryRoot,
        Dictionary<string, ProjectModule> modules,
        CancellationToken cancellationToken)
    {
        foreach (var projectFile in root.EnumerateFiles("*.*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!DotNetProjectFiles.Contains(projectFile.Extension))
                continue;

            if (IsUnderExcludedDirectory(projectFile.FullName, repositoryRoot))
                continue;

            var projectDir = projectFile.Directory;
            if (projectDir is null)
                continue;

            var name = Path.GetFileNameWithoutExtension(projectFile.Name);
            var module = new ProjectModule
            {
                Name = name,
                Path = projectDir.FullName,
                Type = ProjectType.DotNet,
                Language = "C#",
                BuildFile = projectFile.Name,
                IsAnalyzable = true
            };

            TryAddModule(modules, module);
        }
    }

    private ProjectModule? DetectModuleFromDirectory(DirectoryInfo directory)
    {
        string? dotnetFile = null;
        string? goFile = null;
        string? pyFile = null;
        string? tsFile = null;
        string? javaFile = null;
        string? rustFile = null;

        foreach (var file in directory.EnumerateFiles())
        {
            if (dotnetFile is null && file.Extension.Equals(".sln", StringComparison.OrdinalIgnoreCase))
                dotnetFile = file.Name;
            else if (goFile is null && GoFiles.Contains(file.Name))
                goFile = file.Name;
            else if (pyFile is null && PythonFiles.Contains(file.Name))
                pyFile = file.Name;
            else if (tsFile is null && TypeScriptFiles.Contains(file.Name))
                tsFile = file.Name;
            else if (javaFile is null && JavaFiles.Contains(file.Name))
                javaFile = file.Name;
            else if (rustFile is null && RustFiles.Contains(file.Name))
                rustFile = file.Name;
        }

        if (dotnetFile is not null)
        {
            return new ProjectModule
            {
                Name = directory.Name,
                Path = directory.FullName,
                Type = ProjectType.DotNet,
                Language = "C#",
                BuildFile = dotnetFile,
                IsAnalyzable = true
            };
        }

        if (goFile is not null)
        {
            return new ProjectModule
            {
                Name = directory.Name,
                Path = directory.FullName,
                Type = ProjectType.Go,
                Language = "Go",
                BuildFile = goFile,
                IsAnalyzable = true
            };
        }

        if (rustFile is not null)
        {
            return new ProjectModule
            {
                Name = directory.Name,
                Path = directory.FullName,
                Type = ProjectType.Rust,
                Language = "Rust",
                BuildFile = rustFile,
                IsAnalyzable = true
            };
        }

        if (javaFile is not null)
        {
            return new ProjectModule
            {
                Name = directory.Name,
                Path = directory.FullName,
                Type = ProjectType.Java,
                Language = "Java",
                BuildFile = javaFile,
                IsAnalyzable = true
            };
        }

        if (tsFile is not null)
        {
            return new ProjectModule
            {
                Name = directory.Name,
                Path = directory.FullName,
                Type = ProjectType.TypeScript,
                Language = "TypeScript",
                BuildFile = tsFile,
                IsAnalyzable = true
            };
        }

        if (pyFile is not null)
        {
            return new ProjectModule
            {
                Name = directory.Name,
                Path = directory.FullName,
                Type = ProjectType.Python,
                Language = "Python",
                BuildFile = pyFile,
                IsAnalyzable = true
            };
        }

        return null;
    }

    private static bool ContainsDotNetProject(DirectoryInfo directory)
    {
        return directory.EnumerateFiles("*.*", SearchOption.AllDirectories)
            .Any(f => DotNetProjectFiles.Contains(f.Extension));
    }

    private static bool ShouldExcludeDirectory(string directoryName) =>
        DefaultExclusions.Contains(directoryName);

    private static bool IsUnderExcludedDirectory(string fullPath, string repositoryRoot)
    {
        var relative = Path.GetRelativePath(repositoryRoot, fullPath);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Any(s => DefaultExclusions.Contains(s));
    }

    private static void TryAddModule(Dictionary<string, ProjectModule> modules, ProjectModule module)
    {
        if (!modules.ContainsKey(module.Path))
            modules[module.Path] = module;
    }

    private static string? TryReadGoModuleName(string goModPath)
    {
        try
        {
            foreach (var line in File.ReadLines(goModPath))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("module ", StringComparison.Ordinal))
                    continue;

                var module = trimmed["module ".Length..].Trim();
                return string.IsNullOrWhiteSpace(module) ? null : module;
            }
        }
        catch (IOException)
        {
            // Best-effort; directory name remains the display name.
        }

        return null;
    }
}
