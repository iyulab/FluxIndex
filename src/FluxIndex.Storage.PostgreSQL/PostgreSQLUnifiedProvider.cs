using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Storage.PostgreSQL;

/// <summary>
/// Unified PostgreSQL storage provider that supports all capabilities.
/// </summary>
/// <remarks>
/// PostgreSQL provides:
/// - Vector: via PostgreSQLVectorStore (pgvector extension)
/// - Graph: via PostgresGraphStore (IChunkHierarchyRepository)
/// - RDB: via FluxIndexDbContext
/// - SemanticCache: via PostgresSemanticCache
/// 
/// Note: PostgreSQL is a general-purpose provider, not specialized.
/// Specialized providers (Qdrant, Neo4j) take priority when available.
/// </remarks>
public class PostgreSQLUnifiedProvider : IStorageProvider, IVectorCapable, ISemanticCacheCapable
{
    private readonly IVectorStore _vectorStore;
    private readonly ISemanticCacheService? _semanticCache;
    private readonly ILogger<PostgreSQLUnifiedProvider>? _logger;

    /// <summary>
    /// Creates a new instance of <see cref="PostgreSQLUnifiedProvider"/>.
    /// </summary>
    /// <param name="vectorStore">The vector store implementation.</param>
    /// <param name="semanticCache">Optional semantic cache implementation.</param>
    /// <param name="logger">Optional logger.</param>
    public PostgreSQLUnifiedProvider(
        IVectorStore vectorStore,
        ISemanticCacheService? semanticCache = null,
        ILogger<PostgreSQLUnifiedProvider>? logger = null)
    {
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _semanticCache = semanticCache;
        _logger = logger;

        // Update capabilities based on available services
        var caps = StorageCapabilities.Vector | StorageCapabilities.Rdb;
        if (_semanticCache is not null)
            caps |= StorageCapabilities.SemanticCache;

        Capabilities = caps;

        _logger?.LogDebug(
            "PostgreSQLUnifiedProvider initialized with capabilities: {Capabilities}",
            Capabilities);
    }

    /// <inheritdoc />
    public string ProviderName => "PostgreSQL";

    /// <inheritdoc />
    public StorageCapabilities Capabilities { get; }

    /// <inheritdoc />
    /// <remarks>
    /// PostgreSQL is a general-purpose provider, not specialized for any single capability.
    /// When Qdrant (Vector) or Neo4j (Graph) are configured, they take priority.
    /// </remarks>
    public bool IsSpecialized => false;

    /// <inheritdoc />
    public IVectorStore VectorStore => _vectorStore;

    /// <inheritdoc />
    public ISemanticCacheService SemanticCache =>
        _semanticCache ?? throw new InvalidOperationException(
            "Semantic cache is not configured for this PostgreSQL provider.");
}

/// <summary>
/// Factory for creating PostgreSQL unified provider with all available services.
/// </summary>
public static class PostgreSQLUnifiedProviderFactory
{
    /// <summary>
    /// Creates a PostgreSQL unified provider from the service provider.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <returns>A configured PostgreSQL unified provider.</returns>
    public static PostgreSQLUnifiedProvider Create(IServiceProvider serviceProvider)
    {
        var vectorStore = serviceProvider.GetService(typeof(IVectorStore)) as IVectorStore
            ?? throw new InvalidOperationException("No IVectorStore registered for PostgreSQL.");

        var semanticCache = serviceProvider.GetService(typeof(ISemanticCacheService)) as ISemanticCacheService;
        var logger = serviceProvider.GetService(typeof(ILogger<PostgreSQLUnifiedProvider>)) as ILogger<PostgreSQLUnifiedProvider>;

        return new PostgreSQLUnifiedProvider(vectorStore, semanticCache, logger);
    }
}
