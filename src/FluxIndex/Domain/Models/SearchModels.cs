using System;
using System.Collections.Generic;

namespace FluxIndex.Domain.Models;

/// <summary>
/// 벡터 검색 결과
/// </summary>
public class VectorSearchResult
{
    /// <summary>
    /// 문서 청크
    /// </summary>
    public DocumentChunk DocumentChunk { get; init; } = new();

    /// <summary>
    /// 유사도 점수 (0.0 ~ 1.0)
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// 검색 순위
    /// </summary>
    public int Rank { get; init; }

    /// <summary>
    /// 벡터 거리 (코사인 거리 등)
    /// </summary>
    public double Distance { get; init; }

    /// <summary>
    /// 검색 메타데이터
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// 희소(키워드) 검색 결과
/// </summary>
public record SparseSearchResult
{
    /// <summary>
    /// 문서 청크
    /// </summary>
    public DocumentChunk Chunk { get; init; } = new();

    /// <summary>
    /// BM25 점수
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// 매칭된 용어들
    /// </summary>
    public IReadOnlyList<string> MatchedTerms { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 용어별 빈도수
    /// </summary>
    public Dictionary<string, int> TermFrequencies { get; init; } = new();

    /// <summary>
    /// 문서 길이
    /// </summary>
    public int DocumentLength { get; init; }

    /// <summary>
    /// BM25 점수 구성요소
    /// </summary>
    public BM25Components? ScoreComponents { get; init; }
}

/// <summary>
/// BM25 점수 구성요소
/// </summary>
public class BM25Components
{
    /// <summary>
    /// 용어 빈도 점수
    /// </summary>
    public double TermFrequencyScore { get; init; }

    /// <summary>
    /// 역문서 빈도 점수
    /// </summary>
    public double InverseDocumentFrequencyScore { get; init; }

    /// <summary>
    /// 문서 길이 정규화
    /// </summary>
    public double DocumentLengthNormalization { get; init; }

    /// <summary>
    /// 최종 점수
    /// </summary>
    public double FinalScore { get; init; }

    /// <summary>
    /// 용어별 점수
    /// </summary>
    public Dictionary<string, double> TermScores { get; init; } = new();
}

/// <summary>
/// 희소 검색 옵션
/// </summary>
public class SparseSearchOptions
{
    /// <summary>
    /// 최대 결과 수
    /// </summary>
    public int MaxResults { get; set; } = 10;

    /// <summary>
    /// 최소 점수
    /// </summary>
    public double MinScore { get; set; } = 0.0;

    /// <summary>
    /// BM25 k1 매개변수
    /// </summary>
    public double K1 { get; set; } = 1.2;

    /// <summary>
    /// BM25 b 매개변수
    /// </summary>
    public double B { get; set; } = 0.75;

    /// <summary>
    /// 용어 확장 활성화
    /// </summary>
    public bool EnableTermExpansion { get; set; } = false;

    /// <summary>
    /// 구문 검색 활성화
    /// </summary>
    public bool EnablePhraseSearch { get; set; } = false;
}

/// <summary>
/// 희소 인덱스 통계
/// </summary>
public class SparseIndexStatistics
{
    /// <summary>
    /// 인덱싱된 문서 수
    /// </summary>
    public long DocumentCount { get; init; }

    /// <summary>
    /// 고유 용어 수
    /// </summary>
    public int UniqueTermCount { get; init; }

    /// <summary>
    /// 총 용어 출현 횟수
    /// </summary>
    public long TotalTermOccurrences { get; init; }

    /// <summary>
    /// 평균 문서 길이
    /// </summary>
    public double AverageDocumentLength { get; init; }

    /// <summary>
    /// 인덱스 크기 (바이트)
    /// </summary>
    public long IndexSizeBytes { get; init; }

    /// <summary>
    /// 마지막 최적화 시간
    /// </summary>
    public DateTime LastOptimizedAt { get; init; }

    /// <summary>
    /// 상위 빈도 용어
    /// </summary>
    public Dictionary<string, long> TopFrequentTerms { get; init; } = new();
}

/// <summary>
/// 벡터 검색 옵션
/// </summary>
public class VectorSearchOptions
{
    /// <summary>
    /// 최대 결과 수
    /// </summary>
    public int MaxResults { get; set; } = 10;

    /// <summary>
    /// 최소 점수
    /// </summary>
    public double MinScore { get; set; } = 0.0;

    /// <summary>
    /// 유사도 메트릭
    /// </summary>
    public string SimilarityMetric { get; set; } = "cosine";

    /// <summary>
    /// 필터 조건
    /// </summary>
    public Dictionary<string, object> Filters { get; set; } = new();

    /// <summary>
    /// 부울 연산자 (AND, OR)
    /// </summary>
    public BooleanOperator BooleanOperator { get; set; } = BooleanOperator.OR;

    /// <summary>
    /// 용어 확장 사용 여부 (스테밍, 동의어)
    /// </summary>
    public bool EnableTermExpansion { get; set; } = true;

    /// <summary>
    /// 구문 검색 사용 여부
    /// </summary>
    public bool EnablePhraseSearch { get; set; } = true;
}


/// <summary>
/// 부울 연산자
/// </summary>
public enum BooleanOperator
{
    /// <summary>
    /// OR 연산 (기본값)
    /// </summary>
    OR,

    /// <summary>
    /// AND 연산
    /// </summary>
    AND
}