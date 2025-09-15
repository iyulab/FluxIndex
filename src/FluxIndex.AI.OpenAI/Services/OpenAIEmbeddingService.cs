using Azure;
using Azure.AI.OpenAI;
using FluxIndex.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Embeddings;
using System.ClientModel;
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
    private readonly OpenAIConfiguration _config;
    private readonly IMemoryCache? _cache;
    private readonly ILogger<OpenAIEmbeddingService> _logger;

    public OpenAIEmbeddingService(
        IOptions<OpenAIConfiguration> configuration,
        ILogger<OpenAIEmbeddingService> logger,
        IMemoryCache? cache = null)
    {
        _config = configuration.Value;
        _logger = logger;
        _cache = cache;

        // Initialize OpenAI client
        var azureClient = CreateOpenAIClient(_config);
        _client = azureClient.GetEmbeddingClient(_config.Embedding.Model);
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
        if (_config.Embedding.EnableCaching && _cache != null)
        {
            if (_cache.TryGetValue(cacheKey, out float[]? cachedEmbedding) && cachedEmbedding != null)
            {
                _logger.LogDebug("Embedding cache hit for text length: {Length}", text.Length);
                return cachedEmbedding;
            }
        }

        try
        {
            _logger.LogDebug("Generating embedding for text length: {Length}", text.Length);

            var options = new EmbeddingGenerationOptions();

            if (_config.Embedding.Dimensions.HasValue)
            {
                options.Dimensions = _config.Embedding.Dimensions.Value;
            }

            var embedding = await _client.GenerateEmbeddingAsync(text, options, cancellationToken);
            var vector = embedding.Value.ToFloats().ToArray();

                // Cache the result if enabled
                if (_config.Embedding.EnableCaching && _cache != null)
                {
                    var cacheOptions = new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(_config.Embedding.CacheExpiryHours),
                        Size = EstimateSize(vector)
                    };
                    _cache.Set(cacheKey, vector, cacheOptions);
                    _logger.LogDebug("Embedding cached for {Hours} hours", _config.Embedding.CacheExpiryHours);
                }

                _logger.LogDebug("Embedding generated successfully. Dimensions: {Dimensions}",
                    vector.Length);

                return vector;
        }
        catch (ClientResultException ex)
        {
            _logger.LogError(ex, "OpenAI API request failed: {Message}", ex.Message);
            throw new InvalidOperationException($"Embedding generation request failed: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during embedding generation");
            throw new InvalidOperationException($"Embedding generation failed: {ex.Message}", ex);
        }
    }

    public async Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        var textList = texts.ToList();
        _logger.LogInformation("Generating batch embeddings for {Count} texts", textList.Count);

        var results = new List<float[]>();
        var batchSize = _config.Embedding.BatchSize;

        // Process in batches to respect API limits
        for (int i = 0; i < textList.Count; i += batchSize)
        {
            var batch = textList.Skip(i).Take(batchSize);
            var batchResults = await ProcessBatch(batch, cancellationToken);
            results.AddRange(batchResults);

            if (i + batchSize < textList.Count)
            {
                // Small delay between batches to respect rate limits
                await Task.Delay(100, cancellationToken);
            }
        }

        _logger.LogDebug("Batch embedding generation completed for {Total} texts", results.Count);
        return results;
    }

    public int GetEmbeddingDimension()
    {
        return _config.Embedding.Dimensions ?? 1536; // Default for text-embedding-3-small
    }

    public string GetModelName()
    {
        return _config.Embedding.Model;
    }

    public int GetMaxTokens()
    {
        return _config.Embedding.MaxTokens;
    }

    public Task<int> CountTokensAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Task.FromResult(0);

        // Simple approximation: 1 token ≈ 4 characters for English
        // This is a rough estimate - for accurate counts you'd need tiktoken or similar
        return Task.FromResult(text.Length / 4);
    }

    private async Task<IEnumerable<float[]>> ProcessBatch(
        IEnumerable<string> texts,
        CancellationToken cancellationToken)
    {
        var textList = texts.ToList();
        var results = new List<float[]>();
        var uncachedTexts = new List<(string Text, int Index)>();

        // Check cache for each text if enabled
        if (_config.Embedding.EnableCaching && _cache != null)
        {
            for (int i = 0; i < textList.Count; i++)
            {
                var cacheKey = GenerateCacheKey(textList[i]);
                if (_cache.TryGetValue(cacheKey, out float[]? cachedEmbedding) && cachedEmbedding != null)
                {
                    results.Add(cachedEmbedding);
                }
                else
                {
                    uncachedTexts.Add((textList[i], i));
                    results.Add(null!); // Placeholder
                }
            }
        }
        else
        {
            uncachedTexts = textList.Select((text, index) => (text, index)).ToList();
        }

        // Generate embeddings for uncached texts
        if (uncachedTexts.Count > 0)
        {
            try
            {
                var options = new EmbeddingGenerationOptions();

                if (_config.Embedding.Dimensions.HasValue)
                {
                    options.Dimensions = _config.Embedding.Dimensions.Value;
                }

                var textInputs = uncachedTexts.Select(t => t.Text).ToList();
                var embeddingCollection = await _client.GenerateEmbeddingsAsync(textInputs, options, cancellationToken);
                var embeddings = embeddingCollection.Value.ToList();

                // Process results
                for (int i = 0; i < uncachedTexts.Count && i < embeddings.Count; i++)
                {
                    var vector = embeddings[i].ToFloats().ToArray();

                    var (text, originalIndex) = uncachedTexts[i];

                    // Cache the result
                    if (_config.Embedding.EnableCaching && _cache != null)
                    {
                        var cacheKey = GenerateCacheKey(text);
                        var cacheOptions = new MemoryCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(_config.Embedding.CacheExpiryHours),
                            Size = EstimateSize(vector)
                        };
                        _cache.Set(cacheKey, vector, cacheOptions);
                    }

                    // Place result at correct position
                    if (_config.Embedding.EnableCaching && _cache != null)
                    {
                        results[originalIndex] = vector;
                    }
                    else
                    {
                        results.Add(vector);
                    }
                }

                _logger.LogDebug("Generated {Count} new embeddings",
                    uncachedTexts.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Batch embedding generation failed");
                throw;
            }
        }

        return results.Where(r => r != null);
    }

    private static AzureOpenAIClient CreateOpenAIClient(OpenAIConfiguration config)
    {
        if (!string.IsNullOrEmpty(config.BaseUrl))
        {
            // Azure OpenAI or custom endpoint
            var clientOptions = new AzureOpenAIClientOptions();
            return new AzureOpenAIClient(new Uri(config.BaseUrl), new AzureKeyCredential(config.ApiKey), clientOptions);
        }
        else
        {
            // Standard OpenAI API with Azure client
            var clientOptions = new AzureOpenAIClientOptions();
            // For standard OpenAI, use api.openai.com endpoint
            var endpoint = new Uri("https://api.openai.com/v1");
            return new AzureOpenAIClient(endpoint, new AzureKeyCredential(config.ApiKey), clientOptions);
        }
    }

    private static string GenerateCacheKey(string text)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(text));
        return Convert.ToBase64String(hashBytes);
    }

    private static int EstimateSize(float[] vector)
    {
        // Rough estimate: 4 bytes per float + overhead
        return vector.Length * 4 + 64;
    }
}

/// <summary>
/// Extension methods for embedding service
/// </summary>
public static class EmbeddingServiceExtensions
{
    /// <summary>
    /// Test connection to OpenAI embedding service
    /// </summary>
    public static async Task<bool> TestConnectionAsync(this IEmbeddingService service)
    {
        try
        {
            var result = await service.GenerateEmbeddingAsync("test");
            return result.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get embedding dimensions for the configured model
    /// </summary>
    public static async Task<int> GetEmbeddingDimensionsAsync(this IEmbeddingService service)
    {
        var embedding = await service.GenerateEmbeddingAsync("test");
        return embedding.Length;
    }
}