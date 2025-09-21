using FluxIndex.Domain.Models;

namespace FluxIndex.Domain.Entities;

/// <summary>
/// 문서 엔티티
/// </summary>
public class Document
{
    public string Id { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public DocumentStatus Status { get; set; } = DocumentStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Metadata { get; set; } = new();
    public List<DocumentChunk> Chunks { get; set; } = new();

    /// <summary>
    /// 문서 생성 팩토리 메서드
    /// </summary>
    public static Document Create(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Document ID cannot be empty", nameof(id));

        return new Document
        {
            Id = id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 메타데이터 업데이트
    /// </summary>
    public void UpdateMetadata(DocumentMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        // DocumentMetadata의 속성들을 Dictionary로 변환
        Metadata["Brand"] = metadata.Brand;
        Metadata["Model"] = metadata.Model;
        Metadata["Category"] = metadata.Category;
        Metadata["Language"] = metadata.Language;
        Metadata["Version"] = metadata.Version;
        if (metadata.PublishedDate.HasValue)
            Metadata["PublishedDate"] = metadata.PublishedDate.Value;

        // CustomFields 추가
        foreach (var field in metadata.CustomFields)
        {
            Metadata[field.Key] = field.Value;
        }

        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 문서를 인덱싱 완료로 표시
    /// </summary>
    public void MarkAsIndexed()
    {
        Status = DocumentStatus.Indexed;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 문서를 실패로 표시
    /// </summary>
    public void MarkAsFailed()
    {
        Status = DocumentStatus.Failed;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 청크 추가
    /// </summary>
    public void AddChunk(DocumentChunk chunk)
    {
        // DocumentChunk의 속성이 init-only이므로 새로운 인스턴스를 생성해야 함
        if (chunk.DocumentId != Id)
        {
            var newChunk = new DocumentChunk
            {
                Id = chunk.Id,
                DocumentId = Id,
                Content = chunk.Content,
                ChunkIndex = chunk.ChunkIndex,
                Embedding = chunk.Embedding,
                Score = chunk.Score,
                TokenCount = chunk.TokenCount,
                Metadata = chunk.Metadata,
                CreatedAt = chunk.CreatedAt
            };
            Chunks.Add(newChunk);
        }
        else
        {
            Chunks.Add(chunk);
        }
    }

    /// <summary>
    /// 청크 제거
    /// </summary>
    public bool RemoveChunk(string chunkId)
    {
        return Chunks.RemoveAll(c => c.Id == chunkId) > 0;
    }

    /// <summary>
    /// 상태 업데이트
    /// </summary>
    public void UpdateStatus(DocumentStatus status)
    {
        Status = status;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 메타데이터 추가/업데이트
    /// </summary>
    public void SetMetadata(string key, object value)
    {
        Metadata[key] = value;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 메타데이터 조회
    /// </summary>
    public T? GetMetadata<T>(string key)
    {
        if (Metadata.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }
        return default;
    }
}

/// <summary>
/// 문서 상태
/// </summary>
public enum DocumentStatus
{
    Pending,
    Processing,
    Indexed,
    Failed,
    Deleted
}