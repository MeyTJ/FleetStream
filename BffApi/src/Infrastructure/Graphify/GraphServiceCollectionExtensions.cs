using FleetStream.Application.Abstractions;
using FleetStream.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FleetStream.Infrastructure.Graphify;

/// <summary>
/// Extension methods for registering Graphify services in the DI container.
/// All graph-generation logic is fully contained inside <see cref="GraphGeneratorService"/>;
/// callers depend only on <see cref="IGraphGeneratorService"/>.
/// </summary>
public static class GraphServiceCollectionExtensions
{
    /// <summary>
    /// Adds Graphify graph generation services to the service collection.
    /// Binds <see cref="GraphifyOptions"/> from the <c>Graphify</c> configuration section.
    /// </summary>
    public static IServiceCollection AddGraphifyServices(
        this IServiceCollection services,
        Action<GraphifyOptions>? configureOptions = null)
    {
        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.AddOptions<GraphifyOptions>()
                .BindConfiguration(GraphifyOptions.SectionName)
                .ValidateDataAnnotations()
                .ValidateOnStart();
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ProjectDiscoveryService>();
        services.AddSingleton<GraphifyPipelineExecutor>();
        services.AddSingleton<GoGraphPipelineExecutor>();
        services.AddSingleton<IGraphGeneratorService, GraphGeneratorService>();

        return services;
    }

    /// <summary>
    /// Adds Graphify graph generation services with configuration supplied explicitly.
    /// Useful for tests or CLI tools that don't want configuration binding.
    /// </summary>
    public static IServiceCollection AddGraphifyServices(
        this IServiceCollection services,
        GraphifyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ProjectDiscoveryService>();
        services.AddSingleton<GraphifyPipelineExecutor>();
        services.AddSingleton<GoGraphPipelineExecutor>();
        services.AddSingleton<IGraphGeneratorService, GraphGeneratorService>();

        return services;
    }
}
