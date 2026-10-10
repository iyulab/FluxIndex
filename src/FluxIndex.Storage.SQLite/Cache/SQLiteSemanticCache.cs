using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Application.Utilities;
using FluxIndex.Core.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace FluxIndex.Storage.SQLite.Cache;

/// <summary>
/// SQLite 기반 시맨틱 캐시 구현 — the <see cref="ISemanticCacheService"/> registered for
/// <c>SemanticCacheOptions.Provider = "SQLite"</c> (opt-in), so
/// <c>FluxIndexContext.SearchAsync</c> reads and fills it. Similarity is computed in process over the entries that have
/// not expired (at most <see cref="SQLiteCacheOptions.MaxEntries"/>).
/// </summary>
public partial class SQLiteSemanticCache : ISemanticCacheService, IDisposable
{
    private readonly IDbContextFactory<SQLiteCacheDbContext> _contextFactory;
    private readonly IEmbeddingService _embeddingService;
    private readonly SQLiteCacheOptions _options;
    private readonly ILogger<SQLiteSemanticCache> _logger;
    private readonly SemaphoreSlim _cleanupLock = new(1, 1);
    private DateTime _lastCleanup = DateTime.MinValue;

    public SQLiteSemanticCache(
        IDbContextFactory<SQLiteCacheDbContext> contextFactory,
        IEmbeddingService embeddingService,
        IOptions<SQLiteCacheOptions> options,
        ILogger<SQLiteSemanticCache> logger)
    {
        _contextFactory = contextFactory;
        _embeddingService = embeddingService;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Null <paramref name="similarityThreshold"/> uses <see cref="SQLiteCacheOptions.SimilarityThreshold"/>. An entry
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

            var candidates = (await context.SemanticCache
                    .Where(c => c.ExpiresAt > DateTime.UtcNow)
                    .ToListAsync(cancellationToken))
                .Select(entry => (Entry: entry, Similarity: CalculateCosineSimilarity(queryEmbedding, entry.GetEmbedding())))
                .Where(x => x.Similarity >= threshold)
                .OrderByDescending(x => x.Similarity);

            foreach (var (entry, similarity) in candidates)
            {
                if (!SemanticCacheJson.TryDeserializeResults(entry.ResultsJson, out var results) || results.Count == 0)
                {
                    await RemoveUnreadableAsync(context, entry, cancellationToken);
                    continue;
                }

                // Attached first: the context is NoTracking, and a change to a detached instance is never written.
                context.SemanticCache.Attach(entry);
                entry.HitCount++;
                entry.LastAccessedAt = DateTime.UtcNow;
                await context.SaveChangesAsync(cancellationToken);
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
                    HitCount = entry.HitCount,
                    LastAccessedAt = entry.LastAccessedAt
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
    /// <remarks>Null <paramref name="ttl"/> uses <see cref="SQLiteCacheOptions.DefaultExpiry"/>.</remarks>
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
        var metadataJson = SemanticCacheJson.SerializeMetadata(metadata) ?? "{}";

        var existing = await context.SemanticCache
            .FirstOrDefaultAsync(c => c.QueryHash == queryHash, cancellationToken);

        if (existing != null)
        {
            existing.ResultsJson = resultsJson;
            existing.MetadataJson = metadataJson;
            existing.SetEmbedding(queryEmbedding);
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
                ResultsJson = resultsJson,
                MetadataJson = metadataJson,
                ExpiresAt = expiresAt
            };
            entity.SetEmbedding(queryEmbedding);

            await context.SemanticCache.AddAsync(entity, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
        LogCacheSet(_logger, queryHash);
    }

    /// <inheritdoc />
    /// <remarks>Removes the entries whose query contains <paramref name="pattern"/>; a blank pattern removes nothing.</remarks>
    public async Task InvalidateCacheAsync(string pattern, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return;

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var toDelete = await context.SemanticCache
            .Where(c => EF.Functions.Like(c.Query, $"%{pattern}%"))
            .ToListAsync(cancellationToken);

        context.SemanticCache.RemoveRange(toDelete);
        await context.SaveChangesAsync(cancellationToken);

        LogCacheInvalidated(_logger, toDelete.Count);
    }

    /// <inheritdoc />
    public async Task ClearCacheAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync("DELETE FROM semantic_cache", cancellationToken);

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
    /// <remarks>Removes expired entries, evicts the least recently used beyond <see cref="SQLiteCacheOptions.MaxEntries"/>,
    /// and runs VACUUM on a file database.</remarks>
    public async Task CompactCacheAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var stopwatch = Stopwatch.StartNew();

        var expiredCount = await context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM semantic_cache WHERE ExpiresAt < {DateTime.UtcNow}", cancellationToken);

        var currentCount = await context.SemanticCache.CountAsync(cancellationToken);
        var lruRemoved = 0;

        if (currentCount > _options.MaxEntries)
        {
            var toRemove = await context.SemanticCache
                .OrderBy(c => c.LastAccessedAt)
                .Take(currentCount - _options.MaxEntries)
                .ToListAsync(cancellationToken);

            context.SemanticCache.RemoveRange(toRemove);
            await context.SaveChangesAsync(cancellationToken);
            lruRemoved = toRemove.Count;
        }

        // VACUUM으로 공간 회수 (파일 기반 DB인 경우)
        if (!_options.UseInMemory)
        {
            await context.Database.ExecuteSqlRawAsync("VACUUM", cancellationToken);
        }

        LogCacheOptimized(_logger, expiredCount, lruRemoved, stopwatch.Elapsed.TotalMilliseconds);
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

    private async Task RemoveUnreadableAsync(SQLiteCacheDbContext context, SemanticCacheEntity entry, CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $@"DELETE FROM semantic_cache WHERE ""Id"" = {entry.Id}", cancellationToken);
        LogUnreadableEntryRemoved(_logger, entry.QueryHash);
    }

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

    /// <summary>
    /// Counts a hit or a miss in one atomic upsert, so overlapping callers each count and two first
    /// callers cannot both insert the statistics row.
    /// </summary>
    private static async Task UpdateStatsAsync(SQLiteCacheDbContext context, bool isHit, CancellationToken cancellationToken)
    {
        var hits = isHit ? 1 : 0;
        var misses = isHit ? 0 : 1;
        var now = DateTime.UtcNow;
        await context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO cache_stats (""Id"", ""TotalHits"", ""TotalMisses"", ""TotalEvictions"", ""TotalEntries"", ""LastUpdated"")
            VALUES (1, {hits}, {misses}, 0, 0, {now})
            ON CONFLICT(""Id"") DO UPDATE SET
                ""TotalHits"" = ""TotalHits"" + {hits},
                ""TotalMisses"" = ""TotalMisses"" + {misses},
                ""LastUpdated"" = {now}", cancellationToken);
    }

    private async Task EnsureCapacityAsync(SQLiteCacheDbContext context, CancellationToken cancellationToken)
    {
        var currentCount = await context.SemanticCache.CountAsync(cancellationToken);

        if (currentCount >= _options.MaxEntries)
        {
            // LRU 방식으로 10% 삭제
            var removeCount = Math.Max(1, _options.MaxEntries / 10);
            var toRemove = await context.SemanticCache
                .OrderBy(c => c.LastAccessedAt)
                .Take(removeCount)
                .ToListAsync(cancellationToken);

            context.SemanticCache.RemoveRange(toRemove);
            await context.SaveChangesAsync(cancellationToken);

            // 통계 업데이트 — an increment in SQL: the context is NoTracking, so an edit to a queried
            // statistics row would never be written.
            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"UPDATE cache_stats SET ""TotalEvictions"" = ""TotalEvictions"" + {removeCount} WHERE ""Id"" = 1",
                cancellationToken);

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
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var deleted = await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM semantic_cache WHERE ExpiresAt < {DateTime.UtcNow}", cancellationToken);

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Cache optimization completed: {ExpiredRemoved} expired, {LruRemoved} LRU evicted in {DurationMs}ms")]
    private static partial void LogCacheOptimized(ILogger logger, int expiredRemoved, int lruRemoved, double durationMs);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Evicted {Count} entries due to capacity limit")]
    private static partial void LogCacheEvicted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Auto-cleanup removed {Count} expired entries")]
    private static partial void LogAutoCleanup(ILogger logger, int count);

    #endregion
}
