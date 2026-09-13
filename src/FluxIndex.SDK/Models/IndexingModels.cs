using System;
using System.Collections.Generic;
using FluxIndex.Core.Application.Interfaces;

namespace FluxIndex.SDK;

/// <summary>
/// 호출 단위 인덱싱 옵션 — <see cref="Indexer.IndexDocumentAsync(Core.Domain.Entities.Document, IndexingOptions?, CancellationToken)"/> 에 넘긴다.
/// </summary>
/// <remarks>
/// 이 오버로드는 호출자가 이미 청크로 나눈 <c>Document</c> 를 받으므로 여기 있는 청킹 필드는 읽히지 않는다 —
/// SDK 가 직접 나누는 경로(<see cref="Indexer.IndexDocumentAsync(string, string, Dictionary{string, object}?, CancellationToken)"/>)는
/// 빌더의 <see cref="IndexerOptions.ChunkSize"/>/<see cref="IndexerOptions.ChunkOverlap"/> 을 쓴다.
/// 인덱서가 실제로 읽는 필드는 <see cref="EnableGraphRAG"/>·<see cref="GraphRAGOptions"/>·<see cref="CustomOptions"/> 다.
/// </remarks>
public class IndexingOptions
{
    /// <summary>읽히지 않는다 — 이 호출은 청킹하지 않는다(위 remarks). SDK 의 분할기는 전략이 하나뿐이다.</summary>
    public string ChunkingStrategy { get; set; } = "Auto";
    /// <summary>읽히지 않는다 — 이 호출은 청킹하지 않는다(위 remarks). 청크 크기는 <see cref="IndexerOptions.ChunkSize"/>.</summary>
    public int MaxChunkSize { get; set; } = 512;
    /// <summary>읽히지 않는다 — 이 호출은 청킹하지 않는다(위 remarks). 겹침은 <see cref="IndexerOptions.ChunkOverlap"/>.</summary>
    public int OverlapSize { get; set; } = 64;
    /// <summary>읽히지 않는다 — 인덱서는 항상 임베딩을 만든다. 임베딩 없는 청크는 벡터 저장소가 저장하지 않으므로
    /// <c>false</c> 를 존중하려면 키워드 전용 적재 경로가 먼저 필요하다.</summary>
    public bool GenerateEmbeddings { get; set; } = true;
    /// <summary>읽히지 않는다 — AI 메타데이터 추출은 <c>IMetadataExtractor</c> 등록 +
    /// <see cref="IndexingOptionsExtensions.WithAIMetadataExtraction"/>(= <see cref="CustomOptions"/> 의 키)로 켠다.</summary>
    public bool ExtractMetadata { get; set; } = true;
    /// <summary>읽히지 않는다 — SDK 인덱서는 파일을 파싱하지 않는다(OCR 은 FileFlux 쪽 관심사).</summary>
    public bool EnableOCR { get; set; }
    /// <summary>
    /// 호출 단위 추가 설정. AI 메타데이터 추출 키(<see cref="IndexingOptionsExtensions"/>)가 여기 실린다.
    /// 빌더 수준 <see cref="IndexerOptions.CustomOptions"/> 위에 덧씌워지며 같은 키는 이쪽이 이긴다.
    /// </summary>
    public Dictionary<string, object> CustomOptions { get; set; } = new();

    /// <summary>
    /// GraphRAG 인덱싱 활성화.
    /// - null (기본값): IGraphRAGService가 등록되어 있으면 자동 활성화
    /// - true: 강제 활성화 (서비스 미등록 시 오류)
    /// - false: 강제 비활성화
    /// </summary>
    public bool? EnableGraphRAG { get; set; }

    /// <summary>
    /// GraphRAG 빌드 옵션.
    /// GraphRAG가 활성화될 때 사용됩니다.
    /// </summary>
    public GraphRAGBuildOptions? GraphRAGOptions { get; set; }
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

/// <summary>
/// 검색 진행 상황 (Phase 3: DX 개선)
/// </summary>
public class SearchProgress
{
    public string QueryId { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public int CurrentStep { get; set; }
    public int TotalSteps { get; set; }
    public float ProgressPercentage { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public int ResultsFound { get; set; }
}

/// <summary>
/// 배치 작업 진행 상황 (Phase 3: DX 개선)
/// </summary>
public class BatchProgress
{
    public string BatchId { get; set; } = string.Empty;
    public int CurrentItem { get; set; }
    public int TotalItems { get; set; }
    public float ProgressPercentage { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public int SuccessfulItems { get; set; }
    public int FailedItems { get; set; }
}