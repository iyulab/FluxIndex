using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LocalRerankerLib = LocalReranker;

namespace FluxIndex.AI.LocalReranker;

/// <summary>
/// Adapter that wraps LocalReranker's IReranker to implement FluxIndex's IReranker interface.
/// Provides cross-encoder based semantic reranking using local ONNX models.
/// </summary>
public sealed class LocalRerankerAdapter : IReranker, IAsyncDisposable, IDisposable
{
    private readonly LocalRerankerLib.Reranker _reranker;
    private readonly ILogger<LocalRerankerAdapter> _logger;
    private readonly LocalRerankerOptions _options;
    private bool _disposed;

    public LocalRerankerAdapter(
        IOptions<LocalRerankerOptions> options,
        ILogger<LocalRerankerAdapter>? logger = null)
    {
        _options = options?.Value ?? new LocalRerankerOptions();
        _logger = logger ?? NullLogger<LocalRerankerAdapter>.Instance;

        var rerankerOptions = new LocalRerankerLib.RerankerOptions
        {
            ModelId = _options.ModelId,
            MaxSequenceLength = _options.MaxSequenceLength,
            UseGpu = _options.UseGpu,
            BatchSize = _options.BatchSize,
            CacheDirectory = _options.CacheDirectory,
            ThreadCount = _options.ThreadCount
        };

        _reranker = new LocalRerankerLib.Reranker(rerankerOptions);
        _logger.LogInformation("LocalRerankerAdapter initialized with model: {ModelId}", _options.ModelId);
    }

    /// <summary>
    /// Pre-loads the model to avoid cold start latency on first inference.
    /// </summary>
    public async Task WarmupAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Warming up LocalReranker model...");
        await _reranker.WarmupAsync(cancellationToken);
        _logger.LogInformation("LocalReranker warmup completed");
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
            _logger.LogWarning("No candidates provided for reranking");
            return [];
        }

        _logger.LogDebug("Reranking {Count} candidates with LocalReranker", candidateList.Count);

        // Extract document contents for LocalReranker
        var documents = candidateList.Select(c => TruncateContent(c.Content, rerankOptions.MaxContentLength));

        // Call LocalReranker
        var rankedResults = await _reranker.RerankAsync(
            query,
            documents,
            rerankOptions.TopN,
            cancellationToken);

        // Convert LocalReranker results to FluxIndex RerankResult
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
                Explanation = rerankOptions.IncludeExplanation
                    ? GenerateExplanation(originalCandidate, r.Score, newRank + 1)
                    : null
            };
        });

        // Apply score threshold filter
        var filteredResults = results
            .Where(r => r.RerankScore >= rerankOptions.ScoreThreshold)
            .ToList();

        _logger.LogDebug("LocalReranker completed: {Original} → {Final} results",
            candidateList.Count, filteredResults.Count);

        return filteredResults;
    }

    /// <inheritdoc />
    public RerankModelInfo GetModelInfo()
    {
        var modelInfo = _reranker.GetModelInfo();

        return new RerankModelInfo
        {
            Name = modelInfo?.DisplayName ?? $"LocalReranker ({_options.ModelId})",
            Type = RerankModel.Local,
            Version = "1.0.0",
            SupportsMultilingual = modelInfo?.IsMultilingual ?? false,
            MaxInputLength = modelInfo?.MaxSequenceLength ?? 512,
            EstimatedLatencyMs = 50.0f, // Approximate based on cross-encoder inference
            RequiresApiKey = false,
            Capabilities = new Dictionary<string, object>
            {
                ["model_id"] = _options.ModelId,
                ["use_gpu"] = _options.UseGpu,
                ["batch_size"] = _options.BatchSize,
                ["cross_encoder"] = true,
                ["local_inference"] = true,
                ["parameters"] = modelInfo?.Parameters ?? 0,
                ["size_mb"] = modelInfo?.SizeMB ?? 0
            }
        };
    }

    private static string TruncateContent(string content, int maxLength)
    {
        if (string.IsNullOrEmpty(content) || content.Length <= maxLength)
            return content;

        return content[..maxLength];
    }

    private static string GenerateExplanation(RetrievalCandidate original, float rerankScore, int newRank)
    {
        var rankChange = original.InitialRank - newRank;
        var scoreChange = rerankScore - original.InitialScore;

        var rankDirection = rankChange switch
        {
            > 0 => $"improved by {rankChange} positions",
            < 0 => $"dropped by {Math.Abs(rankChange)} positions",
            _ => "unchanged"
        };

        return $"Cross-encoder reranking: score={rerankScore:F4}, rank {rankDirection} " +
               $"(score delta: {scoreChange:+0.000;-0.000})";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _reranker.Dispose();
        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await _reranker.DisposeAsync();
        _disposed = true;
    }
}
