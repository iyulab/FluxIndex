using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Services.Reranking;
using LocalAI.Reranker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FluxIndex.AI.Local.Services;

/// <summary>
/// Resilient reranker that provides automatic fallback from semantic (cross-encoder)
/// to algorithmic (TF-IDF/BM25) reranking when the neural model is unavailable.
/// </summary>
/// <remarks>
/// Implements the Composite Reranker pattern:
/// - Primary: Cross-encoder semantic reranking (high quality, requires ONNX model)
/// - Fallback: Algorithmic reranking (lower quality, always available)
///
/// Fallback is triggered when:
/// - Model download fails (network issues, firewall)
/// - Model loading fails (disk issues, memory)
/// - Runtime inference fails (unexpected errors)
/// </remarks>
public sealed class ResilientLocalAIReranker : IReranker, IAsyncDisposable
{
    private readonly LocalAIRerankerOptions _options;
    private readonly ILogger<ResilientLocalAIReranker> _logger;
    private readonly AlgorithmicReranker _fallbackReranker;
    private IRerankerModel? _semanticReranker;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _semanticAvailable;
    private bool _initAttempted;
    private bool _disposed;

    public ResilientLocalAIReranker(
        IOptions<LocalAIRerankerOptions> options,
        IEmbeddingService? embeddingService = null,
        ILogger<ResilientLocalAIReranker>? logger = null)
    {
        _options = options?.Value ?? new LocalAIRerankerOptions();
        _logger = logger ?? NullLogger<ResilientLocalAIReranker>.Instance;

        // Initialize algorithmic fallback (always available)
        _fallbackReranker = new AlgorithmicReranker(
            embeddingService,
            new AlgorithmicRerankOptions
            {
                TfIdfWeight = 0.4f,
                Bm25Weight = 0.3f,
                SemanticWeight = embeddingService != null ? 0.3f : 0.0f
            });

        _logger.LogInformation(
            "Resilient LocalAI Reranker initialized: Model={ModelId}, HasEmbeddingFallback={HasEmbedding}",
            _options.ModelId, embeddingService != null);
    }

    /// <summary>
    /// Gets whether the semantic (cross-encoder) reranker is available.
    /// </summary>
    public bool IsSemanticAvailable => _semanticAvailable && _semanticReranker != null;

    /// <summary>
    /// Gets the current reranking method being used.
    /// </summary>
    public RerankMethod CurrentMethod => IsSemanticAvailable
        ? RerankMethod.Semantic
        : RerankMethod.Algorithmic;

    private async ValueTask<IRerankerModel?> TryGetSemanticModelAsync(CancellationToken cancellationToken = default)
    {
        if (_semanticReranker != null)
            return _semanticReranker;

        if (_initAttempted && !_semanticAvailable)
            return null;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_semanticReranker != null)
                return _semanticReranker;

            if (_initAttempted)
                return null;

            _initAttempted = true;

