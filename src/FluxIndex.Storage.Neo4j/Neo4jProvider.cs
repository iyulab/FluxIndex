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
public partial class Neo4jProvider : IStorageProvider, IGraphCapable
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

        if (_logger is not null)
            LogProviderInitialized(_logger);
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

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Neo4jProvider initialized as specialized graph provider")]
    private static partial void LogProviderInitialized(ILogger logger);

    #endregion
}
