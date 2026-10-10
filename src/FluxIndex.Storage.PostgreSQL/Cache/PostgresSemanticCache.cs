using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Application.Utilities;
using FluxIndex.Core.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace FluxIndex.Storage.PostgreSQL.Cache;

/// <summary>
/// PostgreSQL 기반 시맨틱 캐시 구현 (pgvector + UNLOGGED 테이블) — the <see cref="ISemanticCacheService"/> registered
/// for <c>SemanticCacheOptions.Provider = "PostgreSQL"</c> (opt-in), so <c>FluxIndexContext.SearchAsync</c> reads and
/// fills it. Similarity is pgvector's cosine distance.
/// </summary>
public partial class PostgresSemanticCache : ISemanticCacheService, IDisposable
{
    // How many entries above the threshold a lookup reads, so an unreadable best match falls through to the next one.
    private const int LookupCandidates = 5;

    private readonly IDbContextFactory<PostgresCacheDbContext> _contextFactory;
    private readonly IEmbeddingService _embeddingService;
    private readonly PostgresCacheOptions _options;
    private readonly ILogger<PostgresSemanticCache> _logger;
    private readonly SemaphoreSlim _cleanupLock = new(1, 1);
    private DateTime _lastCleanup = DateTime.MinValue;

    public PostgresSemanticCache(
        IDbContextFactory<PostgresCacheDbContext> contextFactory,
        IEmbeddingService embeddingService,
        IOptions<PostgresCacheOptions> options,
        ILogger<PostgresSemanticCache> logger)
    {
        _contextFactory = contextFactory;
        _embeddingService = embeddingService;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Null <paramref name="similarityThreshold"/> uses <see cref="PostgresCacheOptions.SimilarityThreshold"/>. An entry
    /// whose stored results cannot be read (written by an earlier release, or by something else) is removed and the
    /// next most similar entry is tried.
    /// </remarks>
    public async Task<CachedSearchResult?> GetCachedResultAsync(
        string query,
        float? similarityThreshold = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (NoEmbeddingService.IsKeywordOnly(_embeddingService))
            return null;

        var threshold = similarityThreshold ?? _options.SimilarityThreshold;

        try
        {
            await CleanupExpiredIfNeededAsync(cancellationToken);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var queryEmbedding = await _embeddingService.GenerateQueryEmbeddingAsync(query, cancellationToken);
            var queryVector = new Vector(queryEmbedding);

            // pgvector 코사인 유사도 검색 (1 - distance = similarity)
            var candidates = await context.SemanticCache
                .Where(c => c.ExpiresAt > DateTime.UtcNow)
                .Where(c => c.Embedding != null)
                .Select(c => new
                {
                    Entry = c,
                    Distance = c.Embedding!.CosineDistance(queryVector)
                })
                .Where(x => (1 - x.Distance) >= threshold)
                .OrderBy(x => x.Distance)
                .Take(LookupCandidates)
                .ToListAsync(cancellationToken);

            foreach (var candidate in candidates)
            {
                var entry = candidate.Entry;
                if (!SemanticCacheJson.TryDeserializeResults(entry.ResultsJson, out var results) || results.Count == 0)
                {
                    await RemoveUnreadableAsync(context, entry, cancellationToken);
                    continue;
                }

                var similarity = (float)(1 - candidate.Distance);
                var accessedAt = DateTime.UtcNow;

                // An increment in SQL: overlapping hits on one entry each count, and nothing else in the row is rewritten.
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $@"UPDATE semantic_cache SET ""HitCount"" = ""HitCount"" + 1, ""LastAccessedAt"" = {accessedAt} WHERE ""Id"" = {entry.Id}",
                    cancellationToken);
                await UpdateStatsAsync(context, true, cancellationToken);

                LogCacheHit(_logger, similarity);

                return new CachedSearchResult
                {
                    OriginalQuery = query,
                    CachedQuery = entry.Query,
                    SimilarityScore = similarity,
                    Results = results,
                    Metadata = SemanticCacheJson.DeserializeMetadata(entry.MetadataJson),
                    CachedAt = entry.CreatedAt,
                    HitCount = entry.HitCount + 1,
                    LastAccessedAt = accessedAt
                };
            }

            await UpdateStatsAsync(context, false, cancellationToken);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogLookupFailed(_logger, ex);
            return null;
        }
    }

