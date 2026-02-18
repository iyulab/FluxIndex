using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Stack.Application.Interfaces.Services;
using LMSupply.Embedder;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Stack.Infrastructure.Services;

/// <summary>
/// Factory for creating embedding providers based on configuration.
/// Supports LMSupply (local), OpenAI, Azure OpenAI, and other providers via Core interfaces.
/// LMSupply package is directly referenced following FluxIndex SDK design principles
/// (consumer app implements AI service wrappers).
/// </summary>
public partial class EmbeddingServiceFactory : IEmbeddingServiceFactory
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
        LogCreatingEmbeddingProvider(_logger, providerName, modelName ?? "default");

        // If no API key is provided, fall back to local LMSupply
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            LogNoApiKeyFallback(_logger, providerName);
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

    public async Task<IEmbeddingProvider> CreateLocalProviderAsync(
        string? modelName = null,
        CancellationToken cancellationToken = default)
    {
        // Map common model names to LMSupply model IDs
        var effectiveModel = MapToLMSupplyModel(modelName);

        LogCreatingLocalProvider(_logger, effectiveModel);

        try
        {
            // Use LMSupply.Embedder directly (consumer app pattern)
            var embeddingModel = await LocalEmbedder.LoadAsync(effectiveModel, cancellationToken: cancellationToken);
            var wrapper = new LMSupplyEmbeddingWrapper(embeddingModel, _logger);
            return wrapper;
        }
        catch (Exception ex)
        {
            LogLoadModelFailed(_logger, effectiveModel, ex);
            throw;
        }
    }

    private Task<IEmbeddingProvider> CreateOpenAIProviderAsync(
        string apiKey,
        string? modelName,
        string? endpointUrl,
        CancellationToken cancellationToken)
    {
        var effectiveModel = modelName ?? "text-embedding-3-small";
        LogCreatingOpenAIProvider(_logger, effectiveModel, endpointUrl ?? "api.openai.com");

        try
        {
            var providerLogger = _loggerFactory.CreateLogger<OpenAIEmbeddingProvider>();
            var provider = new OpenAIEmbeddingProvider(apiKey, effectiveModel, endpointUrl, providerLogger);
            return Task.FromResult<IEmbeddingProvider>(provider);
        }
        catch (Exception ex)
        {
            LogOpenAIProviderCreationFailed(_logger, effectiveModel, ex);
            throw;
        }
    }

    private Task<IEmbeddingProvider> HandleUnsupportedProvider(
        string providerName,
        string? modelName,
        CancellationToken cancellationToken)
    {
        LogUnsupportedProviderFallback(_logger, providerName);
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

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Creating embedding provider: Provider={Provider}, Model={Model}")]
    private static partial void LogCreatingEmbeddingProvider(ILogger logger, string provider, string model);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No API key provided for {Provider}. Falling back to LMSupply local embedder.")]
    private static partial void LogNoApiKeyFallback(ILogger logger, string provider);

    [LoggerMessage(Level = LogLevel.Information, Message = "Creating LMSupply local embedding provider with model: {Model}")]
    private static partial void LogCreatingLocalProvider(ILogger logger, string model);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to load LMSupply embedding model {Model}")]
    private static partial void LogLoadModelFailed(ILogger logger, string model, Exception? exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Creating OpenAI embedding provider: Model={Model}, Endpoint={Endpoint}")]
    private static partial void LogCreatingOpenAIProvider(ILogger logger, string model, string endpoint);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to create OpenAI embedding provider for model {Model}")]
    private static partial void LogOpenAIProviderCreationFailed(ILogger logger, string model, Exception? exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Provider} embedding not yet implemented. Falling back to LMSupply local embedder.")]
    private static partial void LogUnsupportedProviderFallback(ILogger logger, string provider);

    #endregion
}

/// <summary>
/// Wrapper that adapts LMSupply IEmbeddingModel to IEmbeddingProvider (Stack Application).
/// Consumer app pattern: directly wraps LMSupply without SDK intermediary.
/// </summary>
internal sealed class LMSupplyEmbeddingWrapper : IEmbeddingProvider, IAsyncDisposable
{
    private readonly IEmbeddingModel _model;
    private readonly ILogger _logger;

    public LMSupplyEmbeddingWrapper(IEmbeddingModel model, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        _logger = logger;
    }

    public int EmbeddingDimension => _model.Dimensions;

    public string ModelName => _model.ModelId;

    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        return await _model.EmbedAsync(text, cancellationToken);
    }

    public async Task<float[][]> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        var textList = texts.ToList();
        var results = new List<float[]>();

        foreach (var text in textList)
        {
            var embedding = await _model.EmbedAsync(text, cancellationToken);
            results.Add(embedding);
        }

        return results.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        if (_model is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else if (_model is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
