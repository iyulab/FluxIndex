using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using FluxIndex.AI.Anthropic.Configuration;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.AI.Anthropic.Services;

/// <summary>
/// Anthropic Claude 기반 텍스트 완성 서비스
/// </summary>
public class AnthropicTextCompletionService : ITextCompletionService
{
    private readonly AnthropicClient _client;
    private readonly AnthropicOptions _options;
    private readonly ILogger<AnthropicTextCompletionService> _logger;

    public AnthropicTextCompletionService(
        IOptions<AnthropicOptions> options,
        ILogger<AnthropicTextCompletionService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new AnthropicClient(_options.ApiKey);
    }

    public async Task<string> GenerateCompletionAsync(
        string prompt,
        int maxTokens = 500,
        float temperature = 0.7f,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Calling Anthropic API with model: {Model}", _options.DefaultModel);

        try
        {
            var messages = new List<Message>
            {
                new Message
                {
                    Role = RoleType.User,
                    Content = new List<ContentBase> { new TextContent { Text = prompt } }
                }
            };

            var parameters = new MessageParameters
            {
                Messages = messages,
                Model = _options.DefaultModel,
                MaxTokens = maxTokens,
                Temperature = (decimal)temperature,
                Stream = false
            };

            var response = await _client.Messages.GetClaudeMessageAsync(parameters, cancellationToken);

            if (response?.Content == null || response.Content.Count == 0)
            {
                throw new InvalidOperationException("Anthropic API returned empty response");
            }

            var textContent = response.Content.FirstOrDefault(c => c is TextContent) as TextContent;
            if (textContent == null)
            {
                throw new InvalidOperationException("Anthropic API response does not contain text content");
            }

            _logger.LogInformation("Anthropic API call successful. Tokens used: {InputTokens}/{OutputTokens}",
                response.Usage.InputTokens, response.Usage.OutputTokens);

            return textContent.Text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Anthropic API");
            throw;
        }
    }

    public async Task<string> GenerateJsonCompletionAsync(
        string prompt,
        int maxTokens = 500,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Calling Anthropic API for JSON completion with model: {Model}", _options.DefaultModel);

        try
        {
            var messages = new List<Message>
            {
                new Message
                {
                    Role = RoleType.User,
                    Content = new List<ContentBase> { new TextContent { Text = prompt + "\n\nReturn ONLY valid JSON (no markdown, no explanations):" } }
                }
            };

            var parameters = new MessageParameters
            {
                Messages = messages,
                Model = _options.DefaultModel,
                MaxTokens = maxTokens,
                Temperature = (decimal)_options.Temperature,
                Stream = false
            };

            var response = await _client.Messages.GetClaudeMessageAsync(parameters, cancellationToken);

            if (response?.Content == null || response.Content.Count == 0)
            {
                throw new InvalidOperationException("Anthropic API returned empty response");
            }

            var textContent = response.Content.FirstOrDefault(c => c is TextContent) as TextContent;
            if (textContent == null)
            {
                throw new InvalidOperationException("Anthropic API response does not contain text content");
            }

            _logger.LogInformation("Anthropic JSON API call successful. Model: {Model}, Tokens: {InputTokens}/{OutputTokens}",
                _options.DefaultModel, response.Usage.InputTokens, response.Usage.OutputTokens);

            return textContent.Text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Anthropic API for JSON completion");
            throw;
        }
    }

    public int CountTokens(string text)
    {
        // Rough approximation for Claude: 1 token ≈ 4 characters
        // Claude uses a similar tokenizer to GPT models
        return text.Length / 4;
    }
}
