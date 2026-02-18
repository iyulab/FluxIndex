namespace FluxIndex.Stack.Shared.DTOs.Chunks;

/// <summary>
/// Detailed chunk DTO with all properties.
/// </summary>
public class ChunkDetailDto
{
    public Guid Id { get; init; }
    public Guid DocumentId { get; init; }
    public string DocumentTitle { get; init; } = string.Empty;
    public int ChunkIndex { get; init; }
    public string Content { get; init; } = string.Empty;
    public int TokenCount { get; init; }
    public int StartPosition { get; init; }
    public int EndPosition { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
    public bool HasEmbedding { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>
/// Request to update a chunk's content.
/// </summary>
public class UpdateChunkRequest
{
    /// <summary>
    /// New content for the chunk. If null, content is not updated.
    /// </summary>
    public string? Content { get; init; }

    /// <summary>
    /// Updated metadata. If null, metadata is not updated.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Whether to regenerate the embedding after update.
    /// </summary>
    public bool RegenerateEmbedding { get; init; } = true;
}

/// <summary>
/// Request to enrich a chunk with AI-generated metadata.
/// </summary>
public class EnrichChunkRequest
{
    /// <summary>
    /// Metadata schema to use for enrichment.
    /// </summary>
    public string? MetadataSchema { get; init; }

    /// <summary>
    /// Additional context to provide to the AI for enrichment.
    /// </summary>
    public string? Context { get; init; }

    /// <summary>
    /// Whether to overwrite existing metadata.
    /// </summary>
    public bool OverwriteExisting { get; init; }
}

/// <summary>
/// Response after chunk enrichment.
/// </summary>
public class EnrichChunkResponse
{
    public Guid ChunkId { get; init; }
    public bool Success { get; init; }
    public Dictionary<string, object> EnrichedMetadata { get; init; } = new();
    public string? Message { get; init; }
}

/// <summary>
/// Paginated chunks request.
/// </summary>
public class GetChunksRequest
{
    public Guid? DocumentId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public bool IncludeContent { get; init; } = true;
    public bool IncludeMetadata { get; init; } = true;
}
