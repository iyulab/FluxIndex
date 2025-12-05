namespace FluxIndex.Service.Shared.DTOs.Jobs;

/// <summary>
/// Indexing job DTO.
/// </summary>
public class IndexingJobDto
{
    public Guid Id { get; init; }
    public Guid DocumentId { get; init; }
    public string DocumentTitle { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int TotalChunks { get; init; }
    public int ProcessedChunks { get; init; }
    public double ProgressPercentage { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public double? DurationMs { get; init; }
}

/// <summary>
/// Job status summary for dashboard.
/// </summary>
public class JobStatusSummaryDto
{
    public int QueuedCount { get; init; }
    public int ProcessingCount { get; init; }
    public int CompletedCount { get; init; }
    public int FailedCount { get; init; }
    public int TotalCount { get; init; }
    public double AverageProcessingTimeMs { get; init; }
}
