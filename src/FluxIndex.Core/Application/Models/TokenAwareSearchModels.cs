namespace FluxIndex.Core.Application.Models;

#region Query Analysis Models

/// <summary>
/// 질의 분석 결과
/// </summary>
public class QueryAnalysis
{
    public string OriginalQuery { get; set; } = string.Empty;
    public string NormalizedQuery { get; set; } = string.Empty;
    public int TokenCount { get; set; }
    public string Language { get; set; } = "en";
    public double LanguageConfidence { get; set; }
    public QueryIntent Intent { get; set; }
    public QueryComplexityLevel Complexity { get; set; }
    public List<string> Keywords { get; set; } = new();
    public List<QueryEntity> Entities { get; set; } = new();
    public SearchStrategy RecommendedStrategy { get; set; }
    public int RecommendedTopK { get; set; } = 10;
}

/// <summary>
/// 질의 복잡도 수준
/// </summary>
public enum QueryComplexityLevel
{
    Simple,
    Moderate,
    Complex,
    VeryComplex
}

/// <summary>
/// 질의 의도
/// </summary>
public enum QueryIntent
{
    Informational,
    HowTo,
    Comparison,
    Definition,
    Troubleshooting,
    Listing
}

/// <summary>
/// 질의 엔티티
/// </summary>
public class QueryEntity
{
    public string Text { get; set; } = string.Empty;
    public EntityType Type { get; set; }
    public double Confidence { get; set; }
}

/// <summary>
/// 엔티티 유형
/// </summary>
public enum EntityType
{
    Technology,
    Concept,
    Organization,
    Person,
    Other
}

#endregion

/// <summary>
/// 토큰 예산 기반 검색 요청
/// </summary>
public class TokenAwareSearchRequest
{
    /// <summary>
    /// 검색 질의
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// 최대 응답 토큰 수
    /// </summary>
    public int MaxTokens { get; set; } = 4000;

    /// <summary>
    /// 최소 점수 임계값
    /// </summary>
    public double MinScore { get; set; } = 0.5;

    /// <summary>
    /// 메타데이터 필터
    /// </summary>
    public Dictionary<string, object>? Filters { get; set; }

    /// <summary>
    /// 강제 검색 전략 (null이면 자동 선택)
    /// </summary>
    public SearchStrategy? ForceStrategy { get; set; }

    /// <summary>
    /// 다양성 가중치 (0-1, 높을수록 다양한 소스 선호)
    /// </summary>
    public double DiversityWeight { get; set; } = 0.2;
}

/// <summary>
/// 토큰 예산 기반 검색 결과
/// </summary>
public class TokenAwareSearchResult
{
    /// <summary>
    /// 원본 질의
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// 요청된 최대 토큰
    /// </summary>
    public int RequestedTokens { get; set; }

    /// <summary>
    /// 사용된 토큰
    /// </summary>
    public int UsedTokens { get; set; }

    /// <summary>
    /// 남은 토큰 예산
    /// </summary>
    public int RemainingBudget => RequestedTokens - UsedTokens;

    /// <summary>
    /// 선택된 청크 목록
    /// </summary>
    public List<SelectedChunk> Chunks { get; set; } = new();

    /// <summary>
    /// 검색된 총 청크 수
    /// </summary>
    public int TotalRetrieved { get; set; }

    /// <summary>
    /// 토큰 예산으로 제외된 청크 수
    /// </summary>
    public int TruncatedCount { get; set; }

    /// <summary>
    /// 질의 분석 결과
    /// </summary>
    public QueryAnalysis Analysis { get; set; } = new();

    /// <summary>
    /// 사용된 검색 전략
    /// </summary>
    public SearchStrategy Strategy { get; set; }

    /// <summary>
    /// 검색 소요 시간 (ms)
    /// </summary>
    public long ElapsedMilliseconds { get; set; }
}

/// <summary>
/// 선택된 청크 정보
/// </summary>
public class SelectedChunk
{
    /// <summary>
    /// 청크 ID
    /// </summary>
    public string ChunkId { get; set; } = string.Empty;

    /// <summary>
    /// 문서 ID
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// 콘텐츠
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 관련성 점수
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// 토큰 수
    /// </summary>
    public int TokenCount { get; set; }

    /// <summary>
    /// 누적 토큰 (이 청크까지)
    /// </summary>
    public int CumulativeTokens { get; set; }

    /// <summary>
    /// 청크 인덱스 (문서 내)
    /// </summary>
    public int ChunkIndex { get; set; }

    /// <summary>
    /// 메타데이터
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// 하이라이트 (검색어 매칭 부분)
    /// </summary>
    public List<string> Highlights { get; set; } = new();
}

/// <summary>
/// 검색 전략
/// </summary>
public enum SearchStrategy
{
    /// <summary>
    /// 벡터 검색만
    /// </summary>
    Vector,

    /// <summary>
    /// 키워드 검색만 (BM25)
    /// </summary>
    Keyword,

    /// <summary>
    /// 하이브리드 (벡터 + 키워드)
    /// </summary>
    Hybrid,

    /// <summary>
    /// 적응형 (질의 분석 기반 자동 선택)
    /// </summary>
    Adaptive
}
