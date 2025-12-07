namespace FluxIndex.Stack.Shared.DTOs.Jobs;

/// <summary>
/// Indexing job log entry DTO.
/// </summary>
public class IndexingJobLogDto
{
    public Guid Id { get; init; }
    public Guid JobId { get; init; }
    public string Level { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? Details { get; init; }
    public string? Phase { get; init; }
    public int? ChunkIndex { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Extended job DTO with logs.
/// </summary>
public class IndexingJobDetailDto : IndexingJobDto
{
    public List<IndexingJobLogDto> Logs { get; init; } = new();
}
