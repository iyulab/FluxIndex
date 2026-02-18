using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Storage.Qdrant;

/// <summary>
/// Qdrant storage provider specialized for vector search.
/// </summary>
/// <remarks>
/// Qdrant is a specialized vector database that provides:
/// - High-performance vector similarity search
/// - Native HNSW indexing
/// - Hybrid search capabilities (vector + keyword)
/// 
/// As a specialized provider, Qdrant takes priority over general-purpose
/// providers (SQLite, PostgreSQL) for vector operations.
/// </remarks>
public partial class QdrantProvider : IStorageProvider, IVectorCapable
{
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<QdrantProvider>? _logger;

    /// <summary>
    /// Creates a new instance of <see cref="QdrantProvider"/>.
    /// </summary>
    /// <param name="vectorStore">The Qdrant vector store implementation.</param>
    /// <param name="logger">Optional logger.</param>
    public QdrantProvider(
        QdrantVectorStore vectorStore,
        ILogger<QdrantProvider>? logger = null)
    {
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _logger = logger;

        if (_logger is not null)
            LogProviderInitialized(_logger);
    }

    /// <inheritdoc />
    public string ProviderName => "Qdrant";

    /// <inheritdoc />
    public StorageCapabilities Capabilities => StorageCapabilities.Vector;

    /// <inheritdoc />
    /// <remarks>
    /// Qdrant is a specialized vector database.
    /// It takes priority over general-purpose providers for vector operations.
    /// </remarks>
    public bool IsSpecialized => true;

    /// <inheritdoc />
    public IVectorStore VectorStore => _vectorStore;

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "QdrantProvider initialized as specialized vector provider")]
    private static partial void LogProviderInitialized(ILogger logger);

    #endregion
}

/// <summary>
/// Factory for creating Qdrant provider from service provider.
/// </summary>
public static class QdrantProviderFactory
{
    /// <summary>
    /// Creates a Qdrant provider from the service provider.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <returns>A configured Qdrant provider.</returns>
    public static QdrantProvider Create(IServiceProvider serviceProvider)
    {
        var vectorStore = serviceProvider.GetService(typeof(QdrantVectorStore)) as QdrantVectorStore
            ?? throw new InvalidOperationException("QdrantVectorStore is not registered.");

        var logger = serviceProvider.GetService(typeof(ILogger<QdrantProvider>)) as ILogger<QdrantProvider>;

        return new QdrantProvider(vectorStore, logger);
    }
}
