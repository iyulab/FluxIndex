using System.ClientModel;
using FluxIndex.Stack.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Embeddings;

namespace FluxIndex.Stack.Infrastructure.Services;

/// <summary>
/// Embedding provider that uses the OpenAI API (or Azure OpenAI / OpenAI-compatible endpoints).
/// Consumer app pattern: directly wraps OpenAI SDK without ironhive intermediary.
/// </summary>
internal sealed partial class OpenAIEmbeddingProvider : IEmbeddingProvider
{
    private readonly EmbeddingClient _client;
    private readonly ILogger _logger;

    /// <summary>
    /// Known embedding model dimensions.
    /// </summary>
    private static readonly Dictionary<string, int> KnownDimensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["text-embedding-3-small"] = 1536,
        ["text-embedding-3-large"] = 3072,
        ["text-embedding-ada-002"] = 1536,
    };

    public OpenAIEmbeddingProvider(
        string apiKey,
        string modelName,
        string? endpointUrl,
        ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);

        ModelName = modelName;
        _logger = logger;

        var credential = new ApiKeyCredential(apiKey);

        if (!string.IsNullOrWhiteSpace(endpointUrl))
        {
            // Azure OpenAI or OpenAI-compatible endpoint
            var options = new OpenAIClientOptions { Endpoint = new Uri(endpointUrl) };
            var client = new OpenAIClient(credential, options);
            _client = client.GetEmbeddingClient(modelName);
        }
        else
        {
            // Standard OpenAI API
            _client = new EmbeddingClient(modelName, credential);
        }

        EmbeddingDimension = KnownDimensions.TryGetValue(modelName, out var dim) ? dim : 1536;
    }

    public int EmbeddingDimension { get; }

    public string ModelName { get; }

    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        LogGeneratingEmbedding(_logger, ModelName, text.Length);

        var response = await _client.GenerateEmbeddingAsync(text, cancellationToken: cancellationToken);
        return response.Value.ToFloats().ToArray();
    }

    public async Task<float[][]> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        var textList = texts.ToList();
        if (textList.Count == 0)
            return [];

        LogGeneratingBatchEmbeddings(_logger, ModelName, textList.Count);

        var response = await _client.GenerateEmbeddingsAsync(textList, cancellationToken: cancellationToken);
        return response.Value
            .OrderBy(e => e.Index)
            .Select(e => e.ToFloats().ToArray())
            .ToArray();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Generating embedding via {Model} (text length={TextLength})")]
    private static partial void LogGeneratingEmbedding(ILogger logger, string model, int textLength);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Generating batch embeddings via {Model} (count={Count})")]
    private static partial void LogGeneratingBatchEmbeddings(ILogger logger, string model, int count);
}
