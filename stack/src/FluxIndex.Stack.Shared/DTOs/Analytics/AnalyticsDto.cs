namespace FluxIndex.Stack.Shared.DTOs.Analytics;

/// <summary>
/// System statistics DTO for dashboard.
/// </summary>
public class SystemStatsDto
{
    public int TotalDocuments { get; init; }
    public int TotalChunks { get; init; }
    public int TotalCollections { get; init; }
    public long TotalStorageBytes { get; init; }
    public int IndexedDocuments { get; init; }
    public int PendingDocuments { get; init; }
    public int FailedDocuments { get; init; }
}

/// <summary>
/// Search analytics DTO.
/// </summary>
public class SearchAnalyticsDto
{
    public int TotalSearches { get; init; }
    public double AverageExecutionTimeMs { get; init; }
    public double AverageResultCount { get; init; }
    public List<TopQueryDto> TopQueries { get; init; } = new();
    public List<SearchTrendDto> DailyTrends { get; init; } = new();
}

/// <summary>
/// Top query DTO for analytics.
/// </summary>
public class TopQueryDto
{
    public string Query { get; init; } = string.Empty;
    public int Count { get; init; }
    public double AverageExecutionTimeMs { get; init; }
}

/// <summary>
/// Daily search trend DTO.
/// </summary>
public class SearchTrendDto
{
    public DateTime Date { get; init; }
    public int SearchCount { get; init; }
    public double AverageExecutionTimeMs { get; init; }
}

/// <summary>
/// Document analytics DTO.
/// </summary>
public class DocumentAnalyticsDto
{
    public List<DocumentTypeStatsDto> BySourceType { get; init; } = new();
    public List<DocumentStatusStatsDto> ByStatus { get; init; } = new();
    public List<DocumentTrendDto> DailyUploads { get; init; } = new();
}

/// <summary>
/// Document type statistics.
/// </summary>
public class DocumentTypeStatsDto
{
    public string SourceType { get; init; } = string.Empty;
    public int Count { get; init; }
    public long TotalSizeBytes { get; init; }
}

/// <summary>
/// Document status statistics.
/// </summary>
public class DocumentStatusStatsDto
{
    public string Status { get; init; } = string.Empty;
    public int Count { get; init; }
}

/// <summary>
/// Daily document upload trend.
/// </summary>
public class DocumentTrendDto
{
    public DateTime Date { get; init; }
    public int UploadCount { get; init; }
    public int IndexedCount { get; init; }
}
