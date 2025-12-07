using FluxIndex.Stack.Domain.Entities;

namespace FluxIndex.Stack.Application.Interfaces.Repositories;

/// <summary>
/// Repository interface for indexing job logs.
/// </summary>
public interface IIndexingJobLogRepository
{
    /// <summary>
    /// Gets all logs for a specific job.
    /// </summary>
    Task<List<IndexingJobLog>> GetByJobIdAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets logs for a job with level filter.
    /// </summary>
    Task<List<IndexingJobLog>> GetByJobIdAsync(Guid jobId, IndexingJobLogLevel? minLevel, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new log entry.
    /// </summary>
    Task<IndexingJobLog> AddAsync(IndexingJobLog log, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds multiple log entries.
    /// </summary>
    Task AddRangeAsync(IEnumerable<IndexingJobLog> logs, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all logs for a job.
    /// </summary>
    Task DeleteByJobIdAsync(Guid jobId, CancellationToken cancellationToken = default);
}
