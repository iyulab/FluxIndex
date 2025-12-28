using FluxIndex.Core.Application.Interfaces;
using FluxIndex.SDK.AI.Local;
using FluxIndex.SDK.AI.Local.Services;
using FluxIndex.Stack.Application.Interfaces.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Stack.Infrastructure.Services;

/// <summary>
/// Factory for creating text completion service providers based on configuration.
/// Supports LMSupply (local), OpenAI, Azure OpenAI, and mock providers.
/// </summary>
public class TextCompletionServiceFactory : ITextCompletionServiceFactory
{
    private readonly IMemoryCache _cache;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<TextCompletionServiceFactory> _logger;

    private static readonly List<string> _supportedProviders = new()
    {
        "OpenAI",
        "Azure",
        "LMSupply",
        "Local",
        "Mock"
    };

    public TextCompletionServiceFactory(
        IMemoryCache cache,
        ILoggerFactory loggerFactory,
        ILogger<TextCompletionServiceFactory> logger)
    {
        _cache = cache;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public IReadOnlyList<string> SupportedProviders => _supportedProviders;

    public Task<ITextCompletionService> CreateProviderAsync(
        string providerName,
        string? apiKey,
        string? modelName,
        string? endpointUrl = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Creating text completion provider: Provider={Provider}, Model={Model}",
            providerName, modelName ?? "default");

        var normalizedProvider = providerName.ToLowerInvariant();

        // Local providers don't need API key
        if (normalizedProvider is "local" or "lmsupply")
        {
            return CreateLocalProviderAsync(modelName, cancellationToken);
        }

        // If no API key is provided, fall back to local LMSupply
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning(
                "No API key provided for {Provider}. Falling back to LMSupply local text completion.",
                providerName);
            return CreateLocalProviderAsync(modelName, cancellationToken);
        }

        return normalizedProvider switch
        {
            "openai" => CreateExternalProviderAsync(apiKey, modelName, null, cancellationToken),
            "azure" or "azureopenai" => CreateExternalProviderAsync(apiKey, modelName, endpointUrl, cancellationToken),
            "mock" => Task.FromResult<ITextCompletionService>(new MockTextCompletionService()),
            _ when !string.IsNullOrWhiteSpace(endpointUrl) => CreateExternalProviderAsync(apiKey, modelName, endpointUrl, cancellationToken),
            _ => CreateLocalProviderAsync(modelName, cancellationToken)
        };
    }

    public Task<ITextCompletionService> CreateLocalProviderAsync(
        string? modelName = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveModel = MapToLMSupplyModel(modelName);

        _logger.LogInformation(
            "Creating LMSupply local text completion provider with model: {Model}",
            effectiveModel);

        var options = new LMSupplyTextCompletionOptions
        {
            ModelId = effectiveModel,
            MaxContextLength = 4096,
            TopP = 0.95f
        };

        var serviceLogger = _loggerFactory.CreateLogger<LMSupplyTextCompletionService>();
        var service = new LMSupplyTextCompletionService(
            Options.Create(options),
            serviceLogger);

        return Task.FromResult<ITextCompletionService>(service);
    }

    private Task<ITextCompletionService> CreateExternalProviderAsync(
        string apiKey,
        string? modelName,
        string? endpointUrl,
        CancellationToken cancellationToken)
    {
        // For now, fall back to local provider
        // TODO: Implement external provider support via FluxIndex.Core interfaces
        _logger.LogWarning(
            "External text completion providers require consumer implementation. " +
            "Falling back to LMSupply local completion. " +
            "Requested: Model={Model}, Endpoint={Endpoint}",
            modelName, endpointUrl ?? "default");

        return CreateLocalProviderAsync(modelName, cancellationToken);
    }

    private static string MapToLMSupplyModel(string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
            return "default"; // Qwen2.5-0.5B

        return modelName.ToLowerInvariant() switch
        {
            "gpt-4" or "gpt-4o" or "gpt-4o-mini" => "default", // Fallback for OpenAI models
            "fast" or "tinyllama" => "fast", // TinyLlama-1.1B
            "quality" => "quality", // Qwen2.5-1.5B
            "large" => "large", // Qwen2.5-3B
            _ => modelName // Use as-is for LMSupply model IDs
        };
    }
}
