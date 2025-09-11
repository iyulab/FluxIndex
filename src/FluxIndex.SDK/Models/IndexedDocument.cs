using System;
using System.Collections.Generic;

namespace FluxIndex.SDK;

/// <summary>
/// 인덱싱된 문서 모델
/// </summary>
public class IndexedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DocumentId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public float[] EmbeddingVector { get; set; } = Array.Empty<float>();
    public DocumentMetadata Metadata { get; set; } = new();
    public DateTime IndexedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int ChunkIndex { get; set; }
    public int TotalChunks { get; set; }
    public Dictionary<string, object> Properties { get; set; } = new();
}

/// <summary>
/// 문서 메타데이터
/// </summary>
public class DocumentMetadata
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Language { get; set; } = "ko";
    public string Version { get; set; } = string.Empty;
    public DateTime? PublishedDate { get; set; }
    public Dictionary<string, string> CustomFields { get; set; } = new();
}