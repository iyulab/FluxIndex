using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Storage.SQLite.Cache;
using FluxIndex.Storage.SQLite.Graph;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Storage.SQLite;

/// <summary>
/// Unified SQLite storage provider that supports all capabilities.
/// This is the default provider for local mode.
/// </summary>
/// <remarks>
/// SQLite provides:
/// - Vector: via SQLiteVectorStore or SQLiteVecVectorStore
/// - Graph: via SQLiteGraphStore (IChunkHierarchyRepository)
/// - RDB: via SQLiteDbContext (no separate IDocumentRepository - managed through SDK)
/// - SemanticCache: via SQLiteSemanticCache
/// 
/// Note: SQLite is a general-purpose provider, not specialized.
/// </remarks>
public class SQLiteUnifiedProvider : IStorageProvider, IVectorCapable, ISemanticCacheCapable
{
    private readonly IVectorStore _vectorStore;
    private readonly ISemanticCacheService? _semanticCache;
    private readonly ILogger<SQLiteUnifiedProvider>? _logger;

    /// <summary>
    /// Creates a new instance of <see cref="SQLiteUnifiedProvider"/>.
    /// </summary>
    /// <param name="vectorStore">The vector store implementation.</param>
    /// <param name="semanticCache">Optional semantic cache implementation.</param>
    /// <param name="logger">Optional logger.</param>
    public SQLiteUnifiedProvider(
        IVectorStore vectorStore,
        ISemanticCacheService? semanticCache = null,
        ILogger<SQLiteUnifiedProvider>? logger = null)
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
            "SQLiteUnifiedProvider initialized with capabilities: {Capabilities}",
            Capabilities);
    }

    /// <inheritdoc />
    public string ProviderName => "SQLite";

    /// <inheritdoc />
    public StorageCapabilities Capabilities { get; }

    /// <inheritdoc />
    /// <remarks>
    /// SQLite is a general-purpose provider, not specialized for any single capability.
    /// </remarks>
    public bool IsSpecialized => false;

    /// <inheritdoc />
    public IVectorStore VectorStore => _vectorStore;

    /// <inheritdoc />
    public ISemanticCacheService SemanticCache =>
        _semanticCache ?? throw new InvalidOperationException(
            "Semantic cache is not configured for this SQLite provider.");
}

/// <summary>
/// Factory for creating SQLite unified provider with all available services.
/// </summary>
public static class SQLiteUnifiedProviderFactory
{
    /// <summary>
    /// Creates a SQLite unified provider from the service provider.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <returns>A configured SQLite unified provider.</returns>
    public static SQLiteUnifiedProvider Create(IServiceProvider serviceProvider)
    {
        var vectorStore = serviceProvider.GetService(typeof(IVectorStore)) as IVectorStore
            ?? throw new InvalidOperationException("No IVectorStore registered for SQLite.");

        var semanticCache = serviceProvider.GetService(typeof(ISemanticCacheService)) as ISemanticCacheService;
        var logger = serviceProvider.GetService(typeof(ILogger<SQLiteUnifiedProvider>)) as ILogger<SQLiteUnifiedProvider>;

        return new SQLiteUnifiedProvider(vectorStore, semanticCache, logger);
    }
}
