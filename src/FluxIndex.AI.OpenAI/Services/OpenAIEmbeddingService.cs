using OpenAI;
using OpenAI.Embeddings;
using Azure.AI.OpenAI;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace FluxIndex.AI.OpenAI.Services;

/// <summary>
/// OpenAI implementation of IEmbeddingService
/// Supports both OpenAI API and Azure OpenAI with caching
/// </summary>
public class OpenAIEmbeddingService : IEmbeddingService
{
    private readonly EmbeddingClient _client;
    private readonly OpenAIOptions _options;
    private readonly IMemoryCache? _cache;
    private readonly ILogger<OpenAIEmbeddingService> _logger;

    public OpenAIEmbeddingService(
        IOptions<OpenAIOptions> options,
        ILogger<OpenAIEmbeddingService> logger,
        IMemoryCache? cache = null)
    {
        _options = options.Value;
        _logger = logger;
        _cache = cache;

        // Initialize OpenAI client
        _client = CreateEmbeddingClient(_options);
    }

    public async Task<float[]> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogWarning("Empty or null text provided for embedding generation");
            return Array.Empty<float>();
        }

        // Check cache first if enabled
        var cacheKey = GenerateCacheKey(text);
        if (_cache != null)
        {
            var cachedEmbedding = _cache.Get<float[]>(cacheKey);
            if (cachedEmbedding != null)
            {
                _logger.LogDebug("Cache hit for embedding: {Text}", text.Substring(0, Math.Min(50, text.Length)));
                return cachedEmbedding;
            }
        }

        try
        {
            var options = new EmbeddingGenerationOptions
            {
                Dimensions = _options.Dimensions
            };

            var response = await _client.GenerateEmbeddingAsync(text, options, cancellationToken);
            var embedding = response.Value.ToFloats().ToArray();

            // Cache the result if caching is enabled
            if (_cache != null)
            {
                _cache.Set(cacheKey, embedding, TimeSpan.FromHours(24));
            }

            _logger.LogDebug("Generated embedding for text: {Text}", text.Substring(0, Math.Min(50, text.Length)));
            return embedding;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate embedding for text: {Text}",
                text.Substring(0, Math.Min(50, text.Length)));
            throw;
        }
    }

    public async Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        var textList = texts.ToList();
        var embeddings = new List<float[]>();

        foreach (var text in textList)
        {
            var embedding = await GenerateEmbeddingAsync(text, cancellationToken);
            embeddings.Add(embedding);
        }

        return embeddings;
    }

    public int GetEmbeddingDimension() => _options.Dimensions ?? 1536; // Default for text-embedding-3-small

    public string GetModelName() => _options.ModelName;

    public int GetMaxTokens() => _options.MaxTokens;

    public Task<int> CountTokensAsync(string text, CancellationToken cancellationToken = default)
    {
        // Simple approximation: ~4 characters per token
        var tokenCount = text.Length / 4;
        return Task.FromResult(tokenCount);
    }

    private EmbeddingClient CreateEmbeddingClient(OpenAIOptions options)
    {
        if (string.IsNullOrEmpty(options.Endpoint))
        {
            // Use OpenAI API
            var openAIClient = new OpenAIClient(options.ApiKey);
            return openAIClient.GetEmbeddingClient(options.ModelName);
        }
        else
        {
            // Use Azure OpenAI
            var azureClient = new AzureOpenAIClient(new Uri(options.Endpoint),
                new System.ClientModel.ApiKeyCredential(options.ApiKey));
            return azureClient.GetEmbeddingClient(options.ModelName);
        }
    }

    private string GenerateCacheKey(string text)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes($"{_options.ModelName}:{text}"));
        return Convert.ToBase64String(hash).Replace("/", "_").Replace("+", "-")[..16];
    }
}