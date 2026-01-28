using FluxIndex.Core.Domain.Entities;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Storage capabilities that a provider can support.
/// </summary>
[Flags]
public enum StorageCapabilities
{
    /// <summary>No capabilities.</summary>
    None = 0,

    /// <summary>Vector search capability (embedding-based similarity search).</summary>
    Vector = 1 << 0,

    /// <summary>Graph storage capability (entity and relationship storage).</summary>
    Graph = 1 << 1,

    /// <summary>Relational data capability (document metadata storage).</summary>
    Rdb = 1 << 2,

    /// <summary>Semantic caching capability (query result caching with similarity matching).</summary>
    SemanticCache = 1 << 3
}

/// <summary>
/// Base interface for all storage providers.
/// Providers report their capabilities, and the orchestrator selects
/// the best provider for each capability.
/// </summary>
public interface IStorageProvider
{
    /// <summary>
    /// Gets the provider name for identification and logging.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Gets the capabilities this provider supports.
    /// </summary>
    StorageCapabilities Capabilities { get; }

    /// <summary>
    /// Gets whether this provider is a specialized provider for a specific capability.
    /// Specialized providers take priority over general-purpose providers.
    /// </summary>
    /// <remarks>
    /// Examples:
    /// - Qdrant is specialized for Vector (returns true)
    /// - Neo4j is specialized for Graph (returns true)
    /// - SQLite/PostgreSQL are general-purpose (returns false)
    /// </remarks>
    bool IsSpecialized { get; }
}

/// <summary>
/// Marker interface for providers that support vector search.
/// </summary>
public interface IVectorCapable : IStorageProvider
{
    /// <summary>
    /// Gets the vector store implementation.
    /// </summary>
    IVectorStore VectorStore { get; }
}

/// <summary>
/// Marker interface for providers that support graph storage.
/// </summary>
public interface IGraphCapable : IStorageProvider
{
    /// <summary>
    /// Gets the graph store implementation.
    /// </summary>
    IGraphStore GraphStore { get; }
}

/// <summary>
/// Marker interface for providers that support relational document storage.
/// </summary>
public interface IRdbCapable : IStorageProvider
{
    /// <summary>
    /// Gets the document repository implementation.
    /// </summary>
    IDocumentRepository DocumentRepository { get; }
}

/// <summary>
/// Marker interface for providers that support semantic caching.
/// </summary>
public interface ISemanticCacheCapable : IStorageProvider
{
    /// <summary>
    /// Gets the semantic cache service implementation.
    /// </summary>
    ISemanticCacheService SemanticCache { get; }
}

/// <summary>
/// Storage configuration resolved by the orchestrator.
/// </summary>
public class StorageConfiguration
{
    /// <summary>
    /// The provider used for vector operations.
    /// </summary>
    public string? VectorProvider { get; init; }

    /// <summary>
    /// The provider used for graph operations.
    /// </summary>
    public string? GraphProvider { get; init; }

    /// <summary>
    /// The provider used for document metadata storage.
    /// </summary>
    public string? RdbProvider { get; init; }

    /// <summary>
    /// The provider used for semantic caching.
    /// </summary>
    public string? SemanticCacheProvider { get; init; }

    /// <summary>
    /// Gets whether vector search is available.
    /// </summary>
    public bool HasVector => VectorProvider is not null;

    /// <summary>
    /// Gets whether graph operations are available.
    /// </summary>
    public bool HasGraph => GraphProvider is not null;

    /// <summary>
    /// Gets whether document metadata storage is available.
    /// </summary>
    public bool HasRdb => RdbProvider is not null;

    /// <summary>
    /// Gets whether semantic caching is available.
    /// </summary>
    public bool HasSemanticCache => SemanticCacheProvider is not null;

    /// <summary>
    /// Gets a summary of the configuration for logging.
    /// </summary>
    public override string ToString()
    {
        var parts = new List<string>();
        if (HasVector) parts.Add($"Vector={VectorProvider}");
        if (HasGraph) parts.Add($"Graph={GraphProvider}");
        if (HasRdb) parts.Add($"RDB={RdbProvider}");
        if (HasSemanticCache) parts.Add($"Cache={SemanticCacheProvider}");
        return parts.Count > 0 ? string.Join(", ", parts) : "No storage configured";
    }
}
