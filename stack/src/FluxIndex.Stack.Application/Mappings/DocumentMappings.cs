using FluxIndex.Stack.Domain.Entities;
using FluxIndex.Stack.Shared.DTOs.Documents;

namespace FluxIndex.Stack.Application.Mappings;

/// <summary>
/// Extension methods for mapping Document entities to DTOs.
/// </summary>
public static class DocumentMappings
{
    public static DocumentDto ToDto(this Document entity)
    {
        return new DocumentDto
        {
            Id = entity.Id,
            CollectionId = entity.CollectionId,
            Title = entity.Title,
            SourceType = entity.SourceType,
            SourcePath = entity.SourcePath,
            ContentHash = entity.ContentHash,
            FileSize = entity.FileSize,
            Status = entity.Status.ToString(),
            ChunkCount = entity.ChunkCount,
            Metadata = entity.Metadata,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            IndexedAt = entity.IndexedAt
        };
    }

    public static DocumentDetailDto ToDetailDto(this Document entity, List<DocumentChunk> chunks)
    {
        return new DocumentDetailDto
        {
            Id = entity.Id,
            CollectionId = entity.CollectionId,
            Title = entity.Title,
            SourceType = entity.SourceType,
            SourcePath = entity.SourcePath,
            ContentHash = entity.ContentHash,
            FileSize = entity.FileSize,
            Status = entity.Status.ToString(),
            ChunkCount = entity.ChunkCount,
            Metadata = entity.Metadata,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            IndexedAt = entity.IndexedAt,
            ExtractedContent = entity.ExtractedContent,
            QAPairs = entity.QAPairs.Select(qa => new QAPairDto
            {
                Question = qa.Question,
                Answer = qa.Answer
            }).ToList(),
            Chunks = chunks.Select(c => c.ToDto()).ToList()
        };
    }

    public static DocumentChunkDto ToDto(this DocumentChunk entity)
    {
        return new DocumentChunkDto
        {
            Id = entity.Id,
            ChunkIndex = entity.ChunkIndex,
            Content = entity.Content,
            TokenCount = entity.TokenCount,
            StartPosition = entity.StartPosition,
            EndPosition = entity.EndPosition,
            Metadata = entity.Metadata
        };
    }
}
