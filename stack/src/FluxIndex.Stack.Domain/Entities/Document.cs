namespace FluxIndex.Stack.Domain.Entities;

/// <summary>
/// Represents a document in the system.
/// </summary>
public class Document
{
    public Guid Id { get; private set; }
    public Guid? CollectionId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? SourceType { get; private set; }
    public string? SourcePath { get; private set; }
    public string? ContentHash { get; private set; }
    public long? FileSize { get; private set; }
    public DocumentStatus Status { get; private set; }
    public Dictionary<string, object> Metadata { get; private set; } = new();
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public DateTime? IndexedAt { get; private set; }

    /// <summary>
    /// Full extracted text content of the document.
    /// </summary>
    public string? ExtractedContent { get; private set; }

    /// <summary>
    /// Document-level Q&A pairs stored as JSON.
    /// </summary>
    public List<DocumentQAPair> QAPairs { get; private set; } = new();

    // Computed properties
    public int ChunkCount { get; private set; }

    // Navigation
    public Collection? Collection { get; private set; }

    private Document() { } // EF Core

    public static Document Create(
        string title,
        Guid? collectionId = null,
        string? sourceType = null,
        string? sourcePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return new Document
        {
            Id = Guid.NewGuid(),
            CollectionId = collectionId,
            Title = title,
            SourceType = sourceType,
            SourcePath = sourcePath,
            Status = DocumentStatus.Pending,
            Metadata = new Dictionary<string, object>(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void SetContentHash(string hash, long? fileSize = null)
    {
        ContentHash = hash;
        FileSize = fileSize;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetMetadata(Dictionary<string, object> metadata)
    {
        Metadata = metadata ?? new Dictionary<string, object>();
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkAsPending()
    {
        Status = DocumentStatus.Pending;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkAsProcessing()
    {
        Status = DocumentStatus.Processing;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkAsIndexed(int chunkCount)
    {
        Status = DocumentStatus.Indexed;
        ChunkCount = chunkCount;
        IndexedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkAsFailed(string? errorMessage = null)
    {
        Status = DocumentStatus.Failed;
        if (errorMessage != null)
        {
            Metadata["error"] = errorMessage;
        }
        UpdatedAt = DateTime.UtcNow;
    }

    public void Update(string title, Dictionary<string, object>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        Title = title;
        if (metadata != null)
        {
            Metadata = metadata;
        }
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Updates the chunk count for the document.
    /// </summary>
    public void UpdateChunkCount(int chunkCount)
    {
        ChunkCount = chunkCount;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Sets the extracted content of the document.
    /// </summary>
    public void SetExtractedContent(string? content)
    {
        ExtractedContent = content;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Sets the document-level Q&A pairs.
    /// </summary>
    public void SetQAPairs(List<DocumentQAPair> qaPairs)
    {
        QAPairs = qaPairs ?? new List<DocumentQAPair>();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Adds a Q&A pair to the document.
    /// </summary>
    public void AddQAPair(string question, string answer)
    {
        QAPairs.Add(new DocumentQAPair { Question = question, Answer = answer });
        UpdatedAt = DateTime.UtcNow;
    }
}

public enum DocumentStatus
{
    Pending,
    Processing,
    Indexed,
    Failed
}

/// <summary>
/// Represents a question-answer pair for document-level QA.
/// </summary>
public class DocumentQAPair
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
}
