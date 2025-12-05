using FluxIndex.Service.Application.Interfaces.Repositories;
using FluxIndex.Service.Application.Interfaces.Services;
using FluxIndex.Service.Domain.Entities;
using FluxIndex.Service.Shared.DTOs.Analytics;

namespace FluxIndex.Service.Application.Services;

/// <summary>
/// Service implementation for analytics and statistics.
/// </summary>
public class AnalyticsService : IAnalyticsService
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly ICollectionRepository _collectionRepository;
    private readonly ISearchHistoryRepository _searchHistoryRepository;

    public AnalyticsService(
        IDocumentRepository documentRepository,
        IDocumentChunkRepository chunkRepository,
        ICollectionRepository collectionRepository,
        ISearchHistoryRepository searchHistoryRepository)
    {
        _documentRepository = documentRepository;
        _chunkRepository = chunkRepository;
        _collectionRepository = collectionRepository;
        _searchHistoryRepository = searchHistoryRepository;
    }

    public async Task<SystemStatsDto> GetSystemStatsAsync(CancellationToken cancellationToken = default)
    {
        var totalDocuments = await _documentRepository.GetCountAsync(cancellationToken: cancellationToken);
        var totalChunks = await _chunkRepository.GetCountAsync(cancellationToken: cancellationToken);
        var collections = await _collectionRepository.GetAllAsync(cancellationToken);
        var totalStorage = await _documentRepository.GetTotalFileSizeAsync(cancellationToken: cancellationToken);
        var indexedDocs = await _documentRepository.GetCountAsync(status: DocumentStatus.Indexed, cancellationToken: cancellationToken);
        var pendingDocs = await _documentRepository.GetCountAsync(status: DocumentStatus.Pending, cancellationToken: cancellationToken);
        var failedDocs = await _documentRepository.GetCountAsync(status: DocumentStatus.Failed, cancellationToken: cancellationToken);

        return new SystemStatsDto
        {
            TotalDocuments = totalDocuments,
            TotalChunks = totalChunks,
            TotalCollections = collections.Count,
            TotalStorageBytes = totalStorage,
            IndexedDocuments = indexedDocs,
            PendingDocuments = pendingDocs,
            FailedDocuments = failedDocs
        };
    }

    public async Task<SearchAnalyticsDto> GetSearchAnalyticsAsync(
        int days = 30,
        Guid? collectionId = null,
        CancellationToken cancellationToken = default)
    {
        var fromDate = DateTime.UtcNow.AddDays(-days);

        var totalSearches = await _searchHistoryRepository.GetTotalCountAsync(
            collectionId, fromDate, cancellationToken: cancellationToken);

        var avgExecutionTime = await _searchHistoryRepository.GetAverageExecutionTimeAsync(
            collectionId, fromDate, cancellationToken: cancellationToken);

        var topQueries = await _searchHistoryRepository.GetTopQueriesAsync(
            10, collectionId, fromDate, cancellationToken: cancellationToken);

        var dailyTrends = await _searchHistoryRepository.GetDailyTrendsAsync(
            days, collectionId, cancellationToken);

        return new SearchAnalyticsDto
        {
            TotalSearches = totalSearches,
            AverageExecutionTimeMs = avgExecutionTime,
            AverageResultCount = 0, // TODO: Implement
            TopQueries = topQueries.Select(q => new TopQueryDto
            {
                Query = q.Query,
                Count = q.Count,
                AverageExecutionTimeMs = 0 // TODO: Implement per-query average
            }).ToList(),
            DailyTrends = dailyTrends.Select(t => new SearchTrendDto
            {
                Date = t.Date,
                SearchCount = t.Count,
                AverageExecutionTimeMs = t.AvgTime
            }).ToList()
        };
    }

    public async Task<DocumentAnalyticsDto> GetDocumentAnalyticsAsync(
        int days = 30,
        Guid? collectionId = null,
        CancellationToken cancellationToken = default)
    {
        // TODO: Implement detailed document analytics
        // This requires additional repository methods for grouping by source type and status

        var byStatus = new List<DocumentStatusStatsDto>();
        foreach (DocumentStatus status in Enum.GetValues<DocumentStatus>())
        {
            var count = await _documentRepository.GetCountAsync(collectionId, status, cancellationToken);
            byStatus.Add(new DocumentStatusStatsDto
            {
                Status = status.ToString(),
                Count = count
            });
        }

        return new DocumentAnalyticsDto
        {
            BySourceType = new List<DocumentTypeStatsDto>(), // TODO: Implement
            ByStatus = byStatus,
            DailyUploads = new List<DocumentTrendDto>() // TODO: Implement
        };
    }
}
