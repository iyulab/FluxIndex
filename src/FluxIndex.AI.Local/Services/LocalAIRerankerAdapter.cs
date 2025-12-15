using FluxIndex.Core.Application.Interfaces;
using LocalAI.Reranker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FluxIndex.AI.Local.Services;

/// <summary>
/// Adapter that wraps LocalAI.Reranker to implement FluxIndex's IReranker interface.
/// Provides cross-encoder based semantic reranking using local ONNX models.
/// </summary>
public sealed class LocalAIRerankerAdapter : IReranker, IAsyncDisposable
{
    private readonly LocalAIRerankerOptions _options;
    private readonly ILogger<LocalAIRerankerAdapter> _logger;
    private IRerankerModel? _model;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _disposed;

    public LocalAIRerankerAdapter(
        IOptions<LocalAIRerankerOptions> options,
        ILogger<LocalAIRerankerAdapter>? logger = null)
    {
        _options = options?.Value ?? new LocalAIRerankerOptions();
        _logger = logger ?? NullLogger<LocalAIRerankerAdapter>.Instance;

        _logger.LogInformation(
            "LocalAI Reranker Adapter configured: Model={ModelId}, BatchSize={BatchSize}",
            _options.ModelId, _options.BatchSize);
    }

    private async ValueTask<IRerankerModel> GetModelAsync(CancellationToken cancellationToken = default)
    {
        if (_model != null)
            return _model;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_model != null)
                return _model;

            _logger.LogInformation("Loading LocalAI reranker model: {Model}", _options.ModelId);

            var rerankerOptions = new RerankerOptions
            {
                ModelId = _options.ModelId,
                MaxSequenceLength = _options.MaxSequenceLength,
                BatchSize = _options.BatchSize,
                CacheDirectory = _options.CacheDirectory,
                Provider = _options.ToExecutionProvider(),
                ThreadCount = _options.ThreadCount
            };

            _model = await LocalReranker.LoadAsync(
                _options.ModelId,
                rerankerOptions,
                null,
                cancellationToken);

            _logger.LogInformation("LocalAI reranker model loaded: {Model}", _model.ModelId);
            return _model;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Pre-loads the model to avoid cold start latency on first inference.
    /// </summary>
    public async Task WarmupAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Warming up LocalAI reranker model...");
        var model = await GetModelAsync(cancellationToken);
        await model.WarmupAsync(cancellationToken);
        _logger.LogInformation("LocalAI reranker warmup completed");
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

        _logger.LogDebug("Reranking {Count} candidates with LocalAI Reranker", candidateList.Count);

        var model = await GetModelAsync(cancellationToken);

        // Extract and truncate document contents
        var documents = candidateList
            .Select(c => TruncateContent(c.Content, rerankOptions.MaxContentLength))
            .ToList();

        // Call LocalAI Reranker
        var rankedResults = await model.RerankAsync(
            query,
            documents,
            rerankOptions.TopN,
            cancellationToken);

        // Convert to FluxIndex RerankResult
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

        _logger.LogDebug(
            "LocalAI Reranker completed: {Original} -> {Final} results",
            candidateList.Count, filteredResults.Count);

        return filteredResults;
    }

    /// <inheritdoc />
    public RerankModelInfo GetModelInfo()
    {
        var modelInfo = _model?.GetModelInfo();

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
                ["provider"] = _options.ExecutionProvider.ToString(),
                ["batch_size"] = _options.BatchSize,
                ["cross_encoder"] = true,
                ["local_inference"] = true
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