            try
            {
                _logger.LogInformation("Attempting to load LocalAI reranker model: {Model}", _options.ModelId);

                var rerankerOptions = new RerankerOptions
                {
                    ModelId = _options.ModelId,
                    MaxSequenceLength = _options.MaxSequenceLength,
                    BatchSize = _options.BatchSize,
                    CacheDirectory = _options.CacheDirectory,
                    Provider = _options.ToExecutionProvider(),
                    ThreadCount = _options.ThreadCount
                };

                _semanticReranker = await LocalReranker.LoadAsync(
                    _options.ModelId,
                    rerankerOptions,
                    null,
                    cancellationToken);

                _semanticAvailable = true;
                _logger.LogInformation(
                    "Semantic reranker loaded successfully: {Model}",
                    _semanticReranker.ModelId);

                return _semanticReranker;
            }
            catch (Exception ex)
            {
                _semanticAvailable = false;
                _logger.LogWarning(ex,
                    "Semantic reranker unavailable, using algorithmic fallback. Reason: {Message}",
                    ex.Message);
                return null;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<RerankResult>> RerankAsync(
        string query,
        IEnumerable<RetrievalCandidate> candidates,
        RerankOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var rerankOptions = options ?? new RerankOptions();
        var candidateList = candidates.ToList();

        if (candidateList.Count == 0)
        {
            _logger.LogDebug("No candidates provided for reranking");
            return [];
        }

        // Try semantic reranking first
        var semanticModel = await TryGetSemanticModelAsync(cancellationToken);
        if (semanticModel != null)
        {
            try
            {
                return await RerankWithSemanticAsync(
                    semanticModel, query, candidateList, rerankOptions, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Semantic reranking failed, falling back to algorithmic. Reason: {Message}",
                    ex.Message);
            }
        }

        // Fallback to algorithmic
        return await RerankWithAlgorithmicAsync(
            query, candidateList, rerankOptions, cancellationToken);
    }

    private async Task<IEnumerable<RerankResult>> RerankWithSemanticAsync(
        IRerankerModel model,
        string query,
        List<RetrievalCandidate> candidateList,
        RerankOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Using semantic reranker for {Count} candidates", candidateList.Count);

        var documents = candidateList
            .Select(c => TruncateContent(c.Content, options.MaxContentLength))
            .ToList();

        var rankedResults = await model.RerankAsync(
            query,
            documents,
            options.TopN,
            cancellationToken);

        var results = rankedResults.Select((r, newRank) =>
        {
            var originalCandidate = candidateList[r.OriginalIndex];
            return new RerankResult
            {
                Id = originalCandidate.Id,
                DocumentId = originalCandidate.DocumentId,
                ChunkId = originalCandidate.ChunkId,
                Content = originalCandidate.Content,
                InitialScore = originalCandidate.InitialScore,
                InitialRank = originalCandidate.InitialRank,
                RerankScore = r.Score,
                NewRank = newRank + 1,
                Metadata = originalCandidate.Metadata,
                Explanation = options.IncludeExplanation
                    ? $"Cross-encoder semantic reranking: score={r.Score:F4}"
                    : null
            };
        });

        return results
            .Where(r => r.RerankScore >= options.ScoreThreshold)
            .ToList();
    }

    private async Task<IEnumerable<RerankResult>> RerankWithAlgorithmicAsync(
        string query,
        List<RetrievalCandidate> candidateList,
        RerankOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Using algorithmic reranker for {Count} candidates", candidateList.Count);

        var results = await _fallbackReranker.RerankAsync(
            query,
            candidateList,
            options,
            cancellationToken);

        // Add method indicator to explanation
        if (options.IncludeExplanation)
        {
            results = results.Select(r =>
            {
                r.Explanation = $"[Fallback] {r.Explanation}";
                return r;
            });
        }

        return results;
    }

    /// <inheritdoc />
    public RerankModelInfo GetModelInfo()
    {
        if (_semanticReranker != null)
        {
            var modelInfo = _semanticReranker.GetModelInfo();
            return new RerankModelInfo
            {
                Name = modelInfo?.DisplayName ?? $"LocalAI Reranker ({_options.ModelId})",
                Type = RerankModel.Local,
                Version = "1.0.0",
                SupportsMultilingual = modelInfo?.Alias == "multilingual",
                MaxInputLength = modelInfo?.MaxSequenceLength ?? 512,
                EstimatedLatencyMs = 50.0f,
                RequiresApiKey = false,
                Capabilities = new Dictionary<string, object>
                {
                    ["model_id"] = _options.ModelId,
                    ["cross_encoder"] = true,
                    ["local_inference"] = true,
                    ["has_fallback"] = true,
                    ["current_method"] = CurrentMethod.ToString()
                }
            };
        }

        // Fallback mode info
        return new RerankModelInfo
        {
            Name = "Algorithmic Reranker (Fallback)",
            Type = RerankModel.Local,
            Version = "1.0.0",
            SupportsMultilingual = true,
            MaxInputLength = 2048,
            EstimatedLatencyMs = 10.0f,
            RequiresApiKey = false,
            Capabilities = new Dictionary<string, object>
            {
                ["model_id"] = "algorithmic",
                ["cross_encoder"] = false,
                ["local_inference"] = true,
                ["has_fallback"] = true,
                ["current_method"] = CurrentMethod.ToString(),
                ["supports_tf_idf"] = true,
                ["supports_bm25"] = true
            }
        };
    }

    /// <summary>
    /// Attempts to upgrade from algorithmic to semantic reranking if model becomes available.
    /// </summary>
    public async Task<bool> TryUpgradeToSemanticAsync(CancellationToken cancellationToken = default)
    {
        if (_semanticReranker != null)
        {
            _logger.LogDebug("Semantic reranker already available");
            return true;
        }

        // Reset init state to allow retry
        await _initLock.WaitAsync(cancellationToken);
        try
        {
            _initAttempted = false;
        }
        finally
        {
            _initLock.Release();
        }

        var model = await TryGetSemanticModelAsync(cancellationToken);
        return model != null;
    }

    private static string TruncateContent(string content, int maxLength)
    {
        if (string.IsNullOrEmpty(content) || content.Length <= maxLength)
            return content;

        return content[..maxLength];
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        if (_semanticReranker != null)
        {
            await _semanticReranker.DisposeAsync();
            _semanticReranker = null;
        }

        _initLock.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// Indicates which reranking method is being used.
/// </summary>
public enum RerankMethod
{
    /// <summary>
    /// Cross-encoder neural network reranking (higher quality).
    /// </summary>
    Semantic,

    /// <summary>
    /// TF-IDF/BM25 algorithmic reranking (fallback).
    /// </summary>
    Algorithmic
}
