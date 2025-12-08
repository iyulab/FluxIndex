using FluxIndex.Stack.Application.Interfaces.Repositories;
using FluxIndex.Stack.Domain.Entities;
using FluxIndex.Stack.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FluxIndex.Stack.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for ReindexingJob entity.
/// </summary>
public class ReindexingJobRepository : IReindexingJobRepository
{
    private readonly ServiceDbContext _context;

    public ReindexingJobRepository(ServiceDbContext context)
    {
        _context = context;
    }

    public async Task<ReindexingJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.ReindexingJobs
            .Include(j => j.TargetModel)
            .Include(j => j.SourceModel)
            .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);
    }

    public async Task<ReindexingJob?> GetNextPendingJobAsync(CancellationToken cancellationToken = default)
    {
        return await _context.ReindexingJobs
            .Include(j => j.TargetModel)
            .Include(j => j.SourceModel)
            .Where(j => j.Status == ReindexingJobStatus.Pending)
            .OrderByDescending(j => j.Priority)
            .ThenBy(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<ReindexingJob>> GetPendingJobsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.ReindexingJobs
            .Include(j => j.TargetModel)
            .Where(j => j.Status == ReindexingJobStatus.Pending)
            .OrderByDescending(j => j.Priority)
            .ThenBy(j => j.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<ReindexingJob>> GetByStatusAsync(
        ReindexingJobStatus status,
        CancellationToken cancellationToken = default)
    {
        return await _context.ReindexingJobs
            .Include(j => j.TargetModel)
            .Include(j => j.SourceModel)
            .Where(j => j.Status == status)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<ReindexingJob>> GetByTargetModelAsync(
        Guid targetModelId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ReindexingJobs
            .Include(j => j.TargetModel)
            .Include(j => j.SourceModel)
            .Where(j => j.TargetModelId == targetModelId)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<(List<ReindexingJob> Items, int TotalCount)> GetPagedAsync(
        int page = 1,
        int pageSize = 20,
        ReindexingJobStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.ReindexingJobs
            .Include(j => j.TargetModel)
            .Include(j => j.SourceModel)
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(j => j.Status == status.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(j => j.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task AddAsync(ReindexingJob job, CancellationToken cancellationToken = default)
    {
        await _context.ReindexingJobs.AddAsync(job, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(ReindexingJob job, CancellationToken cancellationToken = default)
    {
        _context.ReindexingJobs.Update(job);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await _context.ReindexingJobs.FindAsync([id], cancellationToken);
        if (job != null)
        {
            _context.ReindexingJobs.Remove(job);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> HasActiveJobAsync(
        ReindexingScope scope,
        Guid? targetId,
        Guid targetModelId,
        CancellationToken cancellationToken = default)
    {
        var query = _context.ReindexingJobs
            .Where(j => j.Scope == scope
                && j.TargetModelId == targetModelId
                && (j.Status == ReindexingJobStatus.Pending || j.Status == ReindexingJobStatus.Processing));

        // For System scope, targetId is null
        if (scope == ReindexingScope.System)
        {
            return await query.AnyAsync(cancellationToken);
        }

        // For other scopes, match the target ID
        return await query
            .Where(j => (scope == ReindexingScope.Collection && j.CollectionId == targetId)
                || (scope == ReindexingScope.Document && j.DocumentId == targetId)
                || (scope == ReindexingScope.Chunk && j.ChunkId == targetId))
            .AnyAsync(cancellationToken);
    }

    public async Task CancelPendingJobsForModelAsync(
        Guid targetModelId,
        CancellationToken cancellationToken = default)
    {
        var pendingJobs = await _context.ReindexingJobs
            .Where(j => j.TargetModelId == targetModelId && j.Status == ReindexingJobStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (var job in pendingJobs)
        {
            job.Cancel();
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReindexingJobStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var stats = await _context.ReindexingJobs
            .GroupBy(_ => 1)
            .Select(g => new
            {
                PendingCount = g.Count(j => j.Status == ReindexingJobStatus.Pending),
                ProcessingCount = g.Count(j => j.Status == ReindexingJobStatus.Processing),
                CompletedCount = g.Count(j => j.Status == ReindexingJobStatus.Completed),
                FailedCount = g.Count(j => j.Status == ReindexingJobStatus.Failed),
                TotalChunksQueued = g.Sum(j => j.TotalChunks),
                TotalChunksProcessed = g.Sum(j => j.ProcessedChunks)
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (stats == null)
        {
            return new ReindexingJobStats(0, 0, 0, 0, 0, 0);
        }

        return new ReindexingJobStats(
            stats.PendingCount,
            stats.ProcessingCount,
            stats.CompletedCount,
            stats.FailedCount,
            stats.TotalChunksQueued,
            stats.TotalChunksProcessed);
    }
}
