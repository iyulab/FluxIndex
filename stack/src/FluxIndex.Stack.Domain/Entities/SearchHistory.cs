namespace FluxIndex.Stack.Domain.Entities;

/// <summary>
/// Represents a search query history entry for analytics and caching.
/// </summary>
public class SearchHistory
{
    public Guid Id { get; private set; }
    public Guid? CollectionId { get; private set; }
    public string Query { get; private set; } = string.Empty;
    public int ResultCount { get; private set; }
    public double ExecutionTimeMs { get; private set; }
    public SearchType SearchType { get; private set; }
    public string? ApiKeyPrefix { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Navigation
    public Collection? Collection { get; private set; }

    private SearchHistory() { } // EF Core

    public static SearchHistory Create(
        string query,
        Guid? collectionId,
        int resultCount,
        double executionTimeMs,
        SearchType searchType,
        string? apiKeyPrefix = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        return new SearchHistory
        {
            Id = Guid.NewGuid(),
            Query = query,
            CollectionId = collectionId,
            ResultCount = resultCount,
            ExecutionTimeMs = executionTimeMs,
            SearchType = searchType,
            ApiKeyPrefix = apiKeyPrefix,
            CreatedAt = DateTime.UtcNow
        };
    }
}

public enum SearchType
{
    Vector,
    Keyword,
    Hybrid
}
