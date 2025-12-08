using FluxIndex.Stack.Domain.Entities;

namespace FluxIndex.Stack.Application.Interfaces.Repositories;

/// <summary>
/// Repository interface for ReindexingJob entity.
/// </summary>
public interface IReindexingJobRepository
{
    Task<ReindexingJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the next pending job with highest priority.
    /// </summary>
    Task<ReindexingJob?> GetNextPendingJobAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all pending jobs.
    /// </summary>
    Task<List<ReindexingJob>> GetPendingJobsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets jobs by status.
    /// </summary>
    Task<List<ReindexingJob>> GetByStatusAsync(
        ReindexingJobStatus status,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets jobs for a specific target model.
    /// </summary>
    Task<List<ReindexingJob>> GetByTargetModelAsync(
        Guid targetModelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recent jobs with pagination.
    /// </summary>
    Task<(List<ReindexingJob> Items, int TotalCount)> GetPagedAsync(
        int page = 1,
        int pageSize = 20,
        ReindexingJobStatus? status = null,
        CancellationToken cancellationToken = default);

    Task AddAsync(ReindexingJob job, CancellationToken cancellationToken = default);

    Task UpdateAsync(ReindexingJob job, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if there's an active reindexing job for the target.
    /// </summary>
    Task<bool> HasActiveJobAsync(
        ReindexingScope scope,
        Guid? targetId,
        Guid targetModelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels all pending jobs for a specific model.
    /// </summary>
    Task CancelPendingJobsForModelAsync(
        Guid targetModelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets aggregate statistics about reindexing jobs.
    /// </summary>
    Task<ReindexingJobStats> GetStatsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Statistics about reindexing jobs.
/// </summary>
public record ReindexingJobStats(
    int PendingCount,
    int ProcessingCount,
    int CompletedCount,
    int FailedCount,
    int TotalChunksQueued,
    int TotalChunksProcessed);
