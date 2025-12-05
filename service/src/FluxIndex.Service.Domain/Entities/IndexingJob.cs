namespace FluxIndex.Service.Domain.Entities;

/// <summary>
/// Represents a background indexing job for tracking document processing.
/// </summary>
public class IndexingJob
{
    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public IndexingJobStatus Status { get; private set; }
    public int TotalChunks { get; private set; }
    public int ProcessedChunks { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    // Navigation
    public Document? Document { get; private set; }

    private IndexingJob() { } // EF Core

    public static IndexingJob Create(Guid documentId)
    {
        return new IndexingJob
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            Status = IndexingJobStatus.Queued,
            TotalChunks = 0,
            ProcessedChunks = 0,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void Start(int totalChunks)
    {
        Status = IndexingJobStatus.Processing;
        TotalChunks = totalChunks;
        StartedAt = DateTime.UtcNow;
    }

    public void UpdateProgress(int processedChunks)
    {
        ProcessedChunks = processedChunks;
    }

    public void Complete()
    {
        Status = IndexingJobStatus.Completed;
        ProcessedChunks = TotalChunks;
        CompletedAt = DateTime.UtcNow;
    }

    public void Fail(string errorMessage)
    {
        Status = IndexingJobStatus.Failed;
        ErrorMessage = errorMessage;
        CompletedAt = DateTime.UtcNow;
    }

    public void Cancel()
    {
        Status = IndexingJobStatus.Cancelled;
        CompletedAt = DateTime.UtcNow;
    }

    public double GetProgressPercentage()
    {
        if (TotalChunks == 0) return 0;
        return (double)ProcessedChunks / TotalChunks * 100;
    }
}

public enum IndexingJobStatus
{
    Queued,
    Processing,
    Completed,
    Failed,
    Cancelled
}
