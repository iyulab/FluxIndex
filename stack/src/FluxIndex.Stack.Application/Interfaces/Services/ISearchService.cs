using FluxIndex.Stack.Shared.DTOs.Search;

namespace FluxIndex.Stack.Application.Interfaces.Services;

/// <summary>
/// Service interface for search operations.
/// </summary>
public interface ISearchService
{
    Task<SearchResponse> SearchAsync(SearchRequest request, string? apiKeyPrefix = null, CancellationToken cancellationToken = default);
    Task<SemanticCacheEntryDto?> GetCachedResponseAsync(string query, double similarityThreshold = 0.95, CancellationToken cancellationToken = default);
    Task CacheResponseAsync(string query, string response, CancellationToken cancellationToken = default);
    Task ClearCacheAsync(Guid? collectionId = null, CancellationToken cancellationToken = default);
}
