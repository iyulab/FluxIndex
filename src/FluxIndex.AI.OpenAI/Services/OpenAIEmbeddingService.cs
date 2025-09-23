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
        if (!textList.Any()) return Array.Empty<float[]>();

        var embeddings = new List<float[]>();
        var uncachedTexts = new List<(int index, string text)>();
        var cachedResults = new Dictionary<int, float[]>();

        // 1. Check cache for all texts
        for (int i = 0; i < textList.Count; i++)
        {
            var text = textList[i];
            if (string.IsNullOrWhiteSpace(text)) continue;

            var cacheKey = GenerateCacheKey(text);
            if (_cache?.TryGetValue(cacheKey, out float[]? cachedEmbedding) == true && cachedEmbedding != null)
            {
                cachedResults[i] = cachedEmbedding;
                _logger.LogDebug("Cache hit for embedding batch item {Index}", i);
            }
            else
            {
                uncachedTexts.Add((i, text));
            }
        }

        // 2. Generate embeddings for uncached texts in optimized batches
        if (uncachedTexts.Any())
        {
            // Process in batches of 50 for better performance
            const int batchSize = 50;
            for (int batchStart = 0; batchStart < uncachedTexts.Count; batchStart += batchSize)
            {
                var batch = uncachedTexts.Skip(batchStart).Take(batchSize).ToList();
                var batchTexts = batch.Select(x => x.text).ToList();

                try
                {
                    // Single API call for batch - major performance improvement
                    var response = await _client.GenerateEmbeddingsAsync(batchTexts, new EmbeddingGenerationOptions(), cancellationToken);

                    for (int i = 0; i < batch.Count && i < response.Value.Count; i++)
                    {
                        var embedding = response.Value[i].ToFloats().ToArray();
                        var originalIndex = batch[i].index;
                        cachedResults[originalIndex] = embedding;

                        // Cache with aggressive caching strategy
                        if (_cache != null)
                        {
                            var cacheKey = GenerateCacheKey(batch[i].text);
                            _cache.Set(cacheKey, embedding, TimeSpan.FromHours(24));
                        }
                    }

                    _logger.LogDebug("Batch embedding successful: {Count} items", batch.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Batch embedding failed, using individual calls");

                    // Fallback to individual calls only when necessary
                    foreach (var (index, text) in batch)
                    {
                        var embedding = await GenerateEmbeddingAsync(text, cancellationToken);
                        cachedResults[index] = embedding;
                    }
                }
            }
        }

        // 3. Reconstruct results in original order
        for (int i = 0; i < textList.Count; i++)
        {
            if (cachedResults.ContainsKey(i))
            {
                embeddings.Add(cachedResults[i]);
            }
            else
            {
                embeddings.Add(Array.Empty<float>());
            }
        }

        _logger.LogInformation("Batch embeddings: {Total} texts, {Cached} cached, {Generated} generated",
            textList.Count, textList.Count - uncachedTexts.Count, uncachedTexts.Count);

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