using System;
using System.Collections.Generic;

namespace FluxIndex.Core.Domain.Models;

/// <summary>
/// 문서 청크 모델 - 시맨틱 캐시용 경량 버전
/// </summary>
public class DocumentChunk
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
    /// 임베딩 벡터
    /// </summary>
    public float[]? Embedding { get; init; }

    /// <summary>
    /// 유사도 점수
    /// </summary>
    public float Score { get; init; }

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
    public static DocumentChunk Create(
        string documentId,
        string content,
        int chunkIndex,
        float[]? embedding = null,
        float score = 0f,
        Dictionary<string, object>? metadata = null)
    {
        return new DocumentChunk
        {
            Id = Guid.NewGuid().ToString(),
            DocumentId = documentId,
            Content = content,
            ChunkIndex = chunkIndex,
            Embedding = embedding,
            Score = score,
            Metadata = metadata ?? new Dictionary<string, object>(),
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 임베딩과 함께 문서 청크 생성
    /// </summary>
    public static DocumentChunk Create(
        string documentId,
        string content,
        int chunkIndex,
        EmbeddingVector embeddingVector,
        float score = 0f,
        Dictionary<string, object>? metadata = null)
    {
        return Create(documentId, content, chunkIndex, embeddingVector.Values, score, metadata);
    }

    /// <summary>
    /// 메타데이터와 함께 복사
    /// </summary>
    public DocumentChunk WithMetadata(Dictionary<string, object> newMetadata)
    {
        var combinedMetadata = new Dictionary<string, object>(Metadata);
        foreach (var kvp in newMetadata)
        {
            combinedMetadata[kvp.Key] = kvp.Value;
        }

        return new DocumentChunk
        {
            Id = Id,
            DocumentId = DocumentId,
            Content = Content,
            ChunkIndex = ChunkIndex,
            Embedding = Embedding,
            Score = Score,
            Metadata = combinedMetadata,
            CreatedAt = CreatedAt
        };
    }

    /// <summary>
    /// 점수와 함께 복사
    /// </summary>
    public DocumentChunk WithScore(float newScore)
    {
        return new DocumentChunk
        {
            Id = Id,
            DocumentId = DocumentId,
            Content = Content,
            ChunkIndex = ChunkIndex,
            Embedding = Embedding,
            Score = newScore,
            Metadata = Metadata,
            CreatedAt = CreatedAt
        };
    }
}

/// <summary>
/// 임베딩 벡터 값 객체
/// </summary>
public class EmbeddingVector
{
    /// <summary>
    /// 벡터 값들
    /// </summary>
    public float[] Values { get; init; }

    /// <summary>
    /// 벡터 차원
    /// </summary>
    public int Dimension => Values.Length;

    /// <summary>
    /// 모델 이름
    /// </summary>
    public string ModelName { get; init; } = string.Empty;

    /// <summary>
    /// 생성 시간
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 생성자
    /// </summary>
    public EmbeddingVector(float[] values, string modelName = "")
    {
        Values = values ?? throw new ArgumentNullException(nameof(values));
        ModelName = modelName;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 임베딩 벡터 생성
    /// </summary>
    public static EmbeddingVector Create(float[] values, string modelName = "")
    {
        return new EmbeddingVector(values, modelName);
    }

    /// <summary>
    /// 코사인 유사도 계산
    /// </summary>
    public float CosineSimilarity(EmbeddingVector other)
    {
        if (other == null || Values.Length != other.Values.Length)
            return 0f;

        var dotProduct = 0f;
        var magnitude1 = 0f;
        var magnitude2 = 0f;

        for (int i = 0; i < Values.Length; i++)
        {
            dotProduct += Values[i] * other.Values[i];
            magnitude1 += Values[i] * Values[i];
            magnitude2 += other.Values[i] * other.Values[i];
        }

        var magnitudeProduct = (float)(Math.Sqrt(magnitude1) * Math.Sqrt(magnitude2));
        return magnitudeProduct == 0f ? 0f : dotProduct / magnitudeProduct;
    }
}