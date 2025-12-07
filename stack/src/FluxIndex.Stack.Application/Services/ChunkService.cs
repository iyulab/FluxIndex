using FluxIndex.Stack.Application.Interfaces.Repositories;
using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Application.Mappings;
using FluxIndex.Stack.Shared.Common;
using FluxIndex.Stack.Shared.DTOs.Chunks;
using FluxIndex.Stack.Shared.DTOs.Documents;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Stack.Application.Services;

/// <summary>
/// Service implementation for chunk-level operations.
/// </summary>
public class ChunkService : IChunkService
{
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly IDocumentRepository _documentRepository;
    private readonly IEmbeddingProvider? _embeddingProvider;
    private readonly ILogger<ChunkService> _logger;

    public ChunkService(
        IDocumentChunkRepository chunkRepository,
        IDocumentRepository documentRepository,
        ILogger<ChunkService> logger,
        IEmbeddingProvider? embeddingProvider = null)
    {
        _chunkRepository = chunkRepository;
        _documentRepository = documentRepository;
        _embeddingProvider = embeddingProvider;
        _logger = logger;
    }

    public async Task<ChunkDetailDto?> GetByIdAsync(Guid chunkId, CancellationToken cancellationToken = default)
    {
        var chunk = await _chunkRepository.GetByIdAsync(chunkId, cancellationToken);
        if (chunk == null) return null;

        return new ChunkDetailDto
        {
            Id = chunk.Id,
            DocumentId = chunk.DocumentId,
            DocumentTitle = chunk.Document?.Title ?? string.Empty,
            ChunkIndex = chunk.ChunkIndex,
            Content = chunk.Content,
            TokenCount = chunk.TokenCount,
            StartPosition = chunk.StartPosition,
            EndPosition = chunk.EndPosition,
            Metadata = chunk.Metadata,
            HasEmbedding = chunk.Embedding != null,
            CreatedAt = chunk.CreatedAt
        };
    }

    public async Task<PagedResult<DocumentChunkDto>> GetByDocumentIdAsync(
        Guid documentId,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _chunkRepository.GetPagedAsync(page, pageSize, documentId, cancellationToken);
        var dtos = items.Select(c => c.ToDto()).ToList();
        return PagedResult<DocumentChunkDto>.Create(dtos, page, pageSize, totalCount);
    }

    public async Task<PagedResult<ChunkDetailDto>> GetPagedAsync(
        int page = 1,
        int pageSize = 20,
        Guid? documentId = null,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _chunkRepository.GetPagedAsync(page, pageSize, documentId, cancellationToken);

        var dtos = items.Select(c => new ChunkDetailDto
        {
            Id = c.Id,
            DocumentId = c.DocumentId,
            DocumentTitle = c.Document?.Title ?? string.Empty,
            ChunkIndex = c.ChunkIndex,
            Content = c.Content,
            TokenCount = c.TokenCount,
            StartPosition = c.StartPosition,
            EndPosition = c.EndPosition,
            Metadata = c.Metadata,
            HasEmbedding = c.Embedding != null,
            CreatedAt = c.CreatedAt
        }).ToList();

        return PagedResult<ChunkDetailDto>.Create(dtos, page, pageSize, totalCount);
    }

    public async Task<ChunkDetailDto> UpdateAsync(Guid chunkId, UpdateChunkRequest request, CancellationToken cancellationToken = default)
    {
        var chunk = await _chunkRepository.GetByIdAsync(chunkId, cancellationToken);
        if (chunk == null)
        {
            throw new KeyNotFoundException($"Chunk with id '{chunkId}' not found.");
        }

        // Update content if provided
        if (!string.IsNullOrWhiteSpace(request.Content))
        {
            chunk.UpdateContent(request.Content);
            _logger.LogInformation("Chunk content updated: {ChunkId}", chunkId);
        }

        // Update metadata if provided
        if (request.Metadata != null)
        {
            chunk.MergeMetadata(request.Metadata, overwrite: true);
            _logger.LogInformation("Chunk metadata updated: {ChunkId}", chunkId);
        }

        await _chunkRepository.UpdateAsync(chunk, cancellationToken);

        // Regenerate embedding if content changed and requested
        if (!string.IsNullOrWhiteSpace(request.Content) && request.RegenerateEmbedding)
        {
            await RegenerateEmbeddingAsync(chunkId, cancellationToken);
        }

        return new ChunkDetailDto
        {
            Id = chunk.Id,
            DocumentId = chunk.DocumentId,
            DocumentTitle = chunk.Document?.Title ?? string.Empty,
            ChunkIndex = chunk.ChunkIndex,
            Content = chunk.Content,
            TokenCount = chunk.TokenCount,
            StartPosition = chunk.StartPosition,
            EndPosition = chunk.EndPosition,
            Metadata = chunk.Metadata,
            HasEmbedding = chunk.Embedding != null,
            CreatedAt = chunk.CreatedAt
        };
    }

    public async Task DeleteAsync(Guid chunkId, CancellationToken cancellationToken = default)
    {
        var chunk = await _chunkRepository.GetByIdAsync(chunkId, cancellationToken);
        if (chunk == null)
        {
            throw new KeyNotFoundException($"Chunk with id '{chunkId}' not found.");
        }

        await _chunkRepository.DeleteAsync(chunkId, cancellationToken);

        // Update document chunk count
        var document = await _documentRepository.GetByIdAsync(chunk.DocumentId, cancellationToken);
        if (document != null)
        {
            var remainingCount = await _chunkRepository.GetCountAsync(chunk.DocumentId, cancellationToken);
            document.UpdateChunkCount(remainingCount);
            await _documentRepository.UpdateAsync(document, cancellationToken);
        }

        _logger.LogInformation("Chunk deleted: {ChunkId} from document {DocumentId}", chunkId, chunk.DocumentId);
    }

    public async Task<EnrichChunkResponse> EnrichAsync(Guid chunkId, EnrichChunkRequest request, CancellationToken cancellationToken = default)
    {
        var chunk = await _chunkRepository.GetByIdAsync(chunkId, cancellationToken);
        if (chunk == null)
        {
            throw new KeyNotFoundException($"Chunk with id '{chunkId}' not found.");
        }

        try
        {
            // Use embedding provider for enrichment if available
            if (_embeddingProvider != null)
            {
                // TODO: Integrate with FluxIndex metadata extraction when available
                _logger.LogInformation("Enrichment requested for chunk: {ChunkId}", chunkId);
            }

            // For now, add basic metadata
            var enrichedMetadata = new Dictionary<string, object>
            {
                ["enriched_at"] = DateTime.UtcNow,
                ["schema"] = request.MetadataSchema ?? "default",
                ["word_count"] = chunk.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
                ["character_count"] = chunk.Content.Length
            };

            if (!string.IsNullOrWhiteSpace(request.Context))
            {
                enrichedMetadata["context"] = request.Context;
            }

            chunk.MergeMetadata(enrichedMetadata, request.OverwriteExisting);
            await _chunkRepository.UpdateAsync(chunk, cancellationToken);

            _logger.LogInformation("Chunk enriched: {ChunkId}", chunkId);

            return new EnrichChunkResponse
            {
                ChunkId = chunkId,
                Success = true,
                EnrichedMetadata = enrichedMetadata,
                Message = "Chunk metadata enriched successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enrich chunk: {ChunkId}", chunkId);
            return new EnrichChunkResponse
            {
                ChunkId = chunkId,
                Success = false,
                Message = ex.Message
            };
        }
    }

    public async Task RegenerateEmbeddingAsync(Guid chunkId, CancellationToken cancellationToken = default)
    {
        var chunk = await _chunkRepository.GetByIdAsync(chunkId, cancellationToken);
        if (chunk == null)
        {
            throw new KeyNotFoundException($"Chunk with id '{chunkId}' not found.");
        }

        // Use embedding provider for embedding generation if available
        if (_embeddingProvider != null)
        {
            try
            {
                var embedding = await _embeddingProvider.GetEmbeddingAsync(chunk.Content, cancellationToken);
                if (embedding != null && embedding.Length > 0)
                {
                    chunk.SetEmbedding(embedding);
                    await _chunkRepository.UpdateAsync(chunk, cancellationToken);
                    _logger.LogInformation("Embedding regenerated for chunk: {ChunkId}", chunkId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to regenerate embedding for chunk: {ChunkId}", chunkId);
                throw;
            }
        }
        else
        {
            _logger.LogWarning("Embedding provider not available for embedding generation. Chunk: {ChunkId}", chunkId);
        }
    }
}
