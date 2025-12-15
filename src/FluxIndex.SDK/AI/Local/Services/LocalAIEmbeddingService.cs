using System.Security.Cryptography;
using System.Text;
using FluxIndex.Core.Application.Interfaces;
using LocalAI;
using LocalAI.Embedder;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.SDK.AI.Local.Services;

/// <summary>
/// Local ONNX-based implementation of IEmbeddingService using LocalAI.Embedder.
/// Provides offline, GPU-accelerated text embeddings without external API calls.
/// </summary>
public sealed class LocalAIEmbeddingService : IEmbeddingService, IAsyncDisposable
{
    private readonly LocalAIEmbeddingOptions _options;
    private readonly IMemoryCache? _cache;
    private readonly ILogger<LocalAIEmbeddingService> _logger;
    private IEmbeddingModel? _model;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _disposed;

    public LocalAIEmbeddingService(
        IOptions<LocalAIEmbeddingOptions> options,
        ILogger<LocalAIEmbeddingService> logger,
        IMemoryCache? cache = null)
    {
        _options = options.Value;
        _logger = logger;
        _cache = cache;

        _logger.LogInformation(
            "LocalAI Embedding Service configured: Model={Model}, Provider={Provider}, Pooling={Pooling}",
            _options.ModelId, _options.ExecutionProvider, _options.PoolingMode);
    }

    private async ValueTask<IEmbeddingModel> GetModelAsync(CancellationToken cancellationToken = default)
    {
        if (_model != null)
            return _model;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_model != null)
                return _model;

            _logger.LogInformation("Loading LocalAI embedding model: {Model}", _options.ModelId);

            var embedderOptions = new EmbedderOptions
            {
                MaxSequenceLength = _options.MaxSequenceLength,
                NormalizeEmbeddings = _options.NormalizeEmbeddings,
                Provider = _options.ToExecutionProvider(),
                PoolingMode = _options.ToPoolingMode(),
                CacheDirectory = _options.CacheDirectory
            };

            _model = await LocalAI.Embedder.LocalEmbedder.LoadAsync(
                _options.ModelId,
                embedderOptions,
                new Progress<DownloadProgress>(p =>
                    _logger.LogDebug("Model loading: {File} - {Downloaded}/{Total}",
                        p.FileName, p.BytesDownloaded, p.TotalBytes)),
                cancellationToken);

            _logger.LogInformation(
                "LocalAI embedding model loaded: {Model}, Dimensions={Dimensions}",
                _model.ModelId, _model.Dimensions);