    /// <inheritdoc />
    /// <remarks>Null <paramref name="ttl"/> uses <see cref="PostgresCacheOptions.DefaultExpiry"/>.</remarks>
    public async Task SetCachedResultAsync(
        string query,
        IReadOnlyList<CacheDocumentChunk> results,
        SearchMetadata? metadata = null,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(results);
        if (NoEmbeddingService.IsKeywordOnly(_embeddingService))
            return;

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var queryHash = ComputeHash(query);
        var queryEmbedding = await _embeddingService.GenerateQueryEmbeddingAsync(query, cancellationToken);
        var expiresAt = DateTime.UtcNow + (ttl ?? _options.DefaultExpiry);
        var resultsJson = SemanticCacheJson.SerializeResults(results);
        var metadataJson = SemanticCacheJson.SerializeMetadata(metadata);

        var existing = await context.SemanticCache
            .FirstOrDefaultAsync(c => c.QueryHash == queryHash, cancellationToken);

        if (existing != null)
        {
            existing.ResultsJson = resultsJson;
            existing.MetadataJson = metadataJson;
            existing.Embedding = new Vector(queryEmbedding);
            existing.ExpiresAt = expiresAt;
            existing.LastAccessedAt = DateTime.UtcNow;
            context.SemanticCache.Update(existing);
        }
        else
        {
            await EnsureCapacityAsync(context, cancellationToken);

            var entity = new SemanticCacheEntity
            {
                Id = Guid.NewGuid().ToString(),
                QueryHash = queryHash,
                Query = query,
                Embedding = new Vector(queryEmbedding),
                ResultsJson = resultsJson,
                MetadataJson = metadataJson,
                ExpiresAt = expiresAt
            };

            await context.SemanticCache.AddAsync(entity, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
        LogCacheSet(_logger, queryHash);
    }

    /// <inheritdoc />
    /// <remarks>Removes the entries whose query contains <paramref name="pattern"/> (case-insensitive); a blank pattern
    /// removes nothing.</remarks>
    public async Task InvalidateCacheAsync(string pattern, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return;

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var toDelete = await context.SemanticCache
            .Where(c => EF.Functions.ILike(c.Query, $"%{pattern}%"))
            .ToListAsync(cancellationToken);

        context.SemanticCache.RemoveRange(toDelete);
        await context.SaveChangesAsync(cancellationToken);

        LogCacheInvalidated(_logger, toDelete.Count);
    }

    /// <inheritdoc />
    public async Task ClearCacheAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        // TRUNCATE는 UNLOGGED 테이블에서 더 효율적
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE semantic_cache", cancellationToken);

        LogCacheCleared(_logger);
    }

    /// <inheritdoc />
    public async Task<SemanticCacheStatistics> GetCacheStatisticsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var stats = await context.CacheStats
            .FirstOrDefaultAsync(s => s.Id == 1, cancellationToken);

        var entryCount = await context.SemanticCache.CountAsync(cancellationToken);
        var expiredCount = await context.SemanticCache
            .CountAsync(c => c.ExpiresAt <= DateTime.UtcNow, cancellationToken);

        return new SemanticCacheStatistics
        {
            TotalEntries = entryCount,
            CacheHits = stats?.TotalHits ?? 0,
            CacheMisses = stats?.TotalMisses ?? 0,
            ExpiredEntries = expiredCount,
            CollectedAt = DateTime.UtcNow
        };
    }

