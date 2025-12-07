using FluxIndex.Stack.Application.Interfaces.Repositories;
using FluxIndex.Stack.Domain.Entities;
using FluxIndex.Stack.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FluxIndex.Stack.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for indexing job logs.
/// </summary>
public class IndexingJobLogRepository : IIndexingJobLogRepository
{
    private readonly ServiceDbContext _context;

    public IndexingJobLogRepository(ServiceDbContext context)
    {
        _context = context;
    }

    public async Task<List<IndexingJobLog>> GetByJobIdAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.IndexingJobLogs
            .Where(l => l.JobId == jobId)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<IndexingJobLog>> GetByJobIdAsync(Guid jobId, IndexingJobLogLevel? minLevel, CancellationToken cancellationToken = default)
    {
        var query = _context.IndexingJobLogs
            .Where(l => l.JobId == jobId);

        if (minLevel.HasValue)
        {
            query = query.Where(l => l.Level >= minLevel.Value);
        }

        return await query
            .OrderBy(l => l.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IndexingJobLog> AddAsync(IndexingJobLog log, CancellationToken cancellationToken = default)
    {
        _context.IndexingJobLogs.Add(log);
        await _context.SaveChangesAsync(cancellationToken);
        return log;
    }

    public async Task AddRangeAsync(IEnumerable<IndexingJobLog> logs, CancellationToken cancellationToken = default)
    {
        _context.IndexingJobLogs.AddRange(logs);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteByJobIdAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        await _context.IndexingJobLogs
            .Where(l => l.JobId == jobId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
