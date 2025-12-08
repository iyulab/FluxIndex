using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Application.Interfaces.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Stack.Infrastructure.Services;

/// <summary>
/// Dynamic embedding provider that reads configuration from AiProviderSettings database.
/// Falls back to LocalEmbedder when no API is configured.
/// Uses IServiceScopeFactory to access scoped repository from singleton context.
/// </summary>
public class DynamicEmbeddingProvider : IEmbeddingProvider, IEmbeddingProviderCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEmbeddingServiceFactory _embeddingServiceFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DynamicEmbeddingProvider> _logger;

    private const string CacheKey = "DynamicEmbedding_CurrentProvider";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public DynamicEmbeddingProvider(
        IServiceScopeFactory scopeFactory,
        IEmbeddingServiceFactory embeddingServiceFactory,
        IMemoryCache cache,
        ILogger<DynamicEmbeddingProvider> logger)
    {
        _scopeFactory = scopeFactory;
        _embeddingServiceFactory = embeddingServiceFactory;
        _cache = cache;
        _logger = logger;
    }

    private async Task<IEmbeddingProvider> GetActiveProviderAsync(CancellationToken cancellationToken)
    {
        // Check cache first
        if (_cache.TryGetValue<IEmbeddingProvider>(CacheKey, out var cachedProvider) && cachedProvider != null)
        {
            return cachedProvider;
        }

        // Use scope to access scoped repository from singleton context
        using var scope = _scopeFactory.CreateScope();
        var settingsRepository = scope.ServiceProvider.GetRequiredService<IAiProviderSettingsRepository>();

        // Get default embedding provider from settings
        var settings = await settingsRepository.GetAllAsync(cancellationToken);
        var defaultEmbedding = settings.FirstOrDefault(s => s.IsDefaultEmbedding && s.IsEnabled);

        IEmbeddingProvider provider;

        if (defaultEmbedding != null && !string.IsNullOrEmpty(defaultEmbedding.ApiKey))
        {
            _logger.LogInformation(
                "Using configured embedding provider: {Provider}, Model: {Model}",
                defaultEmbedding.ProviderName, defaultEmbedding.EmbeddingModel);

            provider = await _embeddingServiceFactory.CreateProviderAsync(
                defaultEmbedding.ProviderName,
                defaultEmbedding.ApiKey,
                defaultEmbedding.EmbeddingModel,
                defaultEmbedding.EndpointUrl,
                cancellationToken);
        }
        else
        {
            _logger.LogInformation(
                "No API-configured embedding provider found. Using LocalEmbedder fallback.");

            provider = await _embeddingServiceFactory.CreateLocalProviderAsync(null, cancellationToken);
        }

        // Cache the provider with size specification (required when SizeLimit is set)
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(CacheDuration)
            .SetSize(1); // Size of 1 for the provider reference
        _cache.Set(CacheKey, provider, cacheOptions);

        return provider;
    }

    public int EmbeddingDimension
    {
        get
        {
            var provider = GetActiveProviderAsync(CancellationToken.None).GetAwaiter().GetResult();
            return provider.EmbeddingDimension;
        }
    }

    public string ModelName
    {
        get
        {
            var provider = GetActiveProviderAsync(CancellationToken.None).GetAwaiter().GetResult();
            return provider.ModelName;
        }
    }

    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var provider = await GetActiveProviderAsync(cancellationToken);
        return await provider.GetEmbeddingAsync(text, cancellationToken);
    }

    public async Task<float[][]> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        var provider = await GetActiveProviderAsync(cancellationToken);
        return await provider.GetEmbeddingsAsync(texts, cancellationToken);
    }

    /// <summary>
    /// Invalidates the cached provider, forcing reconfiguration on next call.
    /// Call this when AI provider settings are changed.
    /// </summary>
    public void InvalidateCache()
    {
        _cache.Remove(CacheKey);
        _logger.LogInformation("Embedding provider cache invalidated. Will reconfigure on next request.");
    }
}
