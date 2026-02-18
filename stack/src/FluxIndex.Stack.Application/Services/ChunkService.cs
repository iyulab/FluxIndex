using FluxIndex.Core.Interfaces;
using FluxIndex.Core.Models;
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
public partial class ChunkService : IChunkService
{
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly IDocumentRepository _documentRepository;
    private readonly IEmbeddingProvider? _embeddingProvider;
    private readonly IReindexingService? _reindexingService;
    private readonly IEmbeddingModelRepository? _embeddingModelRepository;
    private readonly IRuleBasedMetadataExtractor? _metadataExtractor;
    private readonly ILogger<ChunkService> _logger;

    public ChunkService(
        IDocumentChunkRepository chunkRepository,
        IDocumentRepository documentRepository,
        ILogger<ChunkService> logger,
        IEmbeddingProvider? embeddingProvider = null,
        IReindexingService? reindexingService = null,
        IEmbeddingModelRepository? embeddingModelRepository = null,
        IRuleBasedMetadataExtractor? metadataExtractor = null)
    {
        _chunkRepository = chunkRepository;
        _documentRepository = documentRepository;
        _embeddingProvider = embeddingProvider;
        _reindexingService = reindexingService;
        _embeddingModelRepository = embeddingModelRepository;
        _metadataExtractor = metadataExtractor;
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
            HasEmbedding = chunk.ChunkEmbeddings.Count > 0,
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
            HasEmbedding = c.ChunkEmbeddings.Count > 0,
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
            LogChunkContentUpdated(_logger, chunkId);
        }

        // Update metadata if provided
        if (request.Metadata != null)
        {
            chunk.MergeMetadata(request.Metadata, overwrite: true);
            LogChunkMetadataUpdated(_logger, chunkId);
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
            HasEmbedding = chunk.ChunkEmbeddings.Count > 0,
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

        LogChunkDeleted(_logger, chunkId, chunk.DocumentId);
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
            LogEnrichmentRequested(_logger, chunkId);

            // Basic metadata (always present)
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

            // Rule-based metadata extraction (if extractor available)
            if (_metadataExtractor != null)
            {
                var schema = ParseMetadataSchema(request.MetadataSchema);
                var extracted = await _metadataExtractor.ExtractAsync(
                    chunk.Content, schema, cancellationToken);

                if (extracted.Topics.Length > 0)
                    enrichedMetadata["topics"] = extracted.Topics;
                if (extracted.Keywords.Length > 0)
                    enrichedMetadata["keywords"] = extracted.Keywords;
                if (!string.IsNullOrWhiteSpace(extracted.Description))
                    enrichedMetadata["description"] = extracted.Description;
                if (!string.IsNullOrWhiteSpace(extracted.DocumentType))
                    enrichedMetadata["document_type"] = extracted.DocumentType;
                if (!string.IsNullOrWhiteSpace(extracted.Language))
                    enrichedMetadata["language"] = extracted.Language;
                if (extracted.Categories.Length > 0)
                    enrichedMetadata["categories"] = extracted.Categories;
                if (extracted.SchemaSpecificData.Count > 0)
                    enrichedMetadata["schema_specific"] = extracted.SchemaSpecificData;

                enrichedMetadata["extraction_method"] = extracted.ExtractionMethod;
                enrichedMetadata["extraction_confidence"] = extracted.OverallConfidence;

                LogMetadataExtracted(_logger, chunkId, request.MetadataSchema ?? "general");
            }

            chunk.MergeMetadata(enrichedMetadata, request.OverwriteExisting);
            await _chunkRepository.UpdateAsync(chunk, cancellationToken);

            LogChunkEnriched(_logger, chunkId);

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
            LogChunkEnrichmentFailed(_logger, chunkId, ex);
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

        if (_reindexingService == null || _embeddingModelRepository == null)
        {
            LogCannotRegenerateEmbeddingServiceUnavailable(_logger, chunkId);
            return;
        }

        var activeModel = await _embeddingModelRepository.GetActiveModelAsync(cancellationToken);
        if (activeModel == null)
        {
            LogCannotRegenerateEmbeddingNoModel(_logger, chunkId);
            return;
        }

        await _reindexingService.ReindexChunkAsync(chunkId, activeModel.Id, cancellationToken);
        LogEmbeddingRegenerated(_logger, chunkId, activeModel.ModelKey);
    }

    private static MetadataSchema ParseMetadataSchema(string? schemaName) => schemaName?.ToLowerInvariant() switch
    {
        "general" => MetadataSchema.General,
        "productmanual" or "product_manual" => MetadataSchema.ProductManual,
        "technicaldoc" or "technical_doc" => MetadataSchema.TechnicalDoc,
        "article" => MetadataSchema.Article,
        "custom" => MetadataSchema.Custom,
        _ => MetadataSchema.General
    };

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Chunk content updated: {ChunkId}")]
    private static partial void LogChunkContentUpdated(ILogger logger, Guid chunkId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Chunk metadata updated: {ChunkId}")]
    private static partial void LogChunkMetadataUpdated(ILogger logger, Guid chunkId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Chunk deleted: {ChunkId} from document {DocumentId}")]
    private static partial void LogChunkDeleted(ILogger logger, Guid chunkId, Guid documentId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Enrichment requested for chunk: {ChunkId}")]
    private static partial void LogEnrichmentRequested(ILogger logger, Guid chunkId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Chunk enriched: {ChunkId}")]
    private static partial void LogChunkEnriched(ILogger logger, Guid chunkId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Metadata extracted for chunk {ChunkId} using schema {Schema}")]
    private static partial void LogMetadataExtracted(ILogger logger, Guid chunkId, string schema);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to enrich chunk: {ChunkId}")]
    private static partial void LogChunkEnrichmentFailed(ILogger logger, Guid chunkId, Exception? exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot regenerate embedding: ReindexingService or EmbeddingModelRepository not available. Chunk: {ChunkId}")]
    private static partial void LogCannotRegenerateEmbeddingServiceUnavailable(ILogger logger, Guid chunkId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot regenerate embedding: no active embedding model configured. Chunk: {ChunkId}")]
    private static partial void LogCannotRegenerateEmbeddingNoModel(ILogger logger, Guid chunkId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Regenerated embedding for chunk {ChunkId} using model {ModelKey}")]
    private static partial void LogEmbeddingRegenerated(ILogger logger, Guid chunkId, string modelKey);

    #endregion
}
