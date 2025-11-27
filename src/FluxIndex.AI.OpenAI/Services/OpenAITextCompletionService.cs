using Azure.AI.OpenAI;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using System.Security.Cryptography;
using System.Text;

namespace FluxIndex.AI.OpenAI.Services;

/// <summary>
/// OpenAI-compatible implementation of ITextCompletionService
/// Supports: OpenAI API, Azure OpenAI, GPUStack (v1/v2), and other OpenAI-compatible APIs
/// </summary>
public class OpenAITextCompletionService : ITextCompletionService
{
    private readonly ChatClient _client;
    private readonly OpenAIOptions _options;
    private readonly IMemoryCache? _cache;
    private readonly ILogger<OpenAITextCompletionService> _logger;
    private readonly OpenAIProviderType _providerType;

    public OpenAITextCompletionService(
        IOptions<OpenAIOptions> options,
        ILogger<OpenAITextCompletionService> logger,
        IMemoryCache? cache = null)
    {
        _options = options.Value;
        _logger = logger;
        _cache = cache;
        _providerType = _options.GetEffectiveProviderType();

        _logger.LogInformation("Initializing OpenAI Text Completion Service: Provider={Provider}, Model={Model}, Endpoint={Endpoint}",
            _providerType, _options.ModelName, _options.Endpoint ?? "default");

        // Initialize ChatClient
        _client = CreateChatClient(_options);
    }

    public async Task<string> GenerateCompletionAsync(
        string prompt,
        int maxTokens = 500,
        float temperature = 0.7f,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            _logger.LogWarning("Empty or null prompt provided for text completion");
            return string.Empty;
        }

        // Check cache first if enabled
        var cacheKey = GenerateCacheKey(prompt, temperature, maxTokens);
        if (_cache != null)
        {
            var cachedResponse = _cache.Get<string>(cacheKey);
            if (cachedResponse != null)
            {
                _logger.LogDebug("Cache hit for text completion");
                return cachedResponse;
            }
        }

        try
        {
            _logger.LogInformation("Generating text completion: promptLength={Length}, maxTokens={MaxTokens}, temperature={Temperature}",
                prompt.Length, maxTokens, temperature);

            var messages = new List<ChatMessage>
            {
                ChatMessage.CreateUserMessage(prompt)
            };

            var chatOptions = new ChatCompletionOptions
            {
                Temperature = temperature
            };

            // Apply timeout
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var response = await _client.CompleteChatAsync(messages, chatOptions, linkedCts.Token);
            var completion = response.Value.Content[0].Text;

            _logger.LogInformation("Text completion successful: responseLength={Length}", completion.Length);

            // Cache the result if caching is enabled
            if (_cache != null)
            {
                _cache.Set(cacheKey, completion, TimeSpan.FromHours(1));
            }

            return completion;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Text completion cancelled by user");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate text completion");
            throw;
        }
    }

    public async Task<string> GenerateJsonCompletionAsync(
        string prompt,
        int maxTokens = 500,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            _logger.LogWarning("Empty or null prompt provided for JSON completion");
            return "{}";
        }

        // Check cache first if enabled
        var cacheKey = GenerateCacheKey(prompt, 0.1f, maxTokens, "json");
        if (_cache != null)
        {
            var cachedResponse = _cache.Get<string>(cacheKey);
            if (cachedResponse != null)
            {
                _logger.LogDebug("Cache hit for JSON completion");
                return cachedResponse;
            }
        }

        try
        {
            _logger.LogInformation("Generating JSON completion: promptLength={Length}, maxTokens={MaxTokens}",
                prompt.Length, maxTokens);

            var messages = new List<ChatMessage>
            {
                ChatMessage.CreateSystemMessage("You are a helpful assistant that responds with valid JSON only. Do not include markdown code blocks or additional text."),
                ChatMessage.CreateUserMessage(prompt)
            };

            var chatOptions = new ChatCompletionOptions
            {
                Temperature = 0.1f // Low temperature for structured output
            };

            // Apply timeout
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var response = await _client.CompleteChatAsync(messages, chatOptions, linkedCts.Token);
            var jsonCompletion = response.Value.Content[0].Text;

            _logger.LogInformation("JSON completion successful: responseLength={Length}", jsonCompletion.Length);

            // Cache the result if caching is enabled
            if (_cache != null)
            {
                _cache.Set(cacheKey, jsonCompletion, TimeSpan.FromHours(1));
            }

            return jsonCompletion;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("JSON completion cancelled by user");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate JSON completion");
            throw;
        }
    }

    public int CountTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        // Simple approximation: ~4 characters per token
        // For production use, consider integrating tiktoken library for accurate counting
        return text.Length / 4;
    }

    private ChatClient CreateChatClient(OpenAIOptions options)
    {
        var providerType = options.GetEffectiveProviderType();

        switch (providerType)
        {
            case OpenAIProviderType.OpenAI:
                // Use OpenAI API (api.openai.com)
                var openAIClient = new OpenAIClient(options.ApiKey);
                return openAIClient.GetChatClient(options.ModelName);

            case OpenAIProviderType.AzureOpenAI:
                // Use Azure OpenAI
                var azureClient = new AzureOpenAIClient(
                    new Uri(options.Endpoint!),
                    new System.ClientModel.ApiKeyCredential(options.ApiKey));
                return azureClient.GetChatClient(options.ModelName);

            case OpenAIProviderType.GPUStack:
            case OpenAIProviderType.OpenAICompatible:
            default:
                // Use OpenAI-compatible API (GPUStack, Ollama, vLLM, LM Studio, etc.)
                // GPUStack v1/v2 both use OpenAI-compatible endpoints at /v1/chat/completions
                var endpoint = options.Endpoint!.TrimEnd('/');
                if (!endpoint.EndsWith("/v1"))
                {
                    endpoint += "/v1";
                }

                var compatibleClient = new OpenAIClient(
                    new System.ClientModel.ApiKeyCredential(options.ApiKey),
                    new OpenAIClientOptions { Endpoint = new Uri(endpoint) });
                return compatibleClient.GetChatClient(options.ModelName);
        }
    }

    private string GenerateCacheKey(string prompt, float temperature, int maxTokens, string? suffix = null)
    {
        using var sha256 = SHA256.Create();
        var input = $"{_options.ModelName}:{temperature}:{maxTokens}:{suffix}:{prompt}";
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return $"completion:{Convert.ToBase64String(hash).Replace("/", "_").Replace("+", "-")[..24]}";
    }
}
