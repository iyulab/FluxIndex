using FluxIndex.SDK;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.Storage.Neo4j;

/// <summary>
/// Extension methods for FluxIndexContextBuilder to add Neo4j storage support.
/// Consumers must reference FluxIndex.Storage.Neo4j and call these methods.
/// </summary>
public static class FluxIndexContextBuilderExtensions
{
    /// <summary>
    /// Register Neo4j graph store services using the options already set on the builder
    /// (via UseNeo4j() or UseBestInClass()).
    /// </summary>
    public static FluxIndexContextBuilder AddNeo4jStorage(this FluxIndexContextBuilder builder)
    {
        builder.RegisterStorageServices(services =>
        {
            var options = builder.Options;
            var graphProvider = options.GraphStore.Provider?.ToLowerInvariant();

            if (graphProvider != "neo4j")
                return;

            if (!string.IsNullOrEmpty(options.GraphStore.Neo4jUri))
            {
                services.AddNeo4jGraphStore(
                    options.GraphStore.Neo4jUri,
                    options.GraphStore.Neo4jUsername ?? "neo4j",
                    options.GraphStore.Neo4jPassword ?? string.Empty,
                    options.GraphStore.Neo4jDatabase);
            }
        });

        return builder;
    }

    /// <summary>
    /// Register Neo4j graph store with explicit options configuration.
    /// </summary>
    public static FluxIndexContextBuilder AddNeo4jStorage(
        this FluxIndexContextBuilder builder,
        Action<Neo4jOptions> configure)
    {
        builder.RegisterStorageServices(services =>
        {
            services.AddNeo4jGraphStore(configure);
        });

        // Set provider in options for downstream awareness
        builder.Options.GraphStore.Provider = "Neo4j";

        return builder;
    }
}
