using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Core.Application.Services.Storage;

/// <summary>
/// Default implementation of <see cref="IStorageOrchestrator"/>.
/// Automatically selects the best provider for each capability based on priority rules.
/// </summary>
public class StorageOrchestrator : IStorageOrchestrator
{
    private readonly ILogger<StorageOrchestrator>? _logger;
    private readonly List<IStorageProvider> _providers;
    private readonly StorageConfiguration _configuration;

    private readonly IVectorStore? _vectorStore;
    private readonly IGraphStore? _graphStore;
    private readonly IDocumentRepository? _documentRepository;
    private readonly ISemanticCacheService? _semanticCache;

    /// <summary>
    /// Creates a new instance of <see cref="StorageOrchestrator"/>.
    /// </summary>
    /// <param name="providers">All registered storage providers.</param>
    /// <param name="logger">Optional logger.</param>
    public StorageOrchestrator(
        IEnumerable<IStorageProvider> providers,
        ILogger<StorageOrchestrator>? logger = null)
    {
        _logger = logger;
        _providers = providers.ToList();

        // Resolve best provider for each capability
        var vectorProvider = ResolveBestProvider<IVectorCapable>(StorageCapabilities.Vector);
        var graphProvider = ResolveBestProvider<IGraphCapable>(StorageCapabilities.Graph);
        var rdbProvider = ResolveBestProvider<IRdbCapable>(StorageCapabilities.Rdb);
        var cacheProvider = ResolveBestProvider<ISemanticCacheCapable>(StorageCapabilities.SemanticCache);

        _vectorStore = vectorProvider?.VectorStore;
        _graphStore = graphProvider?.GraphStore;
        _documentRepository = rdbProvider?.DocumentRepository;
        _semanticCache = cacheProvider?.SemanticCache;

        _configuration = new StorageConfiguration
        {
            VectorProvider = vectorProvider?.ProviderName,
            GraphProvider = graphProvider?.ProviderName,
            RdbProvider = rdbProvider?.ProviderName,
            SemanticCacheProvider = cacheProvider?.ProviderName
        };

        LogConfiguration();
    }

    /// <inheritdoc />
    public IVectorStore? VectorStore => _vectorStore;

    /// <inheritdoc />
    public IGraphStore? GraphStore => _graphStore;

    /// <inheritdoc />
    public IDocumentRepository? DocumentRepository => _documentRepository;

    /// <inheritdoc />
    public ISemanticCacheService? SemanticCache => _semanticCache;

    /// <inheritdoc />
    public StorageConfiguration GetConfiguration() => _configuration;

    /// <inheritdoc />
    public IReadOnlyList<IStorageProvider> GetProviders() => _providers.AsReadOnly();

    /// <summary>
    /// Resolves the best provider for a specific capability.
    /// Priority: Specialized providers > General-purpose providers.
    /// Among same-priority providers, last registered wins.
    /// </summary>
    private TCapable? ResolveBestProvider<TCapable>(StorageCapabilities capability)
        where TCapable : class, IStorageProvider
    {
        var candidates = _providers
            .Where(p => p.Capabilities.HasFlag(capability))
            .OfType<TCapable>()
            .ToList();

        if (candidates.Count == 0)
            return null;

        // Prefer specialized providers
        var specialized = candidates.Where(p => p.IsSpecialized).ToList();
        if (specialized.Count > 0)
        {
            // Last registered specialized provider wins
            var selected = specialized[^1];
            _logger?.LogDebug(
                "Selected specialized provider {Provider} for {Capability}",
                selected.ProviderName, capability);
            return selected;
        }

        // Fall back to general-purpose (last registered)
        var generalPurpose = candidates[^1];
        _logger?.LogDebug(
            "Selected general-purpose provider {Provider} for {Capability}",
            generalPurpose.ProviderName, capability);
        return generalPurpose;
    }

    private void LogConfiguration()
    {
        if (_logger is null) return;

        if (_providers.Count == 0)
        {
            _logger.LogWarning("No storage providers registered");
            return;
        }

        _logger.LogInformation(
            "Storage configuration: {Configuration}",
            _configuration.ToString());

        foreach (var provider in _providers)
        {
            _logger.LogDebug(
                "Registered provider: {Provider} (Capabilities: {Capabilities}, Specialized: {IsSpecialized})",
                provider.ProviderName,
                provider.Capabilities,
                provider.IsSpecialized);
        }
    }
}
