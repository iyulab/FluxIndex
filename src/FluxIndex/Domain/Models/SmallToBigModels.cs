using System;
using System.Collections.Generic;

namespace FluxIndex.Domain.Models;

/// <summary>
/// Small-to-Big 검색 결과
/// </summary>
public class SmallToBigResult
{
    /// <summary>
    /// 매칭된 핵심 청크 (Small - 정밀 매칭)
    /// </summary>
    public DocumentChunk PrimaryChunk { get; init; } = new();

    /// <summary>
    /// 확장된 컨텍스트 청크들 (Big - 배경 정보)
    /// </summary>
    public List<DocumentChunk> ContextChunks { get; init; } = new();

    /// <summary>
    /// 전체 결과의 관련성 점수
    /// </summary>
    public double RelevanceScore { get; init; }

    /// <summary>
    /// 사용된 윈도우 크기
    /// </summary>
    public int WindowSize { get; init; }

    /// <summary>
    /// 컨텍스트 확장 근거
    /// </summary>
    public string ExpansionReason { get; init; } = string.Empty;

    /// <summary>
    /// 확장 전략 정보
    /// </summary>
    public ExpansionStrategy Strategy { get; init; } = new();

    /// <summary>
    /// 검색 메타데이터
    /// </summary>
    public SmallToBigMetadata Metadata { get; init; } = new();

    /// <summary>
    /// 전체 텍스트 (Primary + Context 결합)
    /// </summary>
    public string CombinedText =>
        $"{PrimaryChunk.Content}\n\n--- 관련 컨텍스트 ---\n{string.Join("\n\n", ContextChunks.Select(c => c.Content))}";

    /// <summary>
    /// 컨텍스트 품질 점수
    /// </summary>
    public double ContextQuality => Metadata.ContextQualityScore;
}

/// <summary>
/// Small-to-Big 검색 옵션
/// </summary>
public class SmallToBigOptions
{
    /// <summary>
    /// 최대 결과 수
    /// </summary>
    public int MaxResults { get; set; } = 10;

    /// <summary>
    /// 최소 관련성 점수
    /// </summary>
    public double MinRelevanceScore { get; set; } = 0.1;

    /// <summary>
    /// 기본 윈도우 크기 (자동 결정 시 기본값)
    /// </summary>
    public int DefaultWindowSize { get; set; } = 3;

    /// <summary>
    /// 최대 윈도우 크기
    /// </summary>
    public int MaxWindowSize { get; set; } = 10;

    /// <summary>
    /// 적응형 윈도우 크기 사용 여부
    /// </summary>
    public bool EnableAdaptiveWindowing { get; set; } = true;

    /// <summary>
    /// 의미적 확장 사용 여부
    /// </summary>
    public bool EnableSemanticExpansion { get; set; } = true;

    /// <summary>
    /// 계층적 확장 사용 여부
    /// </summary>
    public bool EnableHierarchicalExpansion { get; set; } = true;

    /// <summary>
    /// 순차적 확장 사용 여부
    /// </summary>
    public bool EnableSequentialExpansion { get; set; } = true;

    /// <summary>
    /// 컨텍스트 품질 필터링 임계값
    /// </summary>
    public double ContextQualityThreshold { get; set; } = 0.5;

    /// <summary>
    /// 중복 제거 임계값 (유사도)
    /// </summary>
    public double DeduplicationThreshold { get; set; } = 0.9;

    /// <summary>
    /// 검색 타임아웃 (밀리초)
    /// </summary>
    public int TimeoutMs { get; set; } = 30000;
}

/// <summary>
/// 확장 전략 정보
/// </summary>
public class ExpansionStrategy
{
    /// <summary>
    /// 전략 유형
    /// </summary>
    public ExpansionStrategyType Type { get; init; }

    /// <summary>
    /// 사용된 확장 방법들
    /// </summary>
    public List<ExpansionMethod> Methods { get; init; } = new();

    /// <summary>
    /// 전략 신뢰도
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// 전략 선택 근거
    /// </summary>
    public string Reasoning { get; init; } = string.Empty;

    /// <summary>
    /// 예상 효과
    /// </summary>
    public ExpectedOutcome ExpectedOutcome { get; init; } = new();
}

/// <summary>
/// Small-to-Big 메타데이터
/// </summary>
public class SmallToBigMetadata
{
    /// <summary>
    /// 검색 실행 시간 (밀리초)
    /// </summary>
    public double SearchTimeMs { get; init; }

    /// <summary>
    /// 컨텍스트 품질 점수
    /// </summary>
    public double ContextQualityScore { get; init; }

    /// <summary>
    /// 확장 효율성 점수
    /// </summary>
    public double ExpansionEfficiency { get; init; }

