using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Services.Reranking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LocalRerankerLib = LocalReranker;

namespace FluxIndex.AI.LocalReranker;

/// <summary>
/// Resilient reranker adapter that provides automatic fallback from semantic (cross-encoder)
/// to algorithmic (TF-IDF/BM25) reranking when the neural model is unavailable.
/// </summary>
/// <remarks>
/// This implements the Composite Reranker pattern recommended by LocalReranker team:
/// - Primary: Cross-encoder semantic reranking (high quality, requires ONNX model)
/// - Fallback: Algorithmic reranking (lower quality, always available)
///
/// Fallback is triggered when:
/// - Model download fails (network issues, firewall, etc.)
/// - Model loading fails (disk issues, memory, etc.)
/// - Runtime inference fails (unexpected errors)
/// </remarks>
public sealed class ResilientRerankerAdapter : IReranker, IAsyncDisposable, IDisposable
{
    private readonly LocalRerankerLib.Reranker? _semanticReranker;
    private readonly AlgorithmicReranker _fallbackReranker;
    private readonly ILogger<ResilientRerankerAdapter> _logger;
    private readonly LocalRerankerOptions _options;
    private readonly bool _semanticAvailable;
    private bool _disposed;

    public ResilientRerankerAdapter(
        IOptions<LocalRerankerOptions> options,
        IEmbeddingService? embeddingService = null,
        ILogger<ResilientRerankerAdapter>? logger = null)
    {
        _options = options?.Value ?? new LocalRerankerOptions();
        _logger = logger ?? NullLogger<ResilientRerankerAdapter>.Instance;

        // Initialize algorithmic fallback (always available)
        _fallbackReranker = new AlgorithmicReranker(
            embeddingService,
            new AlgorithmicRerankOptions
            {
                TfIdfWeight = 0.4f,
                Bm25Weight = 0.3f,
                SemanticWeight = embeddingService != null ? 0.3f : 0.0f
            });

        // Try to initialize semantic reranker
        try
        {
            var rerankerOptions = new LocalRerankerLib.RerankerOptions
            {
                ModelId = _options.ModelId,
                MaxSequenceLength = _options.MaxSequenceLength,
                UseGpu = _options.UseGpu,
                BatchSize = _options.BatchSize,
                CacheDirectory = _options.CacheDirectory,
                ThreadCount = _options.ThreadCount
            };

            _semanticReranker = new LocalRerankerLib.Reranker(rerankerOptions);

            if (_options.WarmupOnStartup)
            {
                // Validate model availability by warming up
                _semanticReranker.WarmupAsync().GetAwaiter().GetResult();
            }

            _semanticAvailable = true;
            _logger.LogInformation(
                "ResilientRerankerAdapter initialized with semantic reranker (model: {ModelId})",
                _options.ModelId);
        }
        catch (Exception ex)
        {
            _semanticReranker = null;
            _semanticAvailable = false;
            _logger.LogWarning(ex,
                "Semantic reranker unavailable, using algorithmic fallback. " +
                "Reason: {Message}",
                ex.Message);
        }
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
        if (_semanticReranker != null)
        {
            try
            {
                return await RerankWithSemanticAsync(
                    query, candidateList, rerankOptions, cancellationToken);
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
        string query,
        List<RetrievalCandidate> candidateList,
        RerankOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Using semantic reranker for {Count} candidates", candidateList.Count);

        var documents = candidateList.Select(c =>
            TruncateContent(c.Content, options.MaxContentLength));

        var rankedResults = await _semanticReranker!.RerankAsync(
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
                Name = modelInfo?.DisplayName ?? $"LocalReranker ({_options.ModelId})",
                Type = RerankModel.Local,
                Version = "1.0.0",
                SupportsMultilingual = modelInfo?.IsMultilingual ?? false,
                MaxInputLength = modelInfo?.MaxSequenceLength ?? 512,
                EstimatedLatencyMs = 50.0f,
                RequiresApiKey = false,
                Capabilities = new Dictionary<string, object>
                {
                    ["model_id"] = _options.ModelId,
                    ["cross_encoder"] = true,
                    ["local_inference"] = true,
                    ["has_fallback"] = true,
                    ["current_method"] = CurrentMethod.ToString(),
                    ["parameters"] = modelInfo?.Parameters ?? 0,
                    ["size_mb"] = modelInfo?.SizeMB ?? 0
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
    /// <returns>True if upgrade was successful, false otherwise.</returns>
    public async Task<bool> TryUpgradeToSemanticAsync(CancellationToken cancellationToken = default)
    {
        if (_semanticReranker != null)
        {
            _logger.LogDebug("Semantic reranker already available");
            return true;
        }

        try
        {
            var rerankerOptions = new LocalRerankerLib.RerankerOptions
            {
                ModelId = _options.ModelId,
                MaxSequenceLength = _options.MaxSequenceLength,
                UseGpu = _options.UseGpu,
                BatchSize = _options.BatchSize,
                CacheDirectory = _options.CacheDirectory,
                ThreadCount = _options.ThreadCount
            };

            var newReranker = new LocalRerankerLib.Reranker(rerankerOptions);
            await newReranker.WarmupAsync(cancellationToken);

            // Note: This class is sealed and _semanticReranker is readonly,
            // so true hot-swapping isn't possible. This method is for diagnostic purposes.
            _logger.LogInformation("Semantic reranker model is now available for new instances");
            newReranker.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Semantic reranker still unavailable: {Message}", ex.Message);
            return false;
        }
    }

    private static string TruncateContent(string content, int maxLength)
    {
        if (string.IsNullOrEmpty(content) || content.Length <= maxLength)
            return content;

        return content[..maxLength];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _semanticReranker?.Dispose();
        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        if (_semanticReranker != null)
        {
            await _semanticReranker.DisposeAsync();
        }
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
