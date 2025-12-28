using FluxIndex.Core.Application.Interfaces;
using FluxIndex.SDK.AI.Local;
using FluxIndex.SDK.AI.Local.Services;
using FluxIndex.Stack.Application.Interfaces.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Stack.Infrastructure.Services;

/// <summary>
/// Factory for creating embedding providers based on configuration.
/// Supports LMSupply (local), OpenAI, Azure OpenAI, and other providers via Core interfaces.
/// </summary>
public class EmbeddingServiceFactory : IEmbeddingServiceFactory
{
    private readonly IMemoryCache _cache;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<EmbeddingServiceFactory> _logger;

    private static readonly List<string> _supportedProviders = new()
    {
        "OpenAI",
        "Azure",
        "GPUStack",
        "OpenAI-Compatible",
        "Local",
        "LMSupply",
        "Cohere",
        "Google"
    };

    public EmbeddingServiceFactory(
        IMemoryCache cache,
        ILoggerFactory loggerFactory,
        ILogger<EmbeddingServiceFactory> logger)
    {
        _cache = cache;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public IReadOnlyList<string> SupportedProviders => _supportedProviders;

    public Task<IEmbeddingProvider> CreateProviderAsync(
        string providerName,
        string? apiKey,
        string? modelName,
        string? endpointUrl = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Creating embedding provider: Provider={Provider}, Model={Model}",
            providerName, modelName ?? "default");

        // If no API key is provided, fall back to local LMSupply
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning(
                "No API key provided for {Provider}. Falling back to LMSupply local embedder.",
                providerName);
            return CreateLocalProviderAsync(modelName, cancellationToken);
        }

        var normalizedProvider = providerName.ToLowerInvariant();

        return normalizedProvider switch
        {
            "local" or "localembedder" or "lmsupply" =>
                CreateLocalProviderAsync(modelName, cancellationToken),

            "openai" =>
                CreateOpenAIProviderAsync(apiKey, modelName, null, cancellationToken),

            "azure" or "azureopenai" =>
                CreateOpenAIProviderAsync(apiKey, modelName, endpointUrl, cancellationToken),

            "gpustack" or "openai-compatible" or "openaicompatible" =>
                CreateOpenAIProviderAsync(apiKey, modelName, endpointUrl, cancellationToken),

            "cohere" or "google" =>
                HandleUnsupportedProvider(normalizedProvider, modelName, cancellationToken),

            _ when !string.IsNullOrWhiteSpace(endpointUrl) =>
                CreateOpenAIProviderAsync(apiKey, modelName, endpointUrl, cancellationToken),

            _ => HandleUnsupportedProvider(normalizedProvider, modelName, cancellationToken)
        };
    }

    public Task<IEmbeddingProvider> CreateLocalProviderAsync(
        string? modelName = null,
        CancellationToken cancellationToken = default)
    {
        // Map common model names to LMSupply model IDs
        var effectiveModel = MapToLMSupplyModel(modelName);

        _logger.LogInformation(
            "Creating LMSupply local embedding provider with model: {Model}",
            effectiveModel);

        var options = new LMSupplyEmbeddingOptions
        {
            ModelId = effectiveModel,
            ExecutionProvider = LMSupplyExecutionProvider.Auto,
            PoolingMode = LMSupplyPoolingMode.Mean,
            NormalizeEmbeddings = true
        };

        var serviceLogger = _loggerFactory.CreateLogger<LMSupplyEmbeddingService>();
        var service = new LMSupplyEmbeddingService(
            Options.Create(options),
            serviceLogger,
            _cache);

        var provider = new EmbeddingServiceWrapper(service);
        return Task.FromResult<IEmbeddingProvider>(provider);
    }

    private Task<IEmbeddingProvider> CreateOpenAIProviderAsync(
        string apiKey,
        string? modelName,
        string? endpointUrl,
        CancellationToken cancellationToken)
    {
        // For now, fall back to local provider
        // TODO: Implement external provider support via FluxIndex.Core interfaces
        _logger.LogWarning(
            "External embedding providers require consumer implementation. " +
            "Falling back to LMSupply local embedder. " +
            "Requested: Model={Model}, Endpoint={Endpoint}",
            modelName, endpointUrl ?? "default");

        return CreateLocalProviderAsync(modelName, cancellationToken);
    }

    private Task<IEmbeddingProvider> HandleUnsupportedProvider(
        string providerName,
        string? modelName,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "{Provider} embedding not yet implemented. Falling back to LMSupply local embedder.",
            providerName);
        return CreateLocalProviderAsync(modelName, cancellationToken);
    }

    private static string MapToLMSupplyModel(string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
            return "all-MiniLM-L6-v2";

        return modelName.ToLowerInvariant() switch
        {
            "text-embedding-3-small" => "all-MiniLM-L6-v2", // Fallback for OpenAI model
            "text-embedding-3-large" => "bge-large-en-v1.5", // Fallback for larger model
            "text-embedding-ada-002" => "all-MiniLM-L6-v2",
            "multilingual" => "multilingual-e5-small",
            "multilingual-large" => "multilingual-e5-base",
            "bge-small" => "bge-small-en-v1.5",
            "bge-large" => "bge-large-en-v1.5",
            _ => modelName // Use as-is for LMSupply model IDs
        };
    }
}

/// <summary>
/// Wrapper that adapts IEmbeddingService (Core) to IEmbeddingProvider (Stack Application).
/// </summary>
internal class EmbeddingServiceWrapper : IEmbeddingProvider
{
    private readonly IEmbeddingService _service;

    public EmbeddingServiceWrapper(IEmbeddingService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public int EmbeddingDimension => _service.GetEmbeddingDimension();

    public string ModelName => _service.GetModelName();

    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        return await _service.GenerateEmbeddingAsync(text, cancellationToken);
    }

    public async Task<float[][]> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        var embeddings = await _service.GenerateEmbeddingsBatchAsync(texts, cancellationToken);
        return embeddings.ToArray();
    }
}
