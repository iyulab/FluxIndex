namespace FluxIndex.Stack.Domain.Entities;

/// <summary>
/// Represents a log entry for an indexing job.
/// </summary>
public class IndexingJobLog
{
    public Guid Id { get; private set; }
    public Guid JobId { get; private set; }
    public IndexingJobLogLevel Level { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public string? Details { get; private set; }
    public string? Phase { get; private set; }
    public int? ChunkIndex { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Navigation
    public IndexingJob? Job { get; private set; }

    private IndexingJobLog() { } // EF Core

    public static IndexingJobLog Create(
        Guid jobId,
        IndexingJobLogLevel level,
        string message,
        string? details = null,
        string? phase = null,
        int? chunkIndex = null)
    {
        return new IndexingJobLog
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            Level = level,
            Message = message,
            Details = details,
            Phase = phase,
            ChunkIndex = chunkIndex,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static IndexingJobLog Info(Guid jobId, string message, string? phase = null, int? chunkIndex = null)
        => Create(jobId, IndexingJobLogLevel.Info, message, phase: phase, chunkIndex: chunkIndex);

    public static IndexingJobLog Warning(Guid jobId, string message, string? details = null, string? phase = null)
        => Create(jobId, IndexingJobLogLevel.Warning, message, details, phase);

    public static IndexingJobLog Error(Guid jobId, string message, string? details = null, string? phase = null)
        => Create(jobId, IndexingJobLogLevel.Error, message, details, phase);

    public static IndexingJobLog Debug(Guid jobId, string message, string? details = null, string? phase = null, int? chunkIndex = null)
        => Create(jobId, IndexingJobLogLevel.Debug, message, details, phase, chunkIndex);
}

public enum IndexingJobLogLevel
{
    Debug,
    Info,
    Warning,
    Error
}
