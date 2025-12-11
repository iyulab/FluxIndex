using FluxIndex.Stack.Domain.Entities;

namespace FluxIndex.Stack.Application.Interfaces.Repositories;

/// <summary>
/// Repository interface for SearchHistory entity.
/// </summary>
public interface ISearchHistoryRepository
{
    Task<SearchHistory> AddAsync(SearchHistory searchHistory, CancellationToken cancellationToken = default);
    Task<(List<SearchHistory> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        Guid? collectionId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);
    Task<int> GetTotalCountAsync(
        Guid? collectionId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);
    Task<double> GetAverageExecutionTimeAsync(
        Guid? collectionId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);
    Task<List<(string Query, int Count)>> GetTopQueriesAsync(
        int count,
        Guid? collectionId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);
    Task<List<(DateTime Date, int Count, double AvgTime)>> GetDailyTrendsAsync(
        int days,
        Guid? collectionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets average result count for searches.
    /// </summary>
    Task<double> GetAverageResultCountAsync(
        Guid? collectionId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets top queries with their average execution times.
    /// </summary>
    Task<List<(string Query, int Count, double AvgExecutionTimeMs)>> GetTopQueriesWithAvgTimeAsync(
        int count,
        Guid? collectionId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);
}
