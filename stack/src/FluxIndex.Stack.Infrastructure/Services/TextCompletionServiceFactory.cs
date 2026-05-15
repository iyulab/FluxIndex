using System.Text;
using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Base;
using FluxIndex.Stack.Application.Interfaces.Services;
using LMSupply.Generator;
using LMSupply.Generator.Abstractions;
using LMSupply.Generator.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Stack.Infrastructure.Services;

/// <summary>
/// Factory for creating text completion service providers based on configuration.
/// Supports LMSupply (local), OpenAI, Azure OpenAI, and mock providers.
/// </summary>
public partial class TextCompletionServiceFactory : ITextCompletionServiceFactory
{
    private readonly IMemoryCache _cache;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<TextCompletionServiceFactory> _logger;

    private static readonly List<string> _supportedProviders =
    [
        "OpenAI",
        "Azure",
        "LMSupply",
        "Local",
        "Mock"
    ];

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
        LogCreatingTextCompletionProvider(_logger, providerName, modelName ?? "default");

        var normalizedProvider = providerName.ToLowerInvariant();

        // Local providers don't need API key
        if (normalizedProvider is "local" or "lmsupply")
        {
            return CreateLocalProviderAsync(modelName, cancellationToken);
        }

        // If no API key is provided, fall back to mock
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            LogNoApiKeyFallbackToMock(_logger, providerName);
            return Task.FromResult<ITextCompletionService>(new MockTextCompletionService());
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

    public async Task<ITextCompletionService> CreateLocalProviderAsync(
        string? modelName = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveModel = MapToLMSupplyModel(modelName);

        LogCreatingLocalTextCompletionProvider(_logger, effectiveModel);

        try
        {
            var generatorModel = await LocalGenerator.LoadAsync(effectiveModel, cancellationToken: cancellationToken);
            var wrapper = new LMSupplyTextCompletionWrapper(generatorModel, _logger);
            LogLMSupplyGeneratorLoaded(_logger, effectiveModel);
            return wrapper;
        }
        catch (Exception ex)
        {
            LogLMSupplyGeneratorLoadFailed(_logger, effectiveModel, ex);
            throw;
        }
    }

    private Task<ITextCompletionService> CreateExternalProviderAsync(
        string apiKey,
        string? modelName,
        string? endpointUrl,
        CancellationToken cancellationToken)
    {
        var effectiveModel = modelName ?? "gpt-4o-mini";
        LogCreatingExternalProvider(_logger, effectiveModel, endpointUrl ?? "api.openai.com");

        try
        {
            var serviceLogger = _loggerFactory.CreateLogger<OpenAITextCompletionService>();
            var service = new OpenAITextCompletionService(apiKey, effectiveModel, endpointUrl, serviceLogger);
            return Task.FromResult<ITextCompletionService>(service);
        }
        catch (Exception ex)
        {
            LogExternalProviderCreationFailed(_logger, effectiveModel, ex);
            throw;
        }
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

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Creating text completion provider: Provider={Provider}, Model={Model}")]
    private static partial void LogCreatingTextCompletionProvider(ILogger logger, string provider, string model);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No API key provided for {Provider}. Falling back to mock text completion.")]
    private static partial void LogNoApiKeyFallbackToMock(ILogger logger, string provider);

    [LoggerMessage(Level = LogLevel.Information, Message = "Creating local text completion provider with model: {Model}")]
    private static partial void LogCreatingLocalTextCompletionProvider(ILogger logger, string model);

    [LoggerMessage(Level = LogLevel.Information, Message = "LMSupply.Generator model loaded successfully: {Model}")]
    private static partial void LogLMSupplyGeneratorLoaded(ILogger logger, string model);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to load LMSupply.Generator model: {Model}")]
    private static partial void LogLMSupplyGeneratorLoadFailed(ILogger logger, string model, Exception? exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Creating external text completion provider: Model={Model}, Endpoint={Endpoint}")]
    private static partial void LogCreatingExternalProvider(ILogger logger, string model, string endpoint);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to create external text completion provider: {Model}")]
    private static partial void LogExternalProviderCreationFailed(ILogger logger, string model, Exception? exception);

    #endregion
}

/// <summary>
/// Wrapper that adapts LMSupply ITextGenerator to ITextCompletionService (FluxIndex Core).
/// Uses <see cref="TextCompletionServiceBase"/> for default JSON extraction and token counting.
/// Consumer app pattern: directly wraps LMSupply without SDK intermediary.
/// </summary>
internal sealed class LMSupplyTextCompletionWrapper : TextCompletionServiceBase, IAsyncDisposable
{
    private readonly ITextGenerator _generator;
    private readonly ILogger _logger;

    public LMSupplyTextCompletionWrapper(ITextGenerator generator, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(generator);
        _generator = generator;
        _logger = logger;
    }

    protected override async Task<string> CompleteCoreAsync(
        string prompt,
        Flux.Abstractions.TextCompletionOptions options,
        CancellationToken cancellationToken)
    {
        var genOptions = new GenerationOptions
        {
            MaxTokens = options.MaxTokens,
            Temperature = options.Temperature,
        };

        return await _generator.GenerateCompleteAsync(prompt, genOptions, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _generator.DisposeAsync();
    }
}
