using System.ClientModel;
using Flux.Abstractions;
using FluxIndex.Core.Application.Services.Base;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;

namespace FluxIndex.Stack.Infrastructure.Services;

/// <summary>
/// OpenAI text completion service implementation (ChatClient-based).
/// Consumer app pattern: directly wraps Azure.AI.OpenAI SDK.
/// Supports OpenAI, Azure OpenAI, and OpenAI-compatible endpoints.
/// </summary>
internal sealed partial class OpenAITextCompletionService : TextCompletionServiceBase
{
    private readonly ChatClient _chatClient;
    private readonly ILogger _logger;
    private readonly string _modelName;

    public OpenAITextCompletionService(
        string apiKey,
        string modelName,
        string? endpointUrl,
        ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);

        _modelName = modelName;
        _logger = logger;

        var credential = new ApiKeyCredential(apiKey);

        if (!string.IsNullOrWhiteSpace(endpointUrl))
        {
            var options = new OpenAIClientOptions { Endpoint = new Uri(endpointUrl) };
            var client = new OpenAIClient(credential, options);
            _chatClient = client.GetChatClient(modelName);
        }
        else
        {
            _chatClient = new ChatClient(modelName, credential);
        }
    }

    protected override async Task<string> CompleteCoreAsync(
        string prompt,
        TextCompletionOptions options,
        CancellationToken cancellationToken)
    {
        LogGenerating(_logger, _modelName, prompt.Length, options.MaxTokens, options.Temperature);

        var messages = new List<ChatMessage> { new UserChatMessage(prompt) };
        var chatOptions = new ChatCompletionOptions
        {
            Temperature = options.Temperature,
            MaxOutputTokenCount = options.MaxTokens,
        };

        var response = await _chatClient.CompleteChatAsync(messages, chatOptions, cancellationToken);
        return response.Value.Content[0].Text ?? string.Empty;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Generating text via {Model} (prompt length={PromptLength}, maxTokens={MaxTokens}, temp={Temperature})")]
    private static partial void LogGenerating(ILogger logger, string model, int promptLength, int maxTokens, float temperature);
}
