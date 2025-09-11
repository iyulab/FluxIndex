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
    private readonly OpenAIClient _client;
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

    private static OpenAIClient CreateOpenAIClient(OpenAIConfiguration config)
    {
        if (!string.IsNullOrEmpty(config.BaseUrl))
        {
            // Azure OpenAI or custom endpoint
            var clientOptions = new OpenAIClientOptions();
            return new OpenAIClient(new Uri(config.BaseUrl), new ApiKeyCredential(config.ApiKey), clientOptions);
        }
        else
        {
            // Standard OpenAI API
            var clientOptions = new OpenAIClientOptions();
            return new OpenAIClient(config.ApiKey, clientOptions);
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