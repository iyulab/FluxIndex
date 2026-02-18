using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using FluxImprover.Options;
using FluxImprover.QAGeneration;
using FluxIndexChunk = FluxIndex.Core.Application.Interfaces.IEnrichedChunk;
using FluxImproverEnrichedChunk = FluxImprover.Models.IEnrichedChunk;

namespace FluxIndex.SDK.Extensions.FluxImprover.Services;

/// <summary>
/// 캐싱 지원 파이프라인 실행기 - 인리치먼트 및 QA 생성 결과를 캐싱하여 재처리 방지
/// </summary>
public sealed class CachedPipelineExecutor : IDisposable
{
    private readonly ChunkEnrichmentServiceWrapper? _enrichmentService;
    private readonly QAGenerationService? _qaService;
    private readonly RAGEvaluationService? _evaluationService;
    private readonly ConcurrentDictionary<string, CachedEnrichment> _enrichmentCache;
    private readonly ConcurrentDictionary<string, CachedQAGeneration> _qaCache;
    private readonly CacheOptions _cacheOptions;
    private readonly Timer? _cleanupTimer;
    private bool _disposed;

    /// <summary>
    /// 캐싱 파이프라인 실행기를 초기화합니다.
    /// </summary>
    public CachedPipelineExecutor(
        ChunkEnrichmentServiceWrapper? enrichmentService = null,
        QAGenerationService? qaService = null,
        RAGEvaluationService? evaluationService = null,
        CacheOptions? cacheOptions = null)
    {
        _enrichmentService = enrichmentService;
        _qaService = qaService;
        _evaluationService = evaluationService;
        _cacheOptions = cacheOptions ?? new CacheOptions();
        _enrichmentCache = new ConcurrentDictionary<string, CachedEnrichment>();
        _qaCache = new ConcurrentDictionary<string, CachedQAGeneration>();

        if (_cacheOptions.EnableAutomaticCleanup)
        {
            _cleanupTimer = new Timer(
                CleanupExpiredEntries,
                null,
                _cacheOptions.CleanupInterval,
                _cacheOptions.CleanupInterval);
        }
    }

    /// <summary>
    /// 캐시 통계
    /// </summary>
    public CacheStatistics Statistics => new()
    {
        EnrichmentCacheCount = _enrichmentCache.Count,
        QACacheCount = _qaCache.Count,
        EnrichmentCacheHits = _enrichmentCacheHits,
        EnrichmentCacheMisses = _enrichmentCacheMisses,
        QACacheHits = _qaCacheHits,
        QACacheMisses = _qaCacheMisses
    };

    private long _enrichmentCacheHits;
    private long _enrichmentCacheMisses;
    private long _qaCacheHits;
    private long _qaCacheMisses;

    /// <summary>
    /// 캐싱을 활용하여 청크를 인리치먼트합니다.
    /// </summary>
    public async Task<FluxImproverEnrichedChunk> EnrichWithCacheAsync(
        FluxIndexChunk chunk,
        EnrichmentOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (_enrichmentService == null)
            throw new InvalidOperationException("Enrichment service is not available.");

        var cacheKey = GenerateCacheKey(chunk, options);

        if (_enrichmentCache.TryGetValue(cacheKey, out var cached) && !cached.IsExpired(_cacheOptions.EnrichmentTTL))
        {
            Interlocked.Increment(ref _enrichmentCacheHits);
            return cached.Result;
        }

        Interlocked.Increment(ref _enrichmentCacheMisses);

        var result = await _enrichmentService.EnrichAsync(chunk, options, cancellationToken);

        _enrichmentCache[cacheKey] = new CachedEnrichment
        {
            Result = result,
            CreatedAt = DateTime.UtcNow
        };

        // 캐시 크기 제한 적용
        if (_enrichmentCache.Count > _cacheOptions.MaxEnrichmentCacheSize)
        {
            EvictOldestEntries(_enrichmentCache, _cacheOptions.MaxEnrichmentCacheSize / 2);
        }

        return result;
    }

    /// <summary>
    /// 캐싱을 활용하여 QA를 생성합니다.
    /// </summary>
    public async Task<IReadOnlyList<GeneratedQAPair>> GenerateQAWithCacheAsync(
        FluxIndexChunk chunk,
        QAGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (_qaService == null)
            throw new InvalidOperationException("QA Generation service is not available.");

        var cacheKey = GenerateCacheKey(chunk, options);

        if (_qaCache.TryGetValue(cacheKey, out var cached) && !cached.IsExpired(_cacheOptions.QAGenerationTTL))
        {
            Interlocked.Increment(ref _qaCacheHits);
            return cached.Result;
        }

        Interlocked.Increment(ref _qaCacheMisses);

        var result = await _qaService.GenerateFromChunkAsync(chunk, options, cancellationToken);

        _qaCache[cacheKey] = new CachedQAGeneration
        {
            Result = result,
            CreatedAt = DateTime.UtcNow
        };

        // 캐시 크기 제한 적용
        if (_qaCache.Count > _cacheOptions.MaxQACacheSize)
        {
            EvictOldestEntries(_qaCache, _cacheOptions.MaxQACacheSize / 2);
        }

        return result;
    }

    /// <summary>
    /// 여러 청크를 캐싱과 함께 병렬 처리합니다.
    /// </summary>
    public async Task<IReadOnlyList<CachedPipelineResult>> ProcessWithCacheAsync(
        IEnumerable<FluxIndexChunk> chunks,
        PipelineOptions? options = null,
        ParallelExecutionOptions? parallelOptions = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new PipelineOptions();
        parallelOptions ??= new ParallelExecutionOptions();
        var chunkList = chunks.ToList();
        var results = new ConcurrentBag<CachedPipelineResult>();

        await Parallel.ForEachAsync(
            chunkList,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = parallelOptions.MaxDegreeOfParallelism,
                CancellationToken = cancellationToken
            },
            async (chunk, ct) =>
            {
                var result = await ProcessSingleWithCacheAsync(chunk, options, ct);
                results.Add(result);
            });