            return _model;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogWarning("Empty or null text provided for embedding generation");
            return [];
        }

        var cacheKey = GenerateCacheKey(text);
        if (_cache?.TryGetValue(cacheKey, out float[]? cached) == true && cached != null)
        {
            _logger.LogDebug("Cache hit for embedding");
            return cached;
        }

        try
        {
            var model = await GetModelAsync(cancellationToken);
            _logger.LogDebug("Generating embedding for text of length {Length}", text.Length);

            var embedding = await model.EmbedAsync(text, cancellationToken);

            if (_cache != null)
            {
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromHours(24))
                    .SetSize(embedding.Length * sizeof(float));
                _cache.Set(cacheKey, embedding, cacheOptions);
            }

            _logger.LogDebug("Embedding generated: {Dimensions} dimensions", embedding.Length);
            return embedding;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate embedding for text: {Text}",
                text.Length > 50 ? text[..50] + "..." : text);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var textList = texts.ToList();
        if (textList.Count == 0)
            return [];

        var results = new float[textList.Count][];
        var uncached = new List<(int Index, string Text)>();

        // Check cache
        for (int i = 0; i < textList.Count; i++)
        {
            var text = textList[i];
            if (string.IsNullOrWhiteSpace(text))
            {
                results[i] = [];
                continue;
            }

            var cacheKey = GenerateCacheKey(text);
            if (_cache?.TryGetValue(cacheKey, out float[]? cached) == true && cached != null)
            {
                results[i] = cached;
            }
            else
            {
                uncached.Add((i, text));
            }
        }

        if (uncached.Count == 0)
        {
            _logger.LogDebug("All {Count} embeddings served from cache", textList.Count);
            return results;
        }

        _logger.LogInformation("Generating embeddings for {Count} uncached texts", uncached.Count);

        try
        {
            var model = await GetModelAsync(cancellationToken);
            var batchTexts = uncached.Select(x => x.Text).ToList();
            var embeddings = await model.EmbedAsync(batchTexts, cancellationToken);

            for (int i = 0; i < uncached.Count && i < embeddings.Length; i++)
            {
                var embedding = embeddings[i];
                var originalIndex = uncached[i].Index;
                results[originalIndex] = embedding;

                if (_cache != null)
                {
                    var cacheKey = GenerateCacheKey(uncached[i].Text);
                    var cacheOptions = new MemoryCacheEntryOptions()
                        .SetAbsoluteExpiration(TimeSpan.FromHours(24))
                        .SetSize(embedding.Length * sizeof(float));
                    _cache.Set(cacheKey, embedding, cacheOptions);
                }
            }

            _logger.LogInformation(
                "Batch embeddings: {Total} total, {Cached} cached, {Generated} generated",
                textList.Count, textList.Count - uncached.Count, uncached.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Batch embedding failed, falling back to individual calls");

            foreach (var (index, text) in uncached)
            {
                results[index] = await GenerateEmbeddingAsync(text, cancellationToken);
            }
        }

        // Fill any remaining nulls with empty arrays
        for (int i = 0; i < results.Length; i++)
        {
            results[i] ??= [];
        }

        return results;
    }

    /// <inheritdoc />
    public int GetEmbeddingDimension()
    {
        if (_model != null)
            return _model.Dimensions;

        return _options.GetEffectiveDimensions();
    }

    /// <inheritdoc />
    public string GetModelName() => _options.ModelId;

    /// <inheritdoc />
    public int GetMaxTokens() => _options.MaxTokens;

    /// <inheritdoc />
    public Task<int> CountTokensAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
            return Task.FromResult(0);

        var tokenCount = 0;
        var words = text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words)
        {
            if (word.Length == 0) continue;

            int punctCount = word.Count(char.IsPunctuation);
            tokenCount += punctCount;

            var cleanWord = new string(word.Where(c => !char.IsPunctuation(c)).ToArray());
            if (string.IsNullOrEmpty(cleanWord)) continue;

            int cjkChars = cleanWord.Count(IsCjkCharacter);
            if (cjkChars > 0)
            {
                tokenCount += cjkChars;
                cleanWord = new string(cleanWord.Where(c => !IsCjkCharacter(c)).ToArray());
            }

            if (string.IsNullOrEmpty(cleanWord)) continue;

            tokenCount += cleanWord.Length switch
            {
                <= 4 => 1,
                <= 10 => 1 + (cleanWord.Length - 4) / 6,
                _ => (int)Math.Ceiling(cleanWord.Length / 3.5)
            };
        }

        // Account for special tokens [CLS], [SEP]
        tokenCount += 2;

        return Task.FromResult(tokenCount);
    }

    /// <summary>
    /// Pre-loads the model to avoid cold start latency on first inference.
    /// </summary>
    public async Task WarmupAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Warming up LocalAI embedding model...");
        var model = await GetModelAsync(cancellationToken);
        await model.WarmupAsync(cancellationToken);
        _logger.LogInformation("LocalAI embedding warmup completed");
    }

    private static bool IsCjkCharacter(char c)
    {
        return (c >= '\u4E00' && c <= '\u9FFF') ||   // CJK Unified Ideographs
               (c >= '\u3400' && c <= '\u4DBF') ||   // CJK Extension A
               (c >= '\uAC00' && c <= '\uD7AF') ||   // Hangul Syllables
               (c >= '\u3040' && c <= '\u309F') ||   // Hiragana
               (c >= '\u30A0' && c <= '\u30FF');     // Katakana
    }

    private string GenerateCacheKey(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{_options.ModelId}:{text}"));
        return $"localai-embed:{Convert.ToBase64String(hash).Replace("/", "_").Replace("+", "-")[..16]}";
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        if (_model != null)
        {
            await _model.DisposeAsync();
            _model = null;
        }

        _initLock.Dispose();
        _disposed = true;
    }
}
