using FluxIndex.Service.Application.Interfaces.Repositories;
using FluxIndex.Service.Domain.Entities;
using FluxIndex.Service.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FluxIndex.Service.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for SearchHistory entity.
/// </summary>
public class SearchHistoryRepository : ISearchHistoryRepository
{
    private readonly ServiceDbContext _context;

    public SearchHistoryRepository(ServiceDbContext context)
    {
        _context = context;
    }

    public async Task<SearchHistory> AddAsync(SearchHistory searchHistory, CancellationToken cancellationToken = default)
    {
        await _context.SearchHistories.AddAsync(searchHistory, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return searchHistory;
    }

    public async Task<(List<SearchHistory> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        Guid? collectionId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var query = BuildFilteredQuery(collectionId, fromDate, toDate);
        query = query.OrderByDescending(s => s.CreatedAt);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<int> GetTotalCountAsync(
        Guid? collectionId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        return await BuildFilteredQuery(collectionId, fromDate, toDate)
            .CountAsync(cancellationToken);
    }

    public async Task<double> GetAverageExecutionTimeAsync(
        Guid? collectionId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var query = BuildFilteredQuery(collectionId, fromDate, toDate);
        var count = await query.CountAsync(cancellationToken);

        if (count == 0) return 0;

        return await query.AverageAsync(s => s.ExecutionTimeMs, cancellationToken);
    }

    public async Task<List<(string Query, int Count)>> GetTopQueriesAsync(
        int count,
        Guid? collectionId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var query = BuildFilteredQuery(collectionId, fromDate, toDate);

        var results = await query
            .GroupBy(s => s.Query)
            .Select(g => new { Query = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(count)
            .ToListAsync(cancellationToken);

        return results.Select(r => (r.Query, r.Count)).ToList();
    }

    public async Task<List<(DateTime Date, int Count, double AvgTime)>> GetDailyTrendsAsync(
        int days,
        Guid? collectionId = null,
        CancellationToken cancellationToken = default)
    {
        var fromDate = DateTime.UtcNow.Date.AddDays(-days + 1);
        var query = BuildFilteredQuery(collectionId, fromDate, null);

        var results = await query
            .GroupBy(s => s.CreatedAt.Date)
            .Select(g => new
            {
                Date = g.Key,
                Count = g.Count(),
                AvgTime = g.Average(s => s.ExecutionTimeMs)
            })
            .OrderBy(x => x.Date)
            .ToListAsync(cancellationToken);

        return results.Select(r => (r.Date, r.Count, r.AvgTime)).ToList();
    }

    private IQueryable<SearchHistory> BuildFilteredQuery(
        Guid? collectionId,
        DateTime? fromDate,
        DateTime? toDate)
    {
        var query = _context.SearchHistories.AsQueryable();

        if (collectionId.HasValue)
        {
            query = query.Where(s => s.CollectionId == collectionId.Value);
        }

        if (fromDate.HasValue)
        {
            query = query.Where(s => s.CreatedAt >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(s => s.CreatedAt <= toDate.Value);
        }

        return query;
    }
}