        return results.ToList();
    }

    /// <summary>
    /// 인리치먼트 캐시를 초기화합니다.
    /// </summary>
    public void ClearEnrichmentCache()
    {
        _enrichmentCache.Clear();
    }

    /// <summary>
    /// QA 생성 캐시를 초기화합니다.
    /// </summary>
    public void ClearQACache()
    {
        _qaCache.Clear();
    }

    /// <summary>
    /// 모든 캐시를 초기화합니다.
    /// </summary>
    public void ClearAllCaches()
    {
        ClearEnrichmentCache();
        ClearQACache();
    }

    private async Task<CachedPipelineResult> ProcessSingleWithCacheAsync(
        FluxIndexChunk chunk,
        PipelineOptions options,
        CancellationToken cancellationToken)
    {
        var result = new CachedPipelineResult
        {
            ChunkId = chunk.ChunkId,
            SourceId = chunk.Source.SourceId
        };

        try
        {
            // Step 1: Enrichment with cache
            if (options.EnableEnrichment && _enrichmentService != null)
            {
                var enrichmentCacheKey = GenerateCacheKey(chunk, options.EnrichmentOptions);
                if (_enrichmentCache.TryGetValue(enrichmentCacheKey, out var cached) &&
                    !cached.IsExpired(_cacheOptions.EnrichmentTTL))
                {
                    result.EnrichedChunk = cached.Result;
                    result.EnrichmentFromCache = true;
                    Interlocked.Increment(ref _enrichmentCacheHits);
                }
                else
                {
                    result.EnrichedChunk = await _enrichmentService.EnrichAsync(
                        chunk, options.EnrichmentOptions, cancellationToken);
                    result.EnrichmentFromCache = false;
                    Interlocked.Increment(ref _enrichmentCacheMisses);

                    _enrichmentCache[enrichmentCacheKey] = new CachedEnrichment
                    {
                        Result = result.EnrichedChunk,
                        CreatedAt = DateTime.UtcNow
                    };
                }
                result.EnrichmentCompleted = true;
            }

            // Step 2: QA Generation with cache
            if (options.EnableQAGeneration && _qaService != null)
            {
                var qaCacheKey = GenerateCacheKey(chunk, options.QAGenerationOptions);
                if (_qaCache.TryGetValue(qaCacheKey, out var cached) &&
                    !cached.IsExpired(_cacheOptions.QAGenerationTTL))
                {
                    result.GeneratedQAPairs = cached.Result;
                    result.QAFromCache = true;
                    Interlocked.Increment(ref _qaCacheHits);
                }
                else
                {
                    result.GeneratedQAPairs = await _qaService.GenerateFromChunkAsync(
                        chunk, options.QAGenerationOptions, cancellationToken);
                    result.QAFromCache = false;
                    Interlocked.Increment(ref _qaCacheMisses);

                    _qaCache[qaCacheKey] = new CachedQAGeneration
                    {
                        Result = result.GeneratedQAPairs,
                        CreatedAt = DateTime.UtcNow
                    };
                }
                result.QAGenerationCompleted = true;
            }

            // Step 3: Evaluation (not cached - depends on context)
            if (options.EnableEvaluation && _evaluationService != null && result.GeneratedQAPairs?.Count > 0)
            {
                var evaluations = new List<QAPairWithEvaluation>();
                foreach (var qa in result.GeneratedQAPairs)
                {
                    var evaluation = await _evaluationService.EvaluateAsync(
                        chunk.Content, qa.Question, qa.Answer,
                        options.EvaluationOptions, cancellationToken);

                    evaluations.Add(new QAPairWithEvaluation
                    {
                        Question = qa.Question,
                        Answer = qa.Answer,
                        Context = qa.Context ?? string.Empty,
                        Evaluation = evaluation
                    });
                }
                result.EvaluatedQAPairs = evaluations;
                result.EvaluationCompleted = true;
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private static string GenerateCacheKey(FluxIndexChunk chunk, object? options)
    {
        var keyBuilder = new StringBuilder();
        keyBuilder.Append(chunk.ChunkId);
        keyBuilder.Append('|');
        keyBuilder.Append(chunk.Content.GetHashCode());

        if (options != null)
        {
            keyBuilder.Append('|');
            keyBuilder.Append(options.GetHashCode());
        }

        var bytes = Encoding.UTF8.GetBytes(keyBuilder.ToString());
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }

    private void CleanupExpiredEntries(object? state)
    {
        var enrichmentExpired = _enrichmentCache
            .Where(kvp => kvp.Value.IsExpired(_cacheOptions.EnrichmentTTL))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in enrichmentExpired)
        {
            _enrichmentCache.TryRemove(key, out _);
        }

        var qaExpired = _qaCache
            .Where(kvp => kvp.Value.IsExpired(_cacheOptions.QAGenerationTTL))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in qaExpired)
        {
            _qaCache.TryRemove(key, out _);
        }
    }

    private static void EvictOldestEntries<T>(ConcurrentDictionary<string, T> cache, int targetCount)
        where T : ICachedItem
    {
        var toRemove = cache
            .OrderBy(kvp => kvp.Value.CreatedAt)
            .Take(cache.Count - targetCount)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in toRemove)
        {
            cache.TryRemove(key, out _);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cleanupTimer?.Dispose();
    }
}

/// <summary>
/// 캐시 옵션
/// </summary>
public sealed class CacheOptions
{
    /// <summary>인리치먼트 캐시 TTL (기본값: 1시간)</summary>
    public TimeSpan EnrichmentTTL { get; set; } = TimeSpan.FromHours(1);

    /// <summary>QA 생성 캐시 TTL (기본값: 1시간)</summary>
    public TimeSpan QAGenerationTTL { get; set; } = TimeSpan.FromHours(1);

    /// <summary>최대 인리치먼트 캐시 크기</summary>
    public int MaxEnrichmentCacheSize { get; set; } = 1000;

    /// <summary>최대 QA 캐시 크기</summary>
    public int MaxQACacheSize { get; set; } = 1000;

    /// <summary>자동 정리 활성화</summary>
    public bool EnableAutomaticCleanup { get; set; } = true;

    /// <summary>정리 간격 (기본값: 5분)</summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// 캐시 통계
/// </summary>
public sealed class CacheStatistics
{
    /// <summary>인리치먼트 캐시 항목 수</summary>
    public int EnrichmentCacheCount { get; init; }

    /// <summary>QA 캐시 항목 수</summary>
    public int QACacheCount { get; init; }

    /// <summary>인리치먼트 캐시 히트 수</summary>
    public long EnrichmentCacheHits { get; init; }

    /// <summary>인리치먼트 캐시 미스 수</summary>
    public long EnrichmentCacheMisses { get; init; }

    /// <summary>QA 캐시 히트 수</summary>
    public long QACacheHits { get; init; }

    /// <summary>QA 캐시 미스 수</summary>
    public long QACacheMisses { get; init; }

    /// <summary>인리치먼트 캐시 히트율</summary>
    public double EnrichmentHitRate =>
        EnrichmentCacheHits + EnrichmentCacheMisses > 0
            ? (double)EnrichmentCacheHits / (EnrichmentCacheHits + EnrichmentCacheMisses)
            : 0;

    /// <summary>QA 캐시 히트율</summary>
    public double QAHitRate =>
        QACacheHits + QACacheMisses > 0
            ? (double)QACacheHits / (QACacheHits + QACacheMisses)
            : 0;
}

/// <summary>
/// 캐싱 파이프라인 결과
/// </summary>
public sealed class CachedPipelineResult
{
    /// <summary>청크 ID</summary>
    public required string ChunkId { get; init; }

    /// <summary>소스 문서 ID</summary>
    public required string SourceId { get; init; }

    /// <summary>인리치먼트된 청크</summary>
    public FluxImproverEnrichedChunk? EnrichedChunk { get; set; }

    /// <summary>생성된 QA 쌍</summary>
    public IReadOnlyList<GeneratedQAPair>? GeneratedQAPairs { get; set; }

    /// <summary>평가된 QA 쌍</summary>
    public IReadOnlyList<QAPairWithEvaluation>? EvaluatedQAPairs { get; set; }

    /// <summary>인리치먼트 완료 여부</summary>
    public bool EnrichmentCompleted { get; set; }

    /// <summary>QA 생성 완료 여부</summary>
    public bool QAGenerationCompleted { get; set; }

    /// <summary>평가 완료 여부</summary>
    public bool EvaluationCompleted { get; set; }

    /// <summary>인리치먼트가 캐시에서 제공되었는지</summary>
    public bool EnrichmentFromCache { get; set; }

    /// <summary>QA가 캐시에서 제공되었는지</summary>
    public bool QAFromCache { get; set; }

    /// <summary>성공 여부</summary>
    public bool Success { get; set; }

    /// <summary>오류 메시지</summary>
    public string? ErrorMessage { get; set; }
}

internal interface ICachedItem
{
    DateTime CreatedAt { get; }
}

internal sealed class CachedEnrichment : ICachedItem
{
    public required FluxImproverEnrichedChunk Result { get; init; }
    public required DateTime CreatedAt { get; init; }

    public bool IsExpired(TimeSpan ttl) => DateTime.UtcNow - CreatedAt > ttl;
}

internal sealed class CachedQAGeneration : ICachedItem
{
    public required IReadOnlyList<GeneratedQAPair> Result { get; init; }
    public required DateTime CreatedAt { get; init; }

    public bool IsExpired(TimeSpan ttl) => DateTime.UtcNow - CreatedAt > ttl;
}
