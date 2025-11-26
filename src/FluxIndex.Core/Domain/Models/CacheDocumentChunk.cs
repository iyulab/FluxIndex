using System;
using System.Collections.Generic;
using FluxIndex.Core.Domain.ValueObjects;

namespace FluxIndex.Core.Domain.Models;

/// <summary>
/// 문서 청크 모델 - 시맨틱 캐시용 경량 버전
/// Note: This is a lightweight model for caching purposes.
/// For full document chunk entity, use FluxIndex.Core.Domain.Entities.DocumentChunk
/// </summary>
public class CacheDocumentChunk
{
    /// <summary>
    /// 고유 식별자
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// 소속 문서 ID
    /// </summary>
    public string DocumentId { get; init; } = string.Empty;

    /// <summary>
    /// 청크 내용
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// 청크 번호
    /// </summary>
    public int ChunkIndex { get; init; }

    /// <summary>
    /// 전체 청크 수
    /// </summary>
    public int TotalChunks { get; init; }

    /// <summary>
    /// 임베딩 벡터
    /// </summary>
    public float[]? Embedding { get; init; }

    /// <summary>
    /// 유사도 점수
    /// </summary>
    public float Score { get; init; }

    /// <summary>
    /// 토큰 수
    /// </summary>
    public int TokenCount { get; init; }

    /// <summary>
    /// 메타데이터
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();

    /// <summary>
    /// 생성 시간
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 문서 청크 생성
    /// </summary>
    public static CacheDocumentChunk Create(
        string documentId,
        string content,
        int chunkIndex,
        int totalChunks = 1,
        float[]? embedding = null,
        float score = 0f,
        int tokenCount = 0,
        Dictionary<string, object>? metadata = null)
    {
        return new CacheDocumentChunk
        {
            Id = Guid.NewGuid().ToString(),
            DocumentId = documentId,
            Content = content,
            ChunkIndex = chunkIndex,
            TotalChunks = totalChunks,
            Embedding = embedding,
            Score = score,
            TokenCount = tokenCount,
            Metadata = metadata ?? new Dictionary<string, object>(),
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 임베딩과 함께 문서 청크 생성
    /// </summary>
    public static CacheDocumentChunk Create(
        string documentId,
        string content,
        int chunkIndex,
        EmbeddingVector embeddingVector,
        int totalChunks = 1,
        float score = 0f,
        int tokenCount = 0,
        Dictionary<string, object>? metadata = null)
    {
        return Create(documentId, content, chunkIndex, totalChunks, embeddingVector.Values, score, tokenCount, metadata);
    }

    /// <summary>
    /// 메타데이터와 함께 복사
    /// </summary>
    public CacheDocumentChunk WithMetadata(Dictionary<string, object> newMetadata)
    {
        var combinedMetadata = new Dictionary<string, object>(Metadata);
        foreach (var kvp in newMetadata)
        {
            combinedMetadata[kvp.Key] = kvp.Value;
        }

        return new CacheDocumentChunk
        {
            Id = Id,
            DocumentId = DocumentId,
            Content = Content,
            ChunkIndex = ChunkIndex,
            TotalChunks = TotalChunks,
            Embedding = Embedding,
            Score = Score,
            TokenCount = TokenCount,
            Metadata = combinedMetadata,
            CreatedAt = CreatedAt
        };
    }

    /// <summary>
    /// 점수와 함께 복사
    /// </summary>
    public CacheDocumentChunk WithScore(float newScore)
    {
        return new CacheDocumentChunk
        {
            Id = Id,
            DocumentId = DocumentId,
            Content = Content,
            ChunkIndex = ChunkIndex,
            TotalChunks = TotalChunks,
            Embedding = Embedding,
            Score = newScore,
            TokenCount = TokenCount,
            Metadata = Metadata,
            CreatedAt = CreatedAt
        };
    }
}
