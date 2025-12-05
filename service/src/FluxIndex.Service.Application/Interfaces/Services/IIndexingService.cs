using FluxIndex.Service.Shared.Common;
using FluxIndex.Service.Shared.DTOs.Jobs;

namespace FluxIndex.Service.Application.Interfaces.Services;

/// <summary>
/// Service interface for indexing operations.
/// </summary>
public interface IIndexingService
{
    Task<Guid> QueueIndexingJobAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task ProcessNextJobAsync(CancellationToken cancellationToken = default);
    Task<IndexingJobDto?> GetJobStatusAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<PagedResult<IndexingJobDto>> GetJobsAsync(
        int page,
        int pageSize,
        string? status = null,
        CancellationToken cancellationToken = default);
    Task CancelJobAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<JobStatusSummaryDto> GetStatusSummaryAsync(CancellationToken cancellationToken = default);
}
