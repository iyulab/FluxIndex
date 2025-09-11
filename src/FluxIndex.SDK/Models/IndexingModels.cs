using System;
using System.Collections.Generic;

namespace FluxIndex.SDK;

/// <summary>
/// 인덱싱 옵션
/// </summary>
public class IndexingOptions
{
    public string ChunkingStrategy { get; set; } = "Auto";
    public int MaxChunkSize { get; set; } = 512;
    public int OverlapSize { get; set; } = 64;
    public bool GenerateEmbeddings { get; set; } = true;
    public bool ExtractMetadata { get; set; } = true;
    public bool EnableOCR { get; set; } = false;
    public Dictionary<string, object> CustomOptions { get; set; } = new();
}

/// <summary>
/// 인덱싱 결과
/// </summary>
public class IndexingResult
{
    public string JobId { get; set; } = Guid.NewGuid().ToString();
    public string DocumentId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public int ChunksIndexed { get; set; }
    public int TotalChunks { get; set; }
    public TimeSpan ProcessingTime { get; set; }
    public List<string> IndexedIds { get; set; } = new();
    public List<IndexingError> Errors { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// 일괄 인덱싱 결과
/// </summary>
public class BatchIndexingResult
{
    public string BatchId { get; set; } = Guid.NewGuid().ToString();
    public int TotalDocuments { get; set; }
    public int SuccessfulDocuments { get; set; }
    public int FailedDocuments { get; set; }
    public List<IndexingResult> Results { get; set; } = new();
    public TimeSpan TotalProcessingTime { get; set; }
}

/// <summary>
/// 인덱싱 진행 상황
/// </summary>
public class IndexingProgress
{
    public string JobId { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public int CurrentChunk { get; set; }
    public int TotalChunks { get; set; }
    public float ProgressPercentage { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 인덱싱 상태
/// </summary>
public class IndexingStatus
{
    public string JobId { get; set; } = string.Empty;
    public IndexingState State { get; set; }
    public float ProgressPercentage { get; set; }
    public int ChunksProcessed { get; set; }
    public int TotalChunks { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TimeSpan? EstimatedTimeRemaining { get; set; }
    public List<IndexingError> Errors { get; set; } = new();
}

/// <summary>
/// 인덱싱 오류
/// </summary>
public class IndexingError
{
    public string ErrorCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int? ChunkIndex { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Details { get; set; } = new();
}

/// <summary>
/// 인덱싱 상태 열거형
/// </summary>
public enum IndexingState
{
    Pending,
    Processing,
    Completed,
    Failed,
    Cancelled,
    Paused
}