using FluxIndex.Core.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FluxIndex.Storage.SQLite.Cache;

/// <summary>
/// SQLite 기반 시맨틱 캐시 구현
/// </summary>
public partial class SQLiteSemanticCache : ISemanticCache, IDisposable
{
    private readonly SQLiteCacheDbContext _context;
    private readonly IEmbeddingService _embeddingService;
    private readonly SQLiteCacheOptions _options;
    private readonly ILogger<SQLiteSemanticCache> _logger;
    private readonly SemaphoreSlim _cleanupLock = new(1, 1);
    private DateTime _lastCleanup = DateTime.MinValue;

    public SQLiteSemanticCache(
        SQLiteCacheDbContext context,
        IEmbeddingService embeddingService,
        IOptions<SQLiteCacheOptions> options,
        ILogger<SQLiteSemanticCache> logger)
    {
        _context = context;
        _embeddingService = embeddingService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CacheResult?> GetAsync(
        string query,
        float similarityThreshold = 0.85f,
        int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        await CleanupExpiredIfNeededAsync(cancellationToken);

        // 쿼리 임베딩 생성
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);

        // 만료되지 않은 캐시 항목 조회
        var cacheEntries = await _context.SemanticCache
            .Where(c => c.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        if (cacheEntries.Count == 0)
        {
            await UpdateStatsAsync(false, cancellationToken);
            return null;
        }

        // 유사도 계산 및 필터링
        var similarities = cacheEntries
            .Select(entry =>
            {
                var entryEmbedding = entry.GetEmbedding();
                var similarity = CalculateCosineSimilarity(queryEmbedding, entryEmbedding);
                return (Entry: entry, Similarity: similarity);
            })
            .Where(x => x.Similarity >= similarityThreshold)
            .OrderByDescending(x => x.Similarity)
            .Take(maxResults)
            .ToList();

        if (similarities.Count == 0)
        {
            await UpdateStatsAsync(false, cancellationToken);
            return null;
        }

        var bestMatch = similarities.First();

        // 히트 카운트 및 접근 시간 업데이트
        bestMatch.Entry.HitCount++;
        bestMatch.Entry.LastAccessedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        await UpdateStatsAsync(true, cancellationToken);

        LogCacheHit(_logger, bestMatch.Similarity);

        return new CacheResult
        {
            OriginalQuery = bestMatch.Entry.Query,
            SimilarityScore = bestMatch.Similarity,
            Results = bestMatch.Entry.GetResults(),
            CachedAt = bestMatch.Entry.CreatedAt,
            ExpiresAt = bestMatch.Entry.ExpiresAt,
            HitCount = bestMatch.Entry.HitCount,
            LastAccessedAt = bestMatch.Entry.LastAccessedAt,
            Metadata = new CacheMetadata
            {
                Query = bestMatch.Entry.Query,
                ResultCount = bestMatch.Entry.GetResults().Count
            }
        };
    }

    public async Task SetAsync(
        string query,
        IEnumerable<object> results,
        CacheMetadata? metadata = null,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        var queryHash = ComputeHash(query);
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
        var expiresAt = DateTime.UtcNow + (expiry ?? _options.DefaultExpiry);

        // 기존 항목 확인
        var existing = await _context.SemanticCache
            .FirstOrDefaultAsync(c => c.QueryHash == queryHash, cancellationToken);

        if (existing != null)
        {
            existing.SetResults(results);
            existing.SetEmbedding(queryEmbedding);
            existing.ExpiresAt = expiresAt;
            existing.LastAccessedAt = DateTime.UtcNow;
            _context.SemanticCache.Update(existing);
        }
        else
        {
            // 최대 항목 수 체크
            await EnsureCapacityAsync(cancellationToken);

            var entity = new SemanticCacheEntity
            {
                Id = Guid.NewGuid().ToString(),
                QueryHash = queryHash,
                Query = query,
                ExpiresAt = expiresAt
            };
            entity.SetEmbedding(queryEmbedding);
            entity.SetResults(results);
            if (metadata != null)
            {
                entity.MetadataJson = JsonSerializer.Serialize(metadata);
            }

            await _context.SemanticCache.AddAsync(entity, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
        LogCacheSet(_logger, queryHash);
    }

    public async Task<bool> HasSimilarQueryAsync(
        string query,
        float threshold = 0.85f,
        CancellationToken cancellationToken = default)
    {
        var result = await GetAsync(query, threshold, 1, cancellationToken);
        return result != null;
    }

    public async Task<IEnumerable<SimilarQuery>> FindSimilarQueriesAsync(
        string query,
        float threshold = 0.85f,
        int maxSimilar = 5,
        CancellationToken cancellationToken = default)
    {
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);

        var cacheEntries = await _context.SemanticCache
            .Where(c => c.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        return cacheEntries
            .Select(entry =>
            {
                var entryEmbedding = entry.GetEmbedding();
                var similarity = CalculateCosineSimilarity(queryEmbedding, entryEmbedding);
                return new SimilarQuery
                {
                    Query = entry.Query,
                    SimilarityScore = similarity,
                    CachedAt = entry.CreatedAt,
                    ResultCount = entry.GetResults().Count,
                    CacheKey = entry.QueryHash
                };
            })
            .Where(x => x.SimilarityScore >= threshold)
            .OrderByDescending(x => x.SimilarityScore)
            .Take(maxSimilar);
    }

    public async Task<int> InvalidateAsync(
        string pattern,
        CancellationToken cancellationToken = default)
    {
        var toDelete = await _context.SemanticCache
            .Where(c => EF.Functions.Like(c.Query, $"%{pattern}%"))
            .ToListAsync(cancellationToken);

        _context.SemanticCache.RemoveRange(toDelete);
        await _context.SaveChangesAsync(cancellationToken);

        LogCacheInvalidated(_logger, toDelete.Count, pattern);

        return toDelete.Count;
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _context.Database.ExecuteSqlRawAsync(
            "DELETE FROM semantic_cache", cancellationToken);

        LogCacheCleared(_logger);
    }

    public async Task<CacheStatistics> GetStatisticsAsync(
        CancellationToken cancellationToken = default)
    {
        var stats = await _context.CacheStats
            .FirstOrDefaultAsync(s => s.Id == 1, cancellationToken);

        var entryCount = await _context.SemanticCache.CountAsync(cancellationToken);
        var expiredCount = await _context.SemanticCache
            .CountAsync(c => c.ExpiresAt <= DateTime.UtcNow, cancellationToken);

        return new CacheStatistics
        {
            TotalQueries = entryCount,
            CacheHits = stats?.TotalHits ?? 0,
            CacheMisses = stats?.TotalMisses ?? 0,
            ExpiredEntries = expiredCount,
            CollectedAt = DateTime.UtcNow
        };
    }

    public async Task<CacheOptimizationResult> OptimizeAsync(
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var messages = new List<string>();

        // 만료된 항목 삭제
        var expiredCount = await _context.Database.ExecuteSqlRawAsync(
            "DELETE FROM semantic_cache WHERE ExpiresAt < {0}",
            DateTime.UtcNow);

        messages.Add($"Removed {expiredCount} expired entries");

        // LRU 기반 정리 (최대 항목 수 초과 시)
        var currentCount = await _context.SemanticCache.CountAsync(cancellationToken);
        var lruRemoved = 0;

        if (currentCount > _options.MaxEntries)
        {
            var excessCount = currentCount - _options.MaxEntries;
            var toRemove = await _context.SemanticCache
                .OrderBy(c => c.LastAccessedAt)
                .Take(excessCount)
                .ToListAsync(cancellationToken);

            _context.SemanticCache.RemoveRange(toRemove);
            await _context.SaveChangesAsync(cancellationToken);
            lruRemoved = toRemove.Count;
            messages.Add($"LRU evicted {lruRemoved} entries");
        }

        // VACUUM으로 공간 회수 (파일 기반 DB인 경우)
        if (!_options.UseInMemory)
        {
            await _context.Database.ExecuteSqlRawAsync("VACUUM", cancellationToken);
            messages.Add("VACUUM completed");
        }

        var duration = DateTime.UtcNow - startTime;

        LogCacheOptimized(_logger, expiredCount, lruRemoved, duration.TotalMilliseconds);

        return new CacheOptimizationResult
        {
            RemovedEntries = expiredCount + lruRemoved,
            OptimizationTimeMs = duration.TotalMilliseconds,
            Success = true,
            Messages = messages
        };
    }

    /// <summary>
    /// Disposes the cleanup lock semaphore.
    /// </summary>
    public void Dispose()
    {
        _cleanupLock.Dispose();
        GC.SuppressFinalize(this);
    }

    #region Private Methods

    private static float CalculateCosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0f;

        float dotProduct = 0f, normA = 0f, normB = 0f;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denominator > 0 ? dotProduct / denominator : 0f;
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private async Task UpdateStatsAsync(bool isHit, CancellationToken cancellationToken)
    {
        var stats = await _context.CacheStats
            .FirstOrDefaultAsync(s => s.Id == 1, cancellationToken);

        if (stats == null)
        {
            stats = new CacheStatsEntity { Id = 1 };
            await _context.CacheStats.AddAsync(stats, cancellationToken);
        }

        if (isHit)
            stats.TotalHits++;
        else
            stats.TotalMisses++;

        stats.LastUpdated = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureCapacityAsync(CancellationToken cancellationToken)
    {
        var currentCount = await _context.SemanticCache.CountAsync(cancellationToken);

        if (currentCount >= _options.MaxEntries)
        {
            // LRU 방식으로 10% 삭제
            var removeCount = Math.Max(1, _options.MaxEntries / 10);
            var toRemove = await _context.SemanticCache
                .OrderBy(c => c.LastAccessedAt)
                .Take(removeCount)
                .ToListAsync(cancellationToken);

            _context.SemanticCache.RemoveRange(toRemove);

            // 통계 업데이트
            var stats = await _context.CacheStats
                .FirstOrDefaultAsync(s => s.Id == 1, cancellationToken);
            if (stats != null)
            {
                stats.TotalEvictions += removeCount;
            }

            await _context.SaveChangesAsync(cancellationToken);

            LogCacheEvicted(_logger, removeCount);
        }
    }

    private async Task CleanupExpiredIfNeededAsync(CancellationToken cancellationToken)
    {
        if (!_options.EnableAutoCleanup)
            return;

        if (DateTime.UtcNow - _lastCleanup < _options.CleanupInterval)
            return;

        if (!await _cleanupLock.WaitAsync(0, cancellationToken))
            return;

        try
        {
            var deleted = await _context.Database.ExecuteSqlRawAsync(
                "DELETE FROM semantic_cache WHERE ExpiresAt < {0}",
                DateTime.UtcNow);

            _lastCleanup = DateTime.UtcNow;

            if (deleted > 0)
            {
                LogAutoCleanup(_logger, deleted);
            }
        }
        finally
        {
            _cleanupLock.Release();
        }
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache hit for query: similarity={Similarity:F3}")]
    private static partial void LogCacheHit(ILogger logger, float similarity);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache set for query hash: {Hash}")]
    private static partial void LogCacheSet(ILogger logger, string hash);

    [LoggerMessage(Level = LogLevel.Information, Message = "Invalidated {Count} cache entries matching pattern: {Pattern}")]
    private static partial void LogCacheInvalidated(ILogger logger, int count, string pattern);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cache cleared")]
    private static partial void LogCacheCleared(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cache optimization completed: {ExpiredRemoved} expired, {LruRemoved} LRU evicted in {DurationMs}ms")]
    private static partial void LogCacheOptimized(ILogger logger, int expiredRemoved, int lruRemoved, double durationMs);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Evicted {Count} entries due to capacity limit")]
    private static partial void LogCacheEvicted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Auto-cleanup removed {Count} expired entries")]
    private static partial void LogAutoCleanup(ILogger logger, int count);

    #endregion
}
