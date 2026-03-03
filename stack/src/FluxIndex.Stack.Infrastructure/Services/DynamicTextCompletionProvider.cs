using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Stack.Application.Interfaces.Repositories;
using FluxIndex.Stack.Application.Interfaces.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Stack.Infrastructure.Services;

/// <summary>
/// Dynamic text completion provider that reads configuration from AiProviderSettings database.
/// Provides automatic provider selection based on stored settings.
/// </summary>
public partial class DynamicTextCompletionProvider : ITextCompletionService, ITextCompletionProviderCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITextCompletionServiceFactory _textCompletionFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DynamicTextCompletionProvider> _logger;

    private const string CacheKey = "DynamicTextCompletion_CurrentProvider";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public DynamicTextCompletionProvider(
        IServiceScopeFactory scopeFactory,
        ITextCompletionServiceFactory textCompletionFactory,
        IMemoryCache cache,
        ILogger<DynamicTextCompletionProvider> logger)
    {
        _scopeFactory = scopeFactory;
        _textCompletionFactory = textCompletionFactory;
        _cache = cache;
        _logger = logger;
    }

    private async Task<ITextCompletionService> GetActiveProviderAsync(CancellationToken cancellationToken)
    {
        // Check cache first
        if (_cache.TryGetValue<ITextCompletionService>(CacheKey, out var cachedProvider) && cachedProvider != null)
        {
            return cachedProvider;
        }

        // Use scope to access scoped repository from singleton context
        using var scope = _scopeFactory.CreateScope();
        var settingsRepository = scope.ServiceProvider.GetRequiredService<IAiProviderSettingsRepository>();

        // Get default LLM provider from settings
        var settings = await settingsRepository.GetAllAsync(cancellationToken);

        // Priority: 1) Default LLM provider if enabled
        //           2) Any enabled provider with API key
        //           3) LMSupply if enabled (no API key required for local)
        var defaultProvider = settings.FirstOrDefault(s => s.IsDefaultLlm && s.IsEnabled)
                           ?? settings.FirstOrDefault(s => s.IsEnabled && !string.IsNullOrEmpty(s.ApiKey))
                           ?? settings.FirstOrDefault(s => s.IsEnabled && s.ProviderName == "LMSupply");

        ITextCompletionService provider;

        if (defaultProvider != null)
        {
            var isLocalProvider = defaultProvider.ProviderName is "LMSupply" or "Local";

            // Local providers don't need API key
            if (isLocalProvider || !string.IsNullOrEmpty(defaultProvider.ApiKey))
            {
                LogUsingConfiguredProvider(_logger, defaultProvider.ProviderName, defaultProvider.LlmModel ?? "default", isLocalProvider);

                provider = await _textCompletionFactory.CreateProviderAsync(
                    defaultProvider.ProviderName,
                    defaultProvider.ApiKey,
                    defaultProvider.LlmModel,
                    defaultProvider.EndpointUrl,
                    cancellationToken);
            }
            else
            {
                LogProviderEnabledNoApiKey(_logger, defaultProvider.ProviderName);
                provider = new MockTextCompletionService();
            }
        }
        else
        {
            LogNoProviderConfigured(_logger);
            provider = new MockTextCompletionService();
        }

        // Cache the provider with size specification (required when SizeLimit is set)
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(CacheDuration)
            .SetSize(1); // Size of 1 for the provider reference
        _cache.Set(CacheKey, provider, cacheOptions);
        return provider;
    }

    public async Task<string> CompleteAsync(
        string prompt,
        TextCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var provider = await GetActiveProviderAsync(cancellationToken);
        return await provider.CompleteAsync(prompt, options, cancellationToken);
    }

    public async Task<string> CompleteJsonAsync(
        string prompt,
        TextCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var provider = await GetActiveProviderAsync(cancellationToken);
        return await provider.CompleteJsonAsync(prompt, options, cancellationToken);
    }

#pragma warning disable CS0618 // Obsolete member
    public int CountTokens(string text)
#pragma warning restore CS0618
    {
        // Rough approximation: 1 token ~ 4 characters
        return text.Length / 4;
    }

    public void InvalidateCache()
    {
        _cache.Remove(CacheKey);
        LogCacheInvalidated(_logger);
    }

    public async Task<TextCompletionProviderStatus> GetProviderStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var settingsRepository = scope.ServiceProvider.GetRequiredService<IAiProviderSettingsRepository>();
            var settings = await settingsRepository.GetAllAsync(cancellationToken);

            // Same priority as GetActiveProviderAsync
            var defaultProvider = settings.FirstOrDefault(s => s.IsDefaultLlm && s.IsEnabled)
                               ?? settings.FirstOrDefault(s => s.IsEnabled && !string.IsNullOrEmpty(s.ApiKey))
                               ?? settings.FirstOrDefault(s => s.IsEnabled && s.ProviderName == "LMSupply");

            if (defaultProvider != null)
            {
                var isLocalProvider = defaultProvider.ProviderName is "LMSupply" or "Local";

                if (isLocalProvider || !string.IsNullOrEmpty(defaultProvider.ApiKey))
                {
                    return new TextCompletionProviderStatus
                    {
                        ProviderName = defaultProvider.ProviderName,
                        ModelName = defaultProvider.LlmModel ?? (isLocalProvider ? "default" : "gpt-4o-mini"),
                        IsAvailable = true
                    };
                }
            }

            return new TextCompletionProviderStatus
            {
                ProviderName = "Mock",
                ModelName = "mock",
                IsAvailable = true,
                ErrorMessage = "No AI provider configured. Enable LMSupply (local) or configure an API key."
            };
        }
        catch (Exception ex)
        {
            return new TextCompletionProviderStatus
            {
                ProviderName = "Unknown",
                ModelName = "Unknown",
                IsAvailable = false,
                ErrorMessage = ex.Message
            };
        }
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Using configured text completion provider: {Provider}, Model: {Model}, IsLocal: {IsLocal}")]
    private static partial void LogUsingConfiguredProvider(ILogger logger, string provider, string model, bool isLocal);

    [LoggerMessage(Level = LogLevel.Information, Message = "Provider {Provider} is enabled but has no API key. Using MockTextCompletionService.")]
    private static partial void LogProviderEnabledNoApiKey(ILogger logger, string provider);

    [LoggerMessage(Level = LogLevel.Information, Message = "No AI provider configured. Using MockTextCompletionService.")]
    private static partial void LogNoProviderConfigured(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Text completion provider cache invalidated.")]
    private static partial void LogCacheInvalidated(ILogger logger);

    #endregion
}