    /// <summary>
    /// 각 확장 방법별 기여도
    /// </summary>
    public Dictionary<ExpansionMethod, double> MethodContributions { get; init; } = new();

    /// <summary>
    /// 컨텍스트 다양성 점수
    /// </summary>
    public double ContextDiversity { get; init; }

    /// <summary>
    /// 정보 중복도
    /// </summary>
    public double InformationRedundancy { get; init; }

    /// <summary>
    /// 추가 메타데이터
    /// </summary>
    public Dictionary<string, object> Properties { get; init; } = new();
}

/// <summary>
/// 쿼리 복잡도 분석 결과
/// </summary>
public class QueryComplexityAnalysis
{
    /// <summary>
    /// 전체 복잡도 점수 (0.0 - 1.0)
    /// </summary>
    public double OverallComplexity { get; init; }

    /// <summary>
    /// 어휘적 복잡도
    /// </summary>
    public double LexicalComplexity { get; init; }

    /// <summary>
    /// 구문적 복잡도
    /// </summary>
    public double SyntacticComplexity { get; init; }

    /// <summary>
    /// 의미적 복잡도
    /// </summary>
    public double SemanticComplexity { get; init; }

    /// <summary>
    /// 추론 복잡도
    /// </summary>
    public double ReasoningComplexity { get; init; }

    /// <summary>
    /// 권장 윈도우 크기
    /// </summary>
    public int RecommendedWindowSize { get; init; }

    /// <summary>
    /// 분석 신뢰도
    /// </summary>
    public double AnalysisConfidence { get; init; }

    /// <summary>
    /// 복잡도 구성요소
    /// </summary>
    public ComplexityComponents Components { get; init; } = new();
}

/// <summary>
/// 복잡도 구성요소
/// </summary>
public class ComplexityComponents
{
    /// <summary>
    /// 토큰 수
    /// </summary>
    public int TokenCount { get; init; }

    /// <summary>
    /// 고유 단어 수
    /// </summary>
    public int UniqueWordCount { get; init; }

    /// <summary>
    /// 평균 단어 길이
    /// </summary>
    public double AverageWordLength { get; init; }

    /// <summary>
    /// 구두점 밀도
    /// </summary>
    public double PunctuationDensity { get; init; }

    /// <summary>
    /// 전문 용어 개수
    /// </summary>
    public int TechnicalTermCount { get; init; }

    /// <summary>
    /// 개체명 개수
    /// </summary>
    public int NamedEntityCount { get; init; }

    /// <summary>
    /// 의문사 개수
    /// </summary>
    public int QuestionWordCount { get; init; }

    /// <summary>
    /// 논리 연산자 개수
    /// </summary>
    public int LogicalOperatorCount { get; init; }
}

/// <summary>
/// 예상 결과
/// </summary>
public class ExpectedOutcome
{
    /// <summary>
    /// 예상 정밀도 향상
    /// </summary>
    public double ExpectedPrecisionGain { get; init; }

    /// <summary>
    /// 예상 컨텍스트 풍부함
    /// </summary>
    public double ExpectedContextRichness { get; init; }

    /// <summary>
    /// 예상 응답 시간 증가
    /// </summary>
    public double ExpectedLatencyIncrease { get; init; }

    /// <summary>
    /// 예상 사용자 만족도
    /// </summary>
    public double ExpectedUserSatisfaction { get; init; }
}

/// <summary>
/// 확장 전략 유형
/// </summary>
public enum ExpansionStrategyType
{
    /// <summary>
    /// 보수적 확장 (최소한의 컨텍스트)
    /// </summary>
    Conservative,

    /// <summary>
    /// 균형 확장 (적절한 컨텍스트)
    /// </summary>
    Balanced,

    /// <summary>
    /// 적극적 확장 (풍부한 컨텍스트)
    /// </summary>
    Aggressive,

    /// <summary>
    /// 적응형 확장 (쿼리에 따라 동적 조정)
    /// </summary>
    Adaptive
}

/// <summary>
/// 확장 방법
/// </summary>
public enum ExpansionMethod
{
    /// <summary>
    /// 계층적 확장 (부모-자식 관계)
    /// </summary>
    Hierarchical,

    /// <summary>
    /// 순차적 확장 (인접 청크)
    /// </summary>
    Sequential,

    /// <summary>
    /// 의미적 확장 (유사한 주제)
    /// </summary>
    Semantic,

    /// <summary>
    /// 참조 확장 (명시적 참조)
    /// </summary>
    Reference,

    /// <summary>
    /// 키워드 확장 (공통 키워드)
    /// </summary>
    Keyword,

    /// <summary>
    /// 엔터티 확장 (공통 엔터티)
    /// </summary>
    Entity
}