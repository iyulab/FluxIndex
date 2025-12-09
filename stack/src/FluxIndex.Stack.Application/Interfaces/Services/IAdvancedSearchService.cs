using FluxIndex.Stack.Shared.DTOs.Search;

namespace FluxIndex.Stack.Application.Interfaces.Services;

/// <summary>
/// Service interface for advanced search operations with dynamic fusion,
/// listwise reranking, entity extraction, and community-based search.
/// </summary>
public interface IAdvancedSearchService
{
    /// <summary>
    /// Perform an advanced search with dynamic fusion and optional advanced features.
    /// </summary>
    Task<AdvancedSearchResponse> SearchAsync(
        AdvancedSearchRequest request,
        string? apiKeyPrefix = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyze a query to determine its type, complexity, and optimal search strategy.
    /// </summary>
    Task<QueryAnalysisDto> AnalyzeQueryAsync(
        string query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extract entities from a collection's documents.
    /// </summary>
    Task<List<ExtractedEntityDto>> ExtractEntitiesAsync(
        Guid collectionId,
        int maxEntities = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Build communities from a collection's documents.
    /// </summary>
    Task<CommunitySearchInfoDto> BuildCommunitiesAsync(
        Guid collectionId,
        int maxLevels = 3,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get community hierarchy for a collection.
    /// </summary>
    Task<List<CommunityDto>> GetCommunitiesAsync(
        Guid collectionId,
        int? level = null,
        CancellationToken cancellationToken = default);
}
