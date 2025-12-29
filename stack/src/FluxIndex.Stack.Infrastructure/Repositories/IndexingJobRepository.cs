using FluxIndex.Stack.Application.Interfaces.Repositories;
using FluxIndex.Stack.Domain.Entities;
using FluxIndex.Stack.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FluxIndex.Stack.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for IndexingJob entity.
/// </summary>
public class IndexingJobRepository : IIndexingJobRepository
{
    private readonly ServiceDbContext _context;

    public IndexingJobRepository(ServiceDbContext context)
    {
        _context = context;
    }

    public async Task<IndexingJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.IndexingJobs
            .Include(j => j.Document)
            .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);
    }

    public async Task<IndexingJob?> GetByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        return await _context.IndexingJobs
            .Include(j => j.Document)
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync(j => j.DocumentId == documentId, cancellationToken);
    }

    public async Task<List<IndexingJob>> GetByStatusAsync(IndexingJobStatus status, CancellationToken cancellationToken = default)
    {
        return await _context.IndexingJobs
            .Where(j => j.Status == status)
            .Include(j => j.Document)
            .OrderBy(j => j.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<(List<IndexingJob> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        IndexingJobStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.IndexingJobs.AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(j => j.Status == status.Value);
        }

        query = query.OrderByDescending(j => j.CreatedAt);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Include(j => j.Document)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IndexingJob> AddAsync(IndexingJob job, CancellationToken cancellationToken = default)
    {
        await _context.IndexingJobs.AddAsync(job, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return job;
    }

    public async Task UpdateAsync(IndexingJob job, CancellationToken cancellationToken = default)
    {
        _context.IndexingJobs.Update(job);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await GetByIdAsync(id, cancellationToken);
        if (job != null)
        {
            _context.IndexingJobs.Remove(job);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<int> GetCountByStatusAsync(IndexingJobStatus status, CancellationToken cancellationToken = default)
    {
        return await _context.IndexingJobs
            .CountAsync(j => j.Status == status, cancellationToken);
    }

    public async Task<double> GetAverageProcessingTimeAsync(CancellationToken cancellationToken = default)
    {
        var completedJobs = await _context.IndexingJobs
            .Where(j => j.Status == IndexingJobStatus.Completed && j.StartedAt.HasValue && j.CompletedAt.HasValue)
            .ToListAsync(cancellationToken);

        if (!completedJobs.Any()) return 0;

        return completedJobs
            .Average(j => (j.CompletedAt!.Value - j.StartedAt!.Value).TotalMilliseconds);
    }

    public async Task<IndexingJob?> GetNextQueuedAsync(CancellationToken cancellationToken = default)
    {
        return await _context.IndexingJobs
            .Where(j => j.Status == IndexingJobStatus.Queued)
            .OrderBy(j => j.CreatedAt)
            .Include(j => j.Document)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<int> ResetStuckProcessingJobsAsync(CancellationToken cancellationToken = default)
    {
        var stuckJobs = await _context.IndexingJobs
            .Where(j => j.Status == IndexingJobStatus.Processing)
            .ToListAsync(cancellationToken);

        foreach (var job in stuckJobs)
        {
            job.ResetToQueued();
        }

        if (stuckJobs.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return stuckJobs.Count;
    }
}
