namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Orchestrates multiple storage providers to automatically select
/// the best provider for each capability.
/// </summary>
/// <remarks>
/// Priority rules:
/// 1. Specialized providers take priority over general-purpose providers
/// 2. When multiple specialized providers exist, the last registered one wins
/// 3. General-purpose providers fill in for missing capabilities
/// </remarks>
public interface IStorageOrchestrator
{
    /// <summary>
    /// Gets the resolved vector store, or null if no provider supports it.
    /// </summary>
    IVectorStore? VectorStore { get; }

    /// <summary>
    /// Gets the resolved graph store, or null if no provider supports it.
    /// </summary>
    IGraphStore? GraphStore { get; }

    /// <summary>
    /// Gets the resolved document repository, or null if no provider supports it.
    /// </summary>
    IDocumentRepository? DocumentRepository { get; }

    /// <summary>
    /// Gets the resolved semantic cache service, or null if no provider supports it.
    /// </summary>
    ISemanticCacheService? SemanticCache { get; }

    /// <summary>
    /// Gets the current storage configuration showing which provider
    /// is used for each capability.
    /// </summary>
    StorageConfiguration GetConfiguration();

    /// <summary>
    /// Gets all registered providers.
    /// </summary>
    IReadOnlyList<IStorageProvider> GetProviders();
}
