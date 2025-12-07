using FluxIndex.AI.LocalEmbedder;
using FluxIndex.AI.LocalEmbedder.Services;
using FluxIndex.AI.OpenAI;
using FluxIndex.AI.OpenAI.Services;
using FluxIndex.Stack.Application.Interfaces.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CoreEmbeddingService = FluxIndex.Core.Application.Interfaces.IEmbeddingService;

namespace FluxIndex.Stack.Infrastructure.Services;

/// <summary>
/// Factory for creating embedding providers based on configuration.
/// Supports OpenAI, Azure OpenAI, Local (ONNX), and other OpenAI-compatible providers.
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
        "Local",
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

        // If no API key is provided, fall back to local
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning(
                "No API key provided for {Provider}. Falling back to LocalEmbedder.",
                providerName);
            return CreateLocalProviderAsync(null, cancellationToken);
        }

        var normalizedProvider = providerName.ToLowerInvariant();

        switch (normalizedProvider)
        {
            case "openai":
                return CreateOpenAIProviderAsync(apiKey, modelName, cancellationToken);

            case "azure":
            case "azureopenai":
                return CreateAzureProviderAsync(apiKey, modelName, endpointUrl, cancellationToken);

            case "cohere":
                // Cohere uses OpenAI-compatible API for embeddings
                _logger.LogWarning(
                    "Cohere embedding not yet implemented. Falling back to LocalEmbedder.");
                return CreateLocalProviderAsync(null, cancellationToken);

            case "google":
                // Google Gemini embedding requires different SDK
                _logger.LogWarning(
                    "Google embedding not yet implemented. Falling back to LocalEmbedder.");
                return CreateLocalProviderAsync(null, cancellationToken);

            case "local":
            case "localembedder":
                return CreateLocalProviderAsync(modelName, cancellationToken);

            default:
                _logger.LogWarning(
                    "Unknown provider: {Provider}. Falling back to LocalEmbedder.",
                    providerName);
                return CreateLocalProviderAsync(null, cancellationToken);
        }
    }

    public Task<IEmbeddingProvider> CreateLocalProviderAsync(
        string? modelName = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveModel = modelName ?? "all-MiniLM-L6-v2";

        _logger.LogInformation(
            "Creating LocalEmbedder provider with model: {Model}",
            effectiveModel);

        var options = new LocalEmbedderOptions
        {
            ModelId = effectiveModel,
            ExecutionProvider = LocalEmbedderExecutionProvider.Auto,
            PoolingMode = LocalEmbedderPoolingMode.Mean,
            NormalizeEmbeddings = true
        };

        var serviceLogger = _loggerFactory.CreateLogger<LocalEmbedderService>();
        var service = new LocalEmbedderService(
            Options.Create(options),
            serviceLogger,
            _cache);

        var provider = new EmbeddingServiceWrapper(service);
        return Task.FromResult<IEmbeddingProvider>(provider);
    }

    private Task<IEmbeddingProvider> CreateOpenAIProviderAsync(
        string apiKey,
        string? modelName,
        CancellationToken cancellationToken)
    {
        var effectiveModel = modelName ?? "text-embedding-3-small";

        _logger.LogInformation(
            "Creating OpenAI embedding provider with model: {Model}",
            effectiveModel);

        var options = new OpenAIOptions
        {
            ApiKey = apiKey,
            ModelName = effectiveModel,
            ProviderType = OpenAIProviderType.OpenAI,
            Dimensions = GetDimensionsForModel(effectiveModel)
        };

        var serviceLogger = _loggerFactory.CreateLogger<OpenAIEmbeddingService>();
        var service = new OpenAIEmbeddingService(
            Options.Create(options),
            serviceLogger,
            _cache);

        var provider = new EmbeddingServiceWrapper(service);
        return Task.FromResult<IEmbeddingProvider>(provider);
    }

    private Task<IEmbeddingProvider> CreateAzureProviderAsync(
        string apiKey,
        string? modelName,
        string? endpointUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpointUrl))
        {
            _logger.LogWarning(
                "Azure OpenAI requires endpoint URL. Falling back to LocalEmbedder.");
            return CreateLocalProviderAsync(null, cancellationToken);
        }

        var effectiveModel = modelName ?? "text-embedding-3-small";

        _logger.LogInformation(
            "Creating Azure OpenAI embedding provider: Model={Model}, Endpoint={Endpoint}",
            effectiveModel, endpointUrl);

        var options = new OpenAIOptions
        {
            ApiKey = apiKey,
            ModelName = effectiveModel,
            Endpoint = endpointUrl,
            ProviderType = OpenAIProviderType.AzureOpenAI,
            Dimensions = GetDimensionsForModel(effectiveModel)
        };

        var serviceLogger = _loggerFactory.CreateLogger<OpenAIEmbeddingService>();
        var service = new OpenAIEmbeddingService(
            Options.Create(options),
            serviceLogger,
            _cache);

        var provider = new EmbeddingServiceWrapper(service);
        return Task.FromResult<IEmbeddingProvider>(provider);
    }

    private static int? GetDimensionsForModel(string modelName)
    {
        return modelName.ToLowerInvariant() switch
        {
            "text-embedding-3-small" => 1536,
            "text-embedding-3-large" => 3072,
            "text-embedding-ada-002" => 1536,
            _ => null
        };
    }
}

/// <summary>
/// Wrapper that adapts IEmbeddingService (Core) to IEmbeddingProvider (Stack Application).
/// </summary>
internal class EmbeddingServiceWrapper : IEmbeddingProvider
{
    private readonly CoreEmbeddingService _service;

    public EmbeddingServiceWrapper(CoreEmbeddingService service)
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
