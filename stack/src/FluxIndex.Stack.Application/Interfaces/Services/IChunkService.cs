using FluxIndex.Stack.Shared.Common;
using FluxIndex.Stack.Shared.DTOs.Chunks;
using FluxIndex.Stack.Shared.DTOs.Documents;

namespace FluxIndex.Stack.Application.Interfaces.Services;

/// <summary>
/// Service interface for chunk-level operations.
/// </summary>
public interface IChunkService
{
    /// <summary>
    /// Gets a chunk by its ID.
    /// </summary>
    Task<ChunkDetailDto?> GetByIdAsync(Guid chunkId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets chunks for a specific document.
    /// </summary>
    Task<PagedResult<DocumentChunkDto>> GetByDocumentIdAsync(
        Guid documentId,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all chunks with pagination.
    /// </summary>
    Task<PagedResult<ChunkDetailDto>> GetPagedAsync(
        int page = 1,
        int pageSize = 20,
        Guid? documentId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a chunk's content and/or metadata.
    /// </summary>
    Task<ChunkDetailDto> UpdateAsync(Guid chunkId, UpdateChunkRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a specific chunk.
    /// </summary>
    Task DeleteAsync(Guid chunkId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enriches a chunk with AI-generated metadata.
    /// </summary>
    Task<EnrichChunkResponse> EnrichAsync(Guid chunkId, EnrichChunkRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Regenerates the embedding for a chunk.
    /// </summary>
    Task RegenerateEmbeddingAsync(Guid chunkId, CancellationToken cancellationToken = default);
}
