using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Storage.Neo4j;

/// <summary>
/// Neo4j storage provider specialized for graph operations.
/// </summary>
/// <remarks>
/// Neo4j is a specialized graph database that provides:
/// - Native graph storage and traversal
/// - Entity and relationship management
/// - Community detection support
/// - GraphRAG capabilities
/// 
/// As a specialized provider, Neo4j takes priority over general-purpose
/// providers (SQLite, PostgreSQL) for graph operations.
/// </remarks>
public class Neo4jProvider : IStorageProvider, IGraphCapable
{
    private readonly IGraphStore _graphStore;
    private readonly ILogger<Neo4jProvider>? _logger;

    /// <summary>
    /// Creates a new instance of <see cref="Neo4jProvider"/>.
    /// </summary>
    /// <param name="graphStore">The Neo4j graph store implementation.</param>
    /// <param name="logger">Optional logger.</param>
    public Neo4jProvider(
        Neo4jGraphStore graphStore,
        ILogger<Neo4jProvider>? logger = null)
    {
        _graphStore = graphStore ?? throw new ArgumentNullException(nameof(graphStore));
        _logger = logger;

        _logger?.LogDebug("Neo4jProvider initialized as specialized graph provider");
    }

    /// <inheritdoc />
    public string ProviderName => "Neo4j";

    /// <inheritdoc />
    public StorageCapabilities Capabilities => StorageCapabilities.Graph;

    /// <inheritdoc />
    /// <remarks>
    /// Neo4j is a specialized graph database.
    /// It takes priority over general-purpose providers for graph operations.
    /// </remarks>
    public bool IsSpecialized => true;

    /// <inheritdoc />
    public IGraphStore GraphStore => _graphStore;
}

/// <summary>
/// Factory for creating Neo4j provider from service provider.
/// </summary>
public static class Neo4jProviderFactory
{
    /// <summary>
    /// Creates a Neo4j provider from the service provider.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <returns>A configured Neo4j provider.</returns>
    public static Neo4jProvider Create(IServiceProvider serviceProvider)
    {
        var graphStore = serviceProvider.GetService(typeof(Neo4jGraphStore)) as Neo4jGraphStore
            ?? throw new InvalidOperationException("Neo4jGraphStore is not registered.");

        var logger = serviceProvider.GetService(typeof(ILogger<Neo4jProvider>)) as ILogger<Neo4jProvider>;

        return new Neo4jProvider(graphStore, logger);
    }
}
