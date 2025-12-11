using FluxIndex.Core.Domain.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Cache interface for storing and retrieving evaluation results.
/// Implement this interface to persist evaluation results across sessions.
/// </summary>
public interface IEvaluationResultCache
{
    /// <summary>
    /// Gets a cached evaluation result for the specified version and dataset.
    /// </summary>
    /// <param name="version">System version identifier</param>
    /// <param name="datasetId">Golden dataset identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Cached result if available; null otherwise</returns>
    Task<BatchEvaluationResult?> GetAsync(
        string version,
        string datasetId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores an evaluation result in the cache.
    /// </summary>
    /// <param name="version">System version identifier</param>
    /// <param name="datasetId">Golden dataset identifier</param>
    /// <param name="result">Evaluation result to cache</param>
    /// <param name="expiration">Optional expiration time for the cache entry</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SetAsync(
        string version,
        string datasetId,
        BatchEvaluationResult result,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a cached evaluation result.
    /// </summary>
    /// <param name="version">System version identifier</param>
    /// <param name="datasetId">Golden dataset identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RemoveAsync(
        string version,
        string datasetId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a cached result exists.
    /// </summary>
    /// <param name="version">System version identifier</param>
    /// <param name="datasetId">Golden dataset identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if cached result exists; false otherwise</returns>
    Task<bool> ExistsAsync(
        string version,
        string datasetId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// In-memory implementation of IEvaluationResultCache.
/// Suitable for single-instance scenarios and testing.
/// </summary>
public class InMemoryEvaluationResultCache : IEvaluationResultCache
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CacheEntry> _cache = new();

    private record CacheEntry(BatchEvaluationResult Result, DateTime? ExpiresAt);

    private static string GetKey(string version, string datasetId) => $"{version}:{datasetId}";

    /// <inheritdoc />
    public Task<BatchEvaluationResult?> GetAsync(
        string version,
        string datasetId,
        CancellationToken cancellationToken = default)
    {
        var key = GetKey(version, datasetId);
        if (_cache.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAt == null || entry.ExpiresAt > DateTime.UtcNow)
            {
                return Task.FromResult<BatchEvaluationResult?>(entry.Result);
            }
            // Entry expired, remove it
            _cache.TryRemove(key, out _);
        }
        return Task.FromResult<BatchEvaluationResult?>(null);
    }

    /// <inheritdoc />
    public Task SetAsync(
        string version,
        string datasetId,
        BatchEvaluationResult result,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        var key = GetKey(version, datasetId);
        var expiresAt = expiration.HasValue ? DateTime.UtcNow.Add(expiration.Value) : (DateTime?)null;
        _cache[key] = new CacheEntry(result, expiresAt);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(
        string version,
        string datasetId,
        CancellationToken cancellationToken = default)
    {
        var key = GetKey(version, datasetId);
        _cache.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(
        string version,
        string datasetId,
        CancellationToken cancellationToken = default)
    {
        var key = GetKey(version, datasetId);
        if (_cache.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAt == null || entry.ExpiresAt > DateTime.UtcNow)
            {
                return Task.FromResult(true);
            }
            _cache.TryRemove(key, out _);
        }
        return Task.FromResult(false);
    }
}
