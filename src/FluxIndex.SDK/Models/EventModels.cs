using System;
using System.Collections.Generic;

namespace FluxIndex.SDK;

/// <summary>
/// 인덱싱 시작 이벤트 아규먼트 (Phase 3: DX 개선)
/// </summary>
public class IndexingStartedEventArgs : EventArgs
{
    public string JobId { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public int TotalChunks { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// 인덱싱 완료 이벤트 아규먼트 (Phase 3: DX 개선)
/// </summary>
public class IndexingCompletedEventArgs : EventArgs
{
    public string JobId { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public int ChunksIndexed { get; set; }
    public int TotalChunks { get; set; }
    public bool Success { get; set; }
    public TimeSpan ProcessingTime { get; set; }
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
    public List<IndexingError> Errors { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// 인덱싱 실패 이벤트 아규먼트 (Phase 3: DX 개선)
/// </summary>
public class IndexingFailedEventArgs : EventArgs
{
    public string JobId { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
    public DateTime FailedAt { get; set; } = DateTime.UtcNow;
    public List<IndexingError> Errors { get; set; } = new();
}

/// <summary>
/// 검색 시작 이벤트 아규먼트 (Phase 3: DX 개선)
/// </summary>
public class SearchStartedEventArgs : EventArgs
{
    public string QueryId { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public string SearchType { get; set; } = string.Empty;
    public int TopK { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Parameters { get; set; } = new();
}

/// <summary>
/// 검색 완료 이벤트 아규먼트 (Phase 3: DX 개선)
/// </summary>
public class SearchCompletedEventArgs : EventArgs
{
    public string QueryId { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public string SearchType { get; set; } = string.Empty;
    public int ResultsFound { get; set; }
    public int RequestedTopK { get; set; }
    public TimeSpan ProcessingTime { get; set; }
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// 검색 실패 이벤트 아규먼트 (Phase 3: DX 개선)
/// </summary>
public class SearchFailedEventArgs : EventArgs
{
    public string QueryId { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
    public DateTime FailedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 배치 작업 시작 이벤트 아규먼트 (Phase 3: DX 개선)
/// </summary>
public class BatchStartedEventArgs : EventArgs
{
    public string BatchId { get; set; } = string.Empty;
    public int TotalItems { get; set; }
    public string BatchType { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// 배치 작업 완료 이벤트 아규먼트 (Phase 3: DX 개선)
/// </summary>
public class BatchCompletedEventArgs : EventArgs
{
    public string BatchId { get; set; } = string.Empty;
    public int TotalItems { get; set; }
    public int SuccessfulItems { get; set; }
    public int FailedItems { get; set; }
    public TimeSpan TotalProcessingTime { get; set; }
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// 품질 분석 완료 이벤트 아규먼트 (Phase 3: DX 개선)
/// </summary>
public class QualityAnalysisCompletedEventArgs : EventArgs
{
    public string DocumentId { get; set; } = string.Empty;
    public double OverallQualityScore { get; set; }
    public int QuestionsGenerated { get; set; }
    public double AnswerabilityScore { get; set; }
    public TimeSpan ProcessingTime { get; set; }
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Metrics { get; set; } = new();
}
