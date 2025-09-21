using System;
using System.Collections.Generic;

namespace FluxIndex.Domain.Models;

/// <summary>
/// 하이브리드 검색 결과
/// </summary>
public record HybridSearchResult
{
    /// <summary>
    /// 문서 청크
    /// </summary>
    public DocumentChunk Chunk { get; init; } = new();

    /// <summary>
    /// 최종 융합 점수
    /// </summary>
    public double FusedScore { get; init; }

    /// <summary>
    /// 벡터 검색 점수
    /// </summary>
    public double VectorScore { get; init; }

    /// <summary>
    /// BM25 키워드 점수
    /// </summary>
    public double SparseScore { get; init; }

    /// <summary>
    /// 벡터 검색 순위
    /// </summary>
    public int VectorRank { get; init; }

    /// <summary>
    /// 키워드 검색 순위
    /// </summary>
    public int SparseRank { get; init; }

    /// <summary>
    /// 최종 융합 순위
    /// </summary>
    public int FusedRank { get; init; }

    /// <summary>
    /// 사용된 융합 방법
    /// </summary>
    public FusionMethod FusionMethod { get; init; }

    /// <summary>
    /// 매칭된 키워드
    /// </summary>
    public IReadOnlyList<string> MatchedTerms { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 검색 소스 (Vector, Sparse, Both)
    /// </summary>
    public SearchSource Source { get; init; }

    /// <summary>
    /// 융합 메타데이터
    /// </summary>
    public Dictionary<string, object> FusionMetadata { get; init; } = new();
}

/// <summary>
/// 배치 하이브리드 검색 결과
/// </summary>
public class BatchHybridSearchResult
{
    /// <summary>
    /// 쿼리
    /// </summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// 검색 결과 목록
    /// </summary>
    public IReadOnlyList<HybridSearchResult> Results { get; init; } = Array.Empty<HybridSearchResult>();

    /// <summary>
    /// 검색 실행 시간 (밀리초)
    /// </summary>
    public double SearchTimeMs { get; init; }

    /// <summary>
    /// 사용된 검색 전략
    /// </summary>
    public SearchStrategy Strategy { get; init; } = new();
}

/// <summary>
/// 하이브리드 검색 옵션
/// </summary>
public record HybridSearchOptions
{
    /// <summary>
    /// 최대 결과 수
    /// </summary>
    public int MaxResults { get; set; } = 10;

    /// <summary>
    /// 융합 방법
    /// </summary>
    public FusionMethod FusionMethod { get; set; } = FusionMethod.RRF;

    /// <summary>
    /// RRF k 매개변수
    /// </summary>
    public double RrfK { get; set; } = 60.0;

    /// <summary>
    /// 벡터 검색 가중치 (0.0 - 1.0)
    /// </summary>
    public double VectorWeight { get; set; } = 0.7;

    /// <summary>
    /// 키워드 검색 가중치 (0.0 - 1.0)
    /// </summary>
    public double SparseWeight { get; set; } = 0.3;

    /// <summary>
    /// 벡터 검색 옵션
    /// </summary>
    public VectorSearchOptions VectorOptions { get; set; } = new();

    /// <summary>
    /// 키워드 검색 옵션
    /// </summary>
    public SparseSearchOptions SparseOptions { get; set; } = new();

    /// <summary>
    /// 자동 전략 선택 사용 여부
    /// </summary>
    public bool EnableAutoStrategy { get; set; } = true;

    /// <summary>
    /// 최소 융합 점수
    /// </summary>
    public double MinFusedScore { get; set; } = 0.0;

    /// <summary>
    /// 검색 타임아웃 (밀리초)
    /// </summary>
    public int TimeoutMs { get; set; } = 30000;

    /// <summary>
    /// 결과 다양성 활성화
    /// </summary>
    public bool EnableDiversity { get; set; } = false;

    /// <summary>
    /// 다양성 임계값
    /// </summary>
    public double DiversityThreshold { get; set; } = 0.8;
}

/// <summary>
/// 검색 전략
/// </summary>
public class SearchStrategy
{
    /// <summary>
    /// 전략 유형
    /// </summary>
    public SearchStrategyType Type { get; init; }

    /// <summary>
    /// 추천 융합 방법
    /// </summary>
    public FusionMethod RecommendedFusion { get; init; }

    /// <summary>
    /// 추천 가중치
    /// </summary>
    public (double VectorWeight, double SparseWeight) RecommendedWeights { get; init; }

    /// <summary>
    /// 전략 신뢰도 (0.0 - 1.0)
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// 전략 근거
    /// </summary>
    public string Reasoning { get; init; } = string.Empty;

    /// <summary>
    /// 쿼리 특성 분석
    /// </summary>
    public QueryCharacteristics QueryCharacteristics { get; init; } = new();
}

/// <summary>
/// 융합 성능 메트릭
/// </summary>
public class FusionPerformanceMetrics
{
    /// <summary>
    /// 전체 정밀도 (Precision)
    /// </summary>
    public double Precision { get; init; }

    /// <summary>
    /// 전체 재현율 (Recall)
    /// </summary>
    public double Recall { get; init; }

    /// <summary>
    /// F1 점수
    /// </summary>
    public double F1Score { get; init; }

    /// <summary>
    /// Mean Reciprocal Rank (MRR)
    /// </summary>
    public double MRR { get; init; }

    /// <summary>
    /// NDCG@K
    /// </summary>
    public double NDCG { get; init; }

    /// <summary>
    /// 융합 방법별 성능
    /// </summary>
    public Dictionary<FusionMethod, double> FusionMethodPerformance { get; init; } = new();

    /// <summary>
    /// 평균 검색 시간 (밀리초)
    /// </summary>
    public double AverageSearchTimeMs { get; init; }

    /// <summary>
    /// 벡터 vs 키워드 기여도
    /// </summary>
    public (double VectorContribution, double SparseContribution) ContributionRatio { get; init; }
}

/// <summary>
/// 쿼리 특성
/// </summary>
public class QueryCharacteristics
{
    /// <summary>
    /// 쿼리 길이 (토큰 수)
    /// </summary>
    public int Length { get; init; }

    /// <summary>
    /// 키워드 유형 (Boolean, Natural, Phrase)
    /// </summary>
    public QueryType Type { get; init; }

    /// <summary>
    /// 복잡도 점수 (0.0 - 1.0)
    /// </summary>
    public double Complexity { get; init; }

    /// <summary>
    /// 개체명 포함 여부
    /// </summary>
    public bool ContainsNamedEntities { get; init; }

    /// <summary>
    /// 전문 용어 포함 여부
    /// </summary>
    public bool ContainsTechnicalTerms { get; init; }

    /// <summary>
    /// 감정 극성
    /// </summary>
    public SentimentPolarity Sentiment { get; init; }
}

/// <summary>
/// 융합 방법
/// </summary>
public enum FusionMethod
{
    /// <summary>
    /// Reciprocal Rank Fusion (기본값)
    /// </summary>
    RRF,

    /// <summary>
    /// 가중 선형 조합
    /// </summary>
    WeightedSum,

    /// <summary>
    /// 곱셈 융합
    /// </summary>
    Product,

    /// <summary>
    /// 최대값 융합
    /// </summary>
    Maximum,

    /// <summary>
    /// 조화 평균
    /// </summary>
    HarmonicMean,

    /// <summary>
    /// 학습된 융합 (향후 확장)
    /// </summary>
    Learned
}

/// <summary>
/// 검색 소스
/// </summary>
public enum SearchSource
{
    /// <summary>
    /// 벡터 검색만
    /// </summary>
    Vector,

    /// <summary>
    /// 키워드 검색만
    /// </summary>
    Sparse,

    /// <summary>
    /// 양쪽 모두
    /// </summary>
    Both
}

/// <summary>
/// 검색 전략 유형
/// </summary>
public enum SearchStrategyType
{
    /// <summary>
    /// 벡터 우선
    /// </summary>
    VectorFirst,

    /// <summary>
    /// 키워드 우선
    /// </summary>
    SparseFirst,

    /// <summary>
    /// 균형 융합
    /// </summary>
    Balanced,

    /// <summary>
    /// 적응형
    /// </summary>
    Adaptive
}

/// <summary>
/// 감정 극성
/// </summary>
public enum SentimentPolarity
{
    /// <summary>
    /// 중립
    /// </summary>
    Neutral,

    /// <summary>
    /// 긍정
    /// </summary>
    Positive,

    /// <summary>
    /// 부정
    /// </summary>
    Negative
}