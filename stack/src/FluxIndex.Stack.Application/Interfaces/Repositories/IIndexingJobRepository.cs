using FluxIndex.Stack.Domain.Entities;

namespace FluxIndex.Stack.Application.Interfaces.Repositories;

/// <summary>
/// Repository interface for IndexingJob entity.
/// </summary>
public interface IIndexingJobRepository
{
    Task<IndexingJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IndexingJob?> GetByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<List<IndexingJob>> GetByStatusAsync(IndexingJobStatus status, CancellationToken cancellationToken = default);
    Task<(List<IndexingJob> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        IndexingJobStatus? status = null,
        CancellationToken cancellationToken = default);
    Task<IndexingJob> AddAsync(IndexingJob job, CancellationToken cancellationToken = default);
    Task UpdateAsync(IndexingJob job, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> GetCountByStatusAsync(IndexingJobStatus status, CancellationToken cancellationToken = default);
    Task<double> GetAverageProcessingTimeAsync(CancellationToken cancellationToken = default);
    Task<IndexingJob?> GetNextQueuedAsync(CancellationToken cancellationToken = default);
}