    /// <inheritdoc />
    /// <remarks>Stores nothing: an entry without results could never be a hit, and the embedding is computed when a
    /// search stores its results.</remarks>
    public Task WarmupCacheAsync(IReadOnlyList<string> popularQueries, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>Removes expired entries, evicts the least recently used beyond <see cref="PostgresCacheOptions.MaxEntries"/>,
    /// and runs VACUUM ANALYZE (skipped with a warning when the server refuses it).</remarks>
    public async Task CompactCacheAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var stopwatch = Stopwatch.StartNew();

        var expiredCount = await context.Database.ExecuteSqlInterpolatedAsync(
            $@"DELETE FROM semantic_cache WHERE ""ExpiresAt"" < {DateTime.UtcNow}", cancellationToken);

        var currentCount = await context.SemanticCache.CountAsync(cancellationToken);
        var lruRemoved = 0;

        if (currentCount > _options.MaxEntries)
        {
            var excessCount = currentCount - _options.MaxEntries;

            // PostgreSQL에서 효율적인 LRU 삭제
            lruRemoved = await context.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM semantic_cache
                WHERE ""Id"" IN (
                    SELECT ""Id"" FROM semantic_cache
                    ORDER BY ""LastAccessedAt"" ASC
                    LIMIT {excessCount}
                )", cancellationToken);
        }

        // VACUUM ANALYZE로 통계 업데이트 (UNLOGGED 테이블이라 빠름)
        try
        {
            await context.Database.ExecuteSqlRawAsync("VACUUM ANALYZE semantic_cache", cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogVacuumAnalyzeFailed(_logger, ex);
        }

        LogCacheOptimizationCompleted(_logger, expiredCount, lruRemoved, stopwatch.Elapsed.TotalMilliseconds);
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

    private async Task RemoveUnreadableAsync(PostgresCacheDbContext context, SemanticCacheEntity entry, CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $@"DELETE FROM semantic_cache WHERE ""Id"" = {entry.Id}", cancellationToken);
        LogUnreadableEntryRemoved(_logger, entry.QueryHash);
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Counts a hit or a miss in one atomic upsert, so overlapping callers each count and two first
    /// callers cannot both insert the statistics row.
    /// </summary>
    private static async Task UpdateStatsAsync(PostgresCacheDbContext context, bool isHit, CancellationToken cancellationToken)
    {
        var hits = isHit ? 1L : 0L;
        var misses = isHit ? 0L : 1L;
        var now = DateTime.UtcNow;
        await context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO cache_stats (""Id"", ""TotalHits"", ""TotalMisses"", ""TotalEvictions"", ""TotalEntries"", ""LastUpdated"")
            VALUES (1, {hits}, {misses}, 0, 0, {now})
            ON CONFLICT (""Id"") DO UPDATE SET
                ""TotalHits"" = cache_stats.""TotalHits"" + {hits},
                ""TotalMisses"" = cache_stats.""TotalMisses"" + {misses},
                ""LastUpdated"" = {now}", cancellationToken);
    }

    private async Task EnsureCapacityAsync(PostgresCacheDbContext context, CancellationToken cancellationToken)
    {
        var currentCount = await context.SemanticCache.CountAsync(cancellationToken);

        if (currentCount >= _options.MaxEntries)
        {
            // LRU 방식으로 10% 삭제
            var removeCount = Math.Max(1, _options.MaxEntries / 10);

            await context.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM semantic_cache
                WHERE ""Id"" IN (
                    SELECT ""Id"" FROM semantic_cache
                    ORDER BY ""LastAccessedAt"" ASC
                    LIMIT {removeCount}
                )", cancellationToken);

            // 통계 업데이트 — an increment in SQL: the context is NoTracking, so an edit to a queried
            // statistics row would never be written.
            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"UPDATE cache_stats SET ""TotalEvictions"" = ""TotalEvictions"" + {removeCount} WHERE ""Id"" = 1",
                cancellationToken);

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
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var deleted = await context.Database.ExecuteSqlInterpolatedAsync(
                $@"DELETE FROM semantic_cache WHERE ""ExpiresAt"" < {DateTime.UtcNow}", cancellationToken);

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Semantic cache lookup failed; answering as a miss")]
    private static partial void LogLookupFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Removed a semantic cache entry whose results could not be read: {Hash}")]
    private static partial void LogUnreadableEntryRemoved(ILogger logger, string hash);

    [LoggerMessage(Level = LogLevel.Information, Message = "Invalidated {Count} semantic cache entries")]
    private static partial void LogCacheInvalidated(ILogger logger, int count);

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
