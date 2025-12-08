using FluxIndex.AI.OpenAI;
using FluxIndex.AI.OpenAI.Services;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Stack.Application.Interfaces.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Stack.Infrastructure.Services;

/// <summary>
/// Factory for creating text completion service providers based on configuration.
/// Supports OpenAI, Azure OpenAI, and mock providers.
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

        // If no API key is provided, fall back to mock
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning(
                "No API key provided for {Provider}. Falling back to MockTextCompletionService.",
                providerName);
            return Task.FromResult<ITextCompletionService>(new MockTextCompletionService());
        }

        var normalizedProvider = providerName.ToLowerInvariant();

        switch (normalizedProvider)
        {
            case "openai":
                return CreateOpenAIProviderAsync(apiKey, modelName, cancellationToken);

            case "azure":
            case "azureopenai":
                return CreateAzureProviderAsync(apiKey, modelName, endpointUrl, cancellationToken);

            case "mock":
                return Task.FromResult<ITextCompletionService>(new MockTextCompletionService());

            default:
                _logger.LogWarning(
                    "Unknown provider: {Provider}. Falling back to MockTextCompletionService.",
                    providerName);
                return Task.FromResult<ITextCompletionService>(new MockTextCompletionService());
        }
    }

    private Task<ITextCompletionService> CreateOpenAIProviderAsync(
        string apiKey,
        string? modelName,
        CancellationToken cancellationToken)
    {
        var effectiveModel = modelName ?? "gpt-4o-mini";

        _logger.LogInformation(
            "Creating OpenAI text completion provider with model: {Model}",
            effectiveModel);

        var options = new OpenAIOptions
        {
            ApiKey = apiKey,
            ModelName = effectiveModel,
            ProviderType = OpenAIProviderType.OpenAI
        };

        var serviceLogger = _loggerFactory.CreateLogger<OpenAITextCompletionService>();
        var service = new OpenAITextCompletionService(
            Options.Create(options),
            serviceLogger);

        return Task.FromResult<ITextCompletionService>(service);
    }

    private Task<ITextCompletionService> CreateAzureProviderAsync(
        string apiKey,
        string? modelName,
        string? endpointUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpointUrl))
        {
            _logger.LogWarning(
                "Azure OpenAI requires endpoint URL. Falling back to MockTextCompletionService.");
            return Task.FromResult<ITextCompletionService>(new MockTextCompletionService());
        }

        var effectiveModel = modelName ?? "gpt-4o-mini";

        _logger.LogInformation(
            "Creating Azure OpenAI text completion provider: Model={Model}, Endpoint={Endpoint}",
            effectiveModel, endpointUrl);

        var options = new OpenAIOptions
        {
            ApiKey = apiKey,
            ModelName = effectiveModel,
            Endpoint = endpointUrl,
            ProviderType = OpenAIProviderType.AzureOpenAI
        };

        var serviceLogger = _loggerFactory.CreateLogger<OpenAITextCompletionService>();
        var service = new OpenAITextCompletionService(
            Options.Create(options),
            serviceLogger);

        return Task.FromResult<ITextCompletionService>(service);
    }
}
