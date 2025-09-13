using Azure.AI.OpenAI;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.ClientModel;

namespace FluxIndex.AI.OpenAI.Services;

/// <summary>
/// OpenAI implementation of ITextCompletionService
/// Supports both OpenAI API and Azure OpenAI
/// </summary>
public class OpenAITextCompletionService : ITextCompletionService
{
    private readonly AzureOpenAIClient _client;
    private readonly OpenAIConfiguration _config;
    private readonly ILogger<OpenAITextCompletionService> _logger;

    public OpenAITextCompletionService(
        IOptions<OpenAIConfiguration> configuration,
        ILogger<OpenAITextCompletionService> logger)
    {
        _config = configuration.Value;
        _logger = logger;

        // Initialize OpenAI client based on configuration
        _client = CreateOpenAIClient(_config);
    }

    public async Task<string> GenerateCompletionAsync(
        string prompt,
        int maxTokens = 500,
        float temperature = 0.7f,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Generating completion for prompt length: {PromptLength}", prompt.Length);

            var chatCompletionsOptions = new ChatCompletionsOptions
            {
                DeploymentName = _config.TextCompletion.Model,
                MaxTokens = maxTokens > 0 ? maxTokens : _config.TextCompletion.MaxTokens,
                Temperature = temperature >= 0 ? temperature : _config.TextCompletion.Temperature,
                TopP = _config.TextCompletion.TopP,
                FrequencyPenalty = _config.TextCompletion.FrequencyPenalty,
                PresencePenalty = _config.TextCompletion.PresencePenalty,
                Messages =
                {
                    new ChatRequestUserMessage(prompt)
                }
            };

            if (_config.EnableDetailedLogging)
            {
                _logger.LogDebug("Request options: {Options}", JsonSerializer.Serialize(new
                {
                    Model = chatCompletionsOptions.DeploymentName,
                    MaxTokens = chatCompletionsOptions.MaxTokens,
                    Temperature = chatCompletionsOptions.Temperature,
                    PromptLength = prompt.Length
                }));
            }

            var response = await _client.GetChatCompletionsAsync(chatCompletionsOptions, cancellationToken);
            var completion = response.Value;

            if (completion.Choices?.Count > 0)
            {
                var result = completion.Choices[0].Message?.Content ?? string.Empty;
                
                _logger.LogDebug("Completion generated successfully. Length: {Length}, Tokens used: {InputTokens}/{OutputTokens}",
                    result.Length,
                    completion.Usage?.PromptTokens ?? 0,
                    completion.Usage?.CompletionTokens ?? 0);

                if (_config.EnableDetailedLogging)
                {
                    _logger.LogDebug("Completion result: {Result}", result);
                }

                return result;
            }

            _logger.LogWarning("No completion choices returned from OpenAI");
            return string.Empty;
        }
        catch (ClientRequestException ex)
        {
            _logger.LogError(ex, "OpenAI API request failed with status {StatusCode}: {Message}",
                ex.Status, ex.Message);
            throw new InvalidOperationException($"Text completion request failed: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during text completion");
            throw new InvalidOperationException($"Text completion failed: {ex.Message}", ex);
        }
    }

    public async Task<IEnumerable<string>> GenerateBatchCompletionsAsync(
        IEnumerable<string> prompts,
        int maxTokens = 500,
        float temperature = 0.7f,
        CancellationToken cancellationToken = default)
    {
        var promptList = prompts.ToList();
        _logger.LogInformation("Generating batch completions for {Count} prompts", promptList.Count);

        var tasks = promptList.Select(prompt => 
            GenerateCompletionAsync(prompt, maxTokens, temperature, cancellationToken));

        try
        {
            var results = await Task.WhenAll(tasks);
            _logger.LogDebug("Batch completion completed successfully");
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch completion failed");
            throw;
        }
    }

    public async Task<string> GenerateJsonCompletionAsync(
        string prompt,
        int maxTokens = 500,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Generating JSON completion for prompt length: {PromptLength}", prompt.Length);

            // Add instruction to generate JSON
            var jsonPrompt = $"{prompt}\n\nRespond with valid JSON only.";

            var result = await GenerateCompletionAsync(jsonPrompt, maxTokens, 0.3f, cancellationToken);

            // Validate JSON
            try
            {
                using var doc = JsonDocument.Parse(result);
                return result;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Generated text is not valid JSON, attempting to fix");
                // Try to extract JSON from the response
                var jsonStart = result.IndexOf('{');
                var jsonEnd = result.LastIndexOf('}');
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    var jsonText = result.Substring(jsonStart, jsonEnd - jsonStart + 1);
                    using var doc = JsonDocument.Parse(jsonText);
                    return jsonText;
                }
                throw new InvalidOperationException("Failed to generate valid JSON", ex);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JSON completion failed");
            throw;
        }
    }

    public int CountTokens(string text)
    {
        // Simple approximation: average 4 characters per token
        // For more accurate counting, use a tokenizer library like tiktoken
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    private static AzureOpenAIClient CreateOpenAIClient(OpenAIConfiguration config)
    {
        if (!string.IsNullOrEmpty(config.BaseUrl))
        {
            // Azure OpenAI or custom endpoint
            var clientOptions = new AzureOpenAIClientOptions();
            return new AzureOpenAIClient(new Uri(config.BaseUrl), new ApiKeyCredential(config.ApiKey), clientOptions);
        }
        else
        {
            // Standard OpenAI API with Azure client
            var clientOptions = new AzureOpenAIClientOptions();
            // For standard OpenAI, use api.openai.com endpoint
            var endpoint = new Uri("https://api.openai.com/v1");
            return new AzureOpenAIClient(endpoint, new ApiKeyCredential(config.ApiKey), clientOptions);
        }
    }
}

/// <summary>
/// Extension methods for easier service configuration
/// </summary>
public static class TextCompletionServiceExtensions
{
    /// <summary>
    /// Test connection to OpenAI service
    /// </summary>
    public static async Task<bool> TestConnectionAsync(this ITextCompletionService service)
    {
        try
        {
            var result = await service.GenerateCompletionAsync("Test", maxTokens: 10, temperature: 0);
            return !string.IsNullOrEmpty(result);
        }
        catch
        {
            return false;
        }
    }
}