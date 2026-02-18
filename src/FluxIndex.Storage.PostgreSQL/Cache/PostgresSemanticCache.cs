using FluxIndex.Core.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace FluxIndex.Storage.PostgreSQL.Cache;

/// <summary>
/// PostgreSQL 기반 시맨틱 캐시 구현 (pgvector + UNLOGGED 테이블)
/// </summary>
public partial class PostgresSemanticCache : ISemanticCache, IDisposable
{
    private readonly PostgresCacheDbContext _context;
    private readonly IEmbeddingService _embeddingService;
    private readonly PostgresCacheOptions _options;
    private readonly ILogger<PostgresSemanticCache> _logger;
    private readonly SemaphoreSlim _cleanupLock = new(1, 1);
    private DateTime _lastCleanup = DateTime.MinValue;

    public PostgresSemanticCache(
        PostgresCacheDbContext context,
        IEmbeddingService embeddingService,
        IOptions<PostgresCacheOptions> options,
        ILogger<PostgresSemanticCache> logger)
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
        var queryVector = new Vector(queryEmbedding);

        // pgvector 코사인 유사도 검색 (1 - distance = similarity)
        var result = await _context.SemanticCache
            .Where(c => c.ExpiresAt > DateTime.UtcNow)
            .Where(c => c.Embedding != null)
            .Select(c => new
            {
                Entry = c,
                Distance = c.Embedding!.CosineDistance(queryVector)
            })
            .Where(x => (1 - x.Distance) >= similarityThreshold)
            .OrderBy(x => x.Distance)
            .Take(maxResults)
            .FirstOrDefaultAsync(cancellationToken);

        if (result == null)
        {
            await UpdateStatsAsync(false, cancellationToken);
            return null;
        }

        var similarity = (float)(1 - result.Distance);

        // 히트 카운트 및 접근 시간 업데이트
        result.Entry.HitCount++;
        result.Entry.LastAccessedAt = DateTime.UtcNow;
        _context.SemanticCache.Update(result.Entry);
        await _context.SaveChangesAsync(cancellationToken);
        await UpdateStatsAsync(true, cancellationToken);

        LogCacheHit(_logger, similarity);

        return new CacheResult
        {
            OriginalQuery = result.Entry.Query,
            SimilarityScore = similarity,
            Results = result.Entry.Results?.Cast<object>() ?? Enumerable.Empty<object>(),
            CachedAt = result.Entry.CreatedAt,
            ExpiresAt = result.Entry.ExpiresAt,
            HitCount = result.Entry.HitCount,
            LastAccessedAt = result.Entry.LastAccessedAt,
            Metadata = new CacheMetadata
            {
                Query = result.Entry.Query,
                ResultCount = result.Entry.Results?.Count ?? 0
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
            existing.Results = results.ToList();
            existing.Embedding = new Vector(queryEmbedding);
            existing.ExpiresAt = expiresAt;
            existing.LastAccessedAt = DateTime.UtcNow;
            if (metadata != null)
            {
                existing.Metadata = new Dictionary<string, object>
                {
                    ["Query"] = metadata.Query ?? query,
                    ["ResultCount"] = metadata.ResultCount
                };
            }
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
                Embedding = new Vector(queryEmbedding),
                Results = results.ToList(),
                ExpiresAt = expiresAt
            };

            if (metadata != null)
            {
                entity.Metadata = new Dictionary<string, object>
                {
                    ["Query"] = metadata.Query ?? query,
                    ["ResultCount"] = metadata.ResultCount
                };
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
        var queryVector = new Vector(queryEmbedding);

        var results = await _context.SemanticCache
            .Where(c => c.ExpiresAt > DateTime.UtcNow)
            .Where(c => c.Embedding != null)
            .Select(c => new
            {
                Entry = c,
                Distance = c.Embedding!.CosineDistance(queryVector)
            })
            .Where(x => (1 - x.Distance) >= threshold)
            .OrderBy(x => x.Distance)
            .Take(maxSimilar)
            .ToListAsync(cancellationToken);

        return results.Select(r => new SimilarQuery
        {
            Query = r.Entry.Query,
            SimilarityScore = (float)(1 - r.Distance),
            CachedAt = r.Entry.CreatedAt,
            ResultCount = r.Entry.Results?.Count ?? 0,
            CacheKey = r.Entry.QueryHash
        });
    }

    public async Task<int> InvalidateAsync(
        string pattern,
        CancellationToken cancellationToken = default)
    {
        var toDelete = await _context.SemanticCache
            .Where(c => EF.Functions.ILike(c.Query, $"%{pattern}%"))
            .ToListAsync(cancellationToken);

        _context.SemanticCache.RemoveRange(toDelete);
        await _context.SaveChangesAsync(cancellationToken);

        LogCacheInvalidated(_logger, toDelete.Count, pattern);

        return toDelete.Count;
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        // TRUNCATE는 UNLOGGED 테이블에서 더 효율적
        await _context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE semantic_cache", cancellationToken);

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
            "DELETE FROM semantic_cache WHERE \"ExpiresAt\" < {0}",
            DateTime.UtcNow);

        messages.Add($"Removed {expiredCount} expired entries");

        // LRU 기반 정리 (최대 항목 수 초과 시)
        var currentCount = await _context.SemanticCache.CountAsync(cancellationToken);
        var lruRemoved = 0;

        if (currentCount > _options.MaxEntries)
        {
            var excessCount = currentCount - _options.MaxEntries;

            // PostgreSQL에서 효율적인 LRU 삭제
            lruRemoved = await _context.Database.ExecuteSqlRawAsync($@"
                DELETE FROM semantic_cache
                WHERE ""Id"" IN (
                    SELECT ""Id"" FROM semantic_cache
                    ORDER BY ""LastAccessedAt"" ASC
                    LIMIT {{0}}
                )", excessCount);

            messages.Add($"LRU evicted {lruRemoved} entries");
        }

        // VACUUM ANALYZE로 통계 업데이트 (UNLOGGED 테이블이라 빠름)
        try
        {
            await _context.Database.ExecuteSqlRawAsync("VACUUM ANALYZE semantic_cache", cancellationToken);
            messages.Add("VACUUM ANALYZE completed");
        }
        catch (Exception ex)
        {
            LogVacuumAnalyzeFailed(_logger, ex);
            messages.Add("VACUUM ANALYZE skipped");
        }

        var duration = DateTime.UtcNow - startTime;

        LogCacheOptimizationCompleted(_logger, expiredCount, lruRemoved, duration.TotalMilliseconds);

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

            await _context.Database.ExecuteSqlRawAsync($@"
                DELETE FROM semantic_cache
                WHERE ""Id"" IN (
                    SELECT ""Id"" FROM semantic_cache
                    ORDER BY ""LastAccessedAt"" ASC
                    LIMIT {{0}}
                )", removeCount);

            // 통계 업데이트
            var stats = await _context.CacheStats
                .FirstOrDefaultAsync(s => s.Id == 1, cancellationToken);
            if (stats != null)
            {
                stats.TotalEvictions += removeCount;
                await _context.SaveChangesAsync(cancellationToken);
            }

            LogEvictedEntries(_logger, removeCount);
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
                "DELETE FROM semantic_cache WHERE \"ExpiresAt\" < {0}",
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "VACUUM ANALYZE failed, continuing without it")]
    private static partial void LogVacuumAnalyzeFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cache optimization completed: {ExpiredRemoved} expired, {LruRemoved} LRU evicted in {Duration}ms")]
    private static partial void LogCacheOptimizationCompleted(ILogger logger, int expiredRemoved, int lruRemoved, double duration);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Evicted {Count} entries due to capacity limit")]
    private static partial void LogEvictedEntries(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Auto-cleanup removed {Count} expired entries")]
    private static partial void LogAutoCleanup(ILogger logger, int count);

    #endregion
}
