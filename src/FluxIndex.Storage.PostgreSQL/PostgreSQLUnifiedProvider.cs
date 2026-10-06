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
public partial class PostgreSQLUnifiedProvider : IStorageProvider, IVectorCapable, ISemanticCacheCapable
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

        if (_logger is not null)
            LogProviderInitialized(_logger, Capabilities);
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

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "PostgreSQLUnifiedProvider initialized with capabilities: {Capabilities}")]
    private static partial void LogProviderInitialized(ILogger logger, StorageCapabilities capabilities);

    #endregion
}
