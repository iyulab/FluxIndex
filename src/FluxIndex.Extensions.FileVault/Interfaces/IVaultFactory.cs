using FluxIndex.Extensions.FileVault.Options;

namespace FluxIndex.Extensions.FileVault.Interfaces;

/// <summary>
/// Factory for creating tenant-scoped IVault instances.
///
/// Use this interface when you need isolated vault instances per tenant/context,
/// such as multi-tenant applications where each tenant has their own .vault/ directory.
///
/// Each created vault has:
/// - Isolated .vault/ directory for metadata and extracted content
/// - Isolated queue.db for processing queue persistence
///
/// Shared across all instances (for efficiency):
/// - IContentHasher (stateless)
/// - IGitService (stateless)
/// - IExtractor (stateless)
/// - IChunker (stateless)
/// - IVectorStore (shared vector database)
/// - IEmbeddingService (shared embedding model)
/// </summary>
public interface IVaultFactory : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Gets or creates an IVault instance for the specified tenant.
    /// The vault will use the configured base path with tenant isolation.
    /// </summary>
    /// <param name="tenantId">Unique identifier for the tenant.</param>
    /// <returns>The vault instance for the tenant.</returns>
    IVault GetOrCreate(string tenantId);

    /// <summary>
    /// Gets or creates an IVault instance with custom options.
    /// </summary>
    /// <param name="tenantId">Unique identifier for the tenant.</param>
    /// <param name="configureOptions">Action to configure tenant-specific options.</param>
    /// <returns>The vault instance for the tenant.</returns>
    IVault GetOrCreate(string tenantId, Action<FileVaultOptions> configureOptions);

    /// <summary>
    /// Gets the vault context for a tenant, including queue and pipeline services.
    /// Returns null if the tenant vault hasn't been created yet.
    /// </summary>
    /// <param name="tenantId">Unique identifier for the tenant.</param>
    /// <returns>The vault context or null.</returns>
    VaultContext? GetContext(string tenantId);

    /// <summary>
    /// Checks if a vault exists for the specified tenant.
    /// Checks both in-memory cache and on-disk presence.
    /// </summary>
    /// <param name="tenantId">Unique identifier for the tenant.</param>
    /// <returns>True if vault exists.</returns>
    bool Exists(string tenantId);

    /// <summary>
    /// Discovers all tenants with existing vaults on disk.
    /// </summary>
    /// <returns>Tenant IDs with existing vaults.</returns>
    IEnumerable<string> DiscoverTenants();

    /// <summary>
    /// Gets all currently active (in-memory) tenant IDs.
    /// </summary>
    /// <returns>Active tenant IDs.</returns>
    IReadOnlyCollection<string> GetActiveTenants();

    /// <summary>
    /// Gets all active vault contexts.
    /// Useful for background services that need to process all tenants.
    /// </summary>
    IEnumerable<VaultContext> GetAllContexts();

    /// <summary>
    /// Disposes the vault for a specific tenant.
    /// Releases resources and removes from cache.
    /// </summary>
    /// <param name="tenantId">Unique identifier for the tenant.</param>
    Task DisposeAsync(string tenantId);

    /// <summary>
    /// Disposes all tenant vaults.
    /// </summary>
    Task DisposeAllAsync();
}

/// <summary>
/// Context holding all tenant-specific vault resources.
/// Exposed for background services that need direct access to queue and pipeline.
/// </summary>
public sealed class VaultContext
{
    /// <summary>
    /// Tenant identifier.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Base path for the tenant's vault (.vault directory).
    /// </summary>
    public required string VaultBasePath { get; init; }

    /// <summary>
    /// The vault instance for this tenant.
    /// </summary>
    public required IVault Vault { get; init; }

    /// <summary>
    /// Queue service for this tenant's vault.
    /// </summary>
    public required IVaultQueueService QueueService { get; init; }

    /// <summary>
    /// Storage service for this tenant's vault.
    /// </summary>
    public required IVaultStorageService StorageService { get; init; }

    /// <summary>
    /// Pipeline service for this tenant's vault.
    /// </summary>
    public required IVaultPipeline Pipeline { get; init; }

    /// <summary>
    /// The options used to create this vault.
    /// </summary>
    public required FileVaultOptions Options { get; init; }
}
