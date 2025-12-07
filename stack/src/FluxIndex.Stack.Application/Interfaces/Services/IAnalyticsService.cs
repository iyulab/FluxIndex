using FluxIndex.Stack.Shared.DTOs.Analytics;

namespace FluxIndex.Stack.Application.Interfaces.Services;

/// <summary>
/// Service interface for analytics and statistics.
/// </summary>
public interface IAnalyticsService
{
    Task<SystemStatsDto> GetSystemStatsAsync(CancellationToken cancellationToken = default);
    Task<SearchAnalyticsDto> GetSearchAnalyticsAsync(
        int days = 30,
        Guid? collectionId = null,
        CancellationToken cancellationToken = default);
    Task<DocumentAnalyticsDto> GetDocumentAnalyticsAsync(
        int days = 30,
        Guid? collectionId = null,
        CancellationToken cancellationToken = default);
}
