using FluxIndex.Core.Application.Interfaces;
using LocalEmbedder;
using LocalEmbedder.Download;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace FluxIndex.AI.LocalEmbedder.Services;

/// <summary>
/// Local ONNX-based implementation of IEmbeddingService using LocalEmbedder
/// Provides offline, GPU-accelerated text embeddings without external API calls
/// </summary>
public class LocalEmbedderService : IEmbeddingService, IAsyncDisposable, IDisposable
{
    private readonly LocalEmbedderOptions _options;
    private readonly IMemoryCache? _cache;
    private readonly ILogger<LocalEmbedderService> _logger;
    private IEmbeddingModel? _model;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _disposed;

    public LocalEmbedderService(
        IOptions<LocalEmbedderOptions> options,
        ILogger<LocalEmbedderService> logger,
        IMemoryCache? cache = null)
    {
        _options = options.Value;
        _options.Validate();
        _logger = logger;
        _cache = cache;

        _logger.LogInformation("LocalEmbedder Service configured: Model={Model}, Provider={Provider}, Pooling={Pooling}",
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

            _logger.LogInformation("Loading LocalEmbedder model: {Model}", _options.ModelId);

            var embedderOptions = new EmbedderOptions
            {
                MaxSequenceLength = _options.MaxSequenceLength,
                NormalizeEmbeddings = _options.NormalizeEmbeddings,
                Provider = MapExecutionProvider(_options.ExecutionProvider),
                PoolingMode = MapPoolingMode(_options.PoolingMode)
            };

            _model = await global::LocalEmbedder.LocalEmbedder.LoadAsync(
                _options.ModelId,
                embedderOptions,
                progress: new Progress<DownloadProgress>(p =>
                    _logger.LogDebug("Model loading: {File} - {Downloaded}/{Total}",
                        p.FileName, p.BytesDownloaded, p.TotalBytes)));

            _logger.LogInformation("LocalEmbedder model loaded: {Model}, Dimensions={Dimensions}",
                _model.ModelId, _model.Dimensions);

            return _model;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogWarning("Empty or null text provided for embedding generation");
            return Array.Empty<float>();
        }

        // Check cache first
        var cacheKey = GenerateCacheKey(text);
        if (_cache != null)
        {
            var cachedEmbedding = _cache.Get<float[]>(cacheKey);
            if (cachedEmbedding != null)
            {
                _logger.LogDebug("Cache hit for LocalEmbedder embedding");
                return cachedEmbedding;
            }
        }

        try
        {
            var model = await GetModelAsync(cancellationToken);

            _logger.LogDebug("Generating local embedding for text of length {Length}", text.Length);

            var embedding = await model.EmbedAsync(text, cancellationToken);

            // Cache the result
            if (_cache != null)
            {
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromHours(24))
                    .SetSize(embedding.Length * sizeof(float)); // Size in bytes
                _cache.Set(cacheKey, embedding, cacheOptions);
            }

            _logger.LogDebug("Local embedding generated: {Dimensions} dimensions", embedding.Length);
            return embedding;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate local embedding for text: {Text}",
                text.Substring(0, Math.Min(50, text.Length)));
            throw;
        }
    }

    public async Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        var textList = texts.ToList();
        if (!textList.Any())
            return Array.Empty<float[]>();

        var embeddings = new List<float[]>();
        var uncachedTexts = new List<(int index, string text)>();
        var cachedResults = new Dictionary<int, float[]>();

        // Check cache for all texts
        for (int i = 0; i < textList.Count; i++)
        {
            var text = textList[i];
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var cacheKey = GenerateCacheKey(text);
            if (_cache?.TryGetValue(cacheKey, out float[]? cachedEmbedding) == true && cachedEmbedding != null)
            {
                cachedResults[i] = cachedEmbedding;
                _logger.LogDebug("Cache hit for batch item {Index}", i);
            }
            else
            {
                uncachedTexts.Add((i, text));
            }
        }

        // Generate embeddings for uncached texts
        if (uncachedTexts.Any())
        {
            _logger.LogInformation("Generating local embeddings for {Count} uncached texts", uncachedTexts.Count);

            try
            {
                var model = await GetModelAsync(cancellationToken);
                var batchTexts = uncachedTexts.Select(x => x.text).ToList();

                // Use batch API for efficiency
                var batchEmbeddings = await model.EmbedAsync(batchTexts, cancellationToken);

                for (int i = 0; i < uncachedTexts.Count && i < batchEmbeddings.Length; i++)
                {
                    var embedding = batchEmbeddings[i];
                    var originalIndex = uncachedTexts[i].index;
                    cachedResults[originalIndex] = embedding;

                    // Cache the result
                    if (_cache != null)
                    {
                        var cacheKey = GenerateCacheKey(uncachedTexts[i].text);
                        var cacheOptions = new MemoryCacheEntryOptions()
                            .SetAbsoluteExpiration(TimeSpan.FromHours(24))
                            .SetSize(embedding.Length * sizeof(float)); // Size in bytes
                        _cache.Set(cacheKey, embedding, cacheOptions);
                    }
                }

                _logger.LogInformation("Generated {Count} local embeddings in batch", uncachedTexts.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Batch embedding failed, falling back to individual calls");

                // Fallback to individual calls
                foreach (var (index, text) in uncachedTexts)
                {
                    var embedding = await GenerateEmbeddingAsync(text, cancellationToken);
                    cachedResults[index] = embedding;
                }
            }
        }

        // Reconstruct results in original order
        for (int i = 0; i < textList.Count; i++)
        {
            if (cachedResults.TryGetValue(i, out var result))
            {
                embeddings.Add(result);
            }
            else
            {
                embeddings.Add(Array.Empty<float>());
            }
        }

        _logger.LogInformation("Batch local embeddings: {Total} texts, {Cached} cached, {Generated} generated",
            textList.Count, textList.Count - uncachedTexts.Count, uncachedTexts.Count);

        return embeddings;
    }

    public int GetEmbeddingDimension()
    {
        if (_model != null)
            return _model.Dimensions;

        return _options.GetEffectiveDimensions();
    }

    public string GetModelName() => _options.ModelId;

    public int GetMaxTokens() => _options.MaxTokens;

    public Task<int> CountTokensAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
            return Task.FromResult(0);

        // More accurate token estimation based on BPE/WordPiece tokenization patterns
        // This accounts for:
        // - Word boundaries (spaces)
        // - Subword tokenization (longer words = more tokens)
        // - Punctuation (usually separate tokens)
        // - Non-ASCII characters (CJK characters typically 1 token each)

        var tokenCount = 0;

        // Count by analyzing text characteristics
        var words = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words)
        {
            if (word.Length == 0) continue;

            // Count punctuation as separate tokens
            int punctCount = word.Count(c => char.IsPunctuation(c));
            tokenCount += punctCount;

            // Remove punctuation for word analysis
            var cleanWord = new string(word.Where(c => !char.IsPunctuation(c)).ToArray());
            if (string.IsNullOrEmpty(cleanWord)) continue;

            // Check for CJK characters (Chinese, Japanese, Korean)
            // These are typically tokenized as individual characters
            int cjkChars = cleanWord.Count(c => IsCjkCharacter(c));
            if (cjkChars > 0)
            {
                tokenCount += cjkChars;
                cleanWord = new string(cleanWord.Where(c => !IsCjkCharacter(c)).ToArray());
            }

            if (string.IsNullOrEmpty(cleanWord)) continue;

            // For remaining text (Latin, etc.):
            // - Short words (1-4 chars): typically 1 token
            // - Medium words (5-10 chars): typically 1-2 tokens
            // - Long words (11+ chars): estimate based on subword tokenization
            if (cleanWord.Length <= 4)
            {
                tokenCount += 1;
            }
            else if (cleanWord.Length <= 10)
            {
                tokenCount += 1 + (cleanWord.Length - 4) / 6;
            }
            else
            {
                // Longer words tend to be split into ~3.5 chars/token
                tokenCount += (int)Math.Ceiling(cleanWord.Length / 3.5);
            }
        }

        // Account for special tokens ([CLS], [SEP]) which are added during encoding
        tokenCount += 2;

        return Task.FromResult(tokenCount);
    }

    private static bool IsCjkCharacter(char c)
    {
        // CJK Unified Ideographs and common CJK ranges
        return (c >= '\u4E00' && c <= '\u9FFF') ||   // CJK Unified Ideographs
               (c >= '\u3400' && c <= '\u4DBF') ||   // CJK Extension A
               (c >= '\uAC00' && c <= '\uD7AF') ||   // Hangul Syllables (Korean)
               (c >= '\u3040' && c <= '\u309F') ||   // Hiragana (Japanese)
               (c >= '\u30A0' && c <= '\u30FF');     // Katakana (Japanese)
    }

    private string GenerateCacheKey(string text)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes($"{_options.ModelId}:{text}"));
        return $"localembedder:{Convert.ToBase64String(hash).Replace("/", "_").Replace("+", "-")[..16]}";
    }

    private static ExecutionProvider MapExecutionProvider(LocalEmbedderExecutionProvider provider)
    {
        return provider switch
        {
            LocalEmbedderExecutionProvider.Auto => ExecutionProvider.Auto,
            LocalEmbedderExecutionProvider.CPU => ExecutionProvider.Cpu,
            LocalEmbedderExecutionProvider.CUDA => ExecutionProvider.Cuda,
            LocalEmbedderExecutionProvider.DirectML => ExecutionProvider.DirectML,
            _ => ExecutionProvider.Auto // Default to Auto for automatic GPU detection
        };
    }

    private static PoolingMode MapPoolingMode(LocalEmbedderPoolingMode mode)
    {
        return mode switch
        {
            LocalEmbedderPoolingMode.Cls => PoolingMode.Cls,
            LocalEmbedderPoolingMode.Mean => PoolingMode.Mean,
            LocalEmbedderPoolingMode.LastToken => PoolingMode.Max, // Map to Max as closest alternative
            _ => PoolingMode.Mean
        };
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            if (_model is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else
            {
                _model?.Dispose();
            }
            _model = null;
            _initLock.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _model?.Dispose();
                _model = null;
                _initLock.Dispose();
            }
            _disposed = true;
        }
    }
}
