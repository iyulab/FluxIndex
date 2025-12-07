namespace FluxIndex.Stack.Shared.DTOs.Documents;

/// <summary>
/// Data transfer object for Document entity.
/// </summary>
public class DocumentDto
{
    public Guid Id { get; init; }
    public Guid? CollectionId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? SourceType { get; init; }
    public string? SourcePath { get; init; }
    public string? ContentHash { get; init; }
    public long? FileSize { get; init; }
    public string Status { get; init; } = string.Empty;
    public int ChunkCount { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public DateTime? IndexedAt { get; init; }
}

/// <summary>
/// Detailed document DTO including chunks.
/// </summary>
public class DocumentDetailDto : DocumentDto
{
    public List<DocumentChunkDto> Chunks { get; init; } = new();
}

/// <summary>
/// Document chunk DTO.
/// </summary>
public class DocumentChunkDto
{
    public Guid Id { get; init; }
    public int ChunkIndex { get; init; }
    public string Content { get; init; } = string.Empty;
    public int TokenCount { get; init; }
    public int StartPosition { get; init; }
    public int EndPosition { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// Request to upload a document.
/// </summary>
public class UploadDocumentRequest
{
    public string Title { get; set; } = string.Empty;
    public Guid? CollectionId { get; set; }
    public string? SourceType { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Request to upload document with raw content.
/// </summary>
public class UploadDocumentContentRequest : UploadDocumentRequest
{
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// Request to update document metadata.
/// </summary>
public class UpdateDocumentRequest
{
    public string Title { get; set; } = string.Empty;
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Response after document upload.
/// </summary>
public class UploadDocumentResponse
{
    public Guid DocumentId { get; init; }
    public Guid? JobId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
