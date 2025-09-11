using System;
using System.Collections.Generic;

namespace FluxIndex.SDK;

/// <summary>
/// 임베딩 모델 정보
/// </summary>
public class EmbeddingModelInfo
{
    public string ModelName { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public int Dimension { get; set; }
    public int MaxTokens { get; set; }
    public string Version { get; set; } = string.Empty;
    public Dictionary<string, object> Properties { get; set; } = new();
}

/// <summary>
/// 임베딩 벡터
/// </summary>
public class EmbeddingVector
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public float[] Vector { get; set; } = Array.Empty<float>();
    public string Text { get; set; } = string.Empty;
    public int TokenCount { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// 벡터 저장소 통계
/// </summary>
public class VectorStoreStats
{
    public long TotalDocuments { get; set; }
    public long TotalChunks { get; set; }
    public long TotalVectors { get; set; }
    public long StorageSizeBytes { get; set; }
    public int VectorDimension { get; set; }
    public DateTime LastUpdated { get; set; }
    public Dictionary<string, long> DocumentsByCategory { get; set; } = new();
    public Dictionary<string, long> DocumentsByBrand { get; set; } = new();
    public Dictionary<string, long> DocumentsByLanguage { get; set; } = new();
}