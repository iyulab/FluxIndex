using System;
using System.Collections.Generic;
using System.Linq;
using DocumentChunk = FluxIndex.Core.Domain.Models.DocumentChunk;

namespace FluxIndex.SDK.Models;

/// <summary>
/// Small-to-Big 검색 결과 (SDK용)
/// </summary>
public class SmallToBigSearchResult
{
    /// <summary>
    /// 매칭된 핵심 청크 (Small - 정밀 매칭)
    /// </summary>
    public DocumentChunk PrimaryChunk { get; set; } = new();

    /// <summary>
    /// 확장된 컨텍스트 청크들 (Big - 배경 정보)
    /// </summary>
    public List<DocumentChunk> ContextChunks { get; set; } = new();

    /// <summary>
    /// 전체 결과의 관련성 점수
    /// </summary>
    public double RelevanceScore { get; set; }

    /// <summary>
    /// 사용된 윈도우 크기
    /// </summary>
    public int WindowSize { get; set; }

    /// <summary>
    /// 컨텍스트 확장 근거
    /// </summary>
    public string ExpansionReason { get; set; } = string.Empty;

    /// <summary>
    /// 컨텍스트 품질 점수
    /// </summary>
    public double ContextQuality { get; set; }

    /// <summary>
    /// 전체 텍스트 (Primary + Context 결합)
    /// </summary>
    public string CombinedText { get; set; } = string.Empty;
}

/// <summary>
/// Small-to-Big 검색 옵션 (SDK용)
/// </summary>
public class SmallToBigSearchOptions
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
    /// 기본 윈도우 크기
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
    /// 중복 제거 임계값
    /// </summary>
    public double DeduplicationThreshold { get; set; } = 0.9;

    /// <summary>
    /// 검색 타임아웃 (밀리초)
    /// </summary>
    public int TimeoutMs { get; set; } = 30000;

    /// <summary>
    /// Core 옵션으로 변환
    /// </summary>
    internal Core.Domain.Models.SmallToBigOptions ToCoreOptions()
    {
        return new Core.Domain.Models.SmallToBigOptions
        {
            MaxResults = MaxResults,
            MinRelevanceScore = MinRelevanceScore,
            DefaultWindowSize = DefaultWindowSize,
            MaxWindowSize = MaxWindowSize,
            EnableAdaptiveWindowing = EnableAdaptiveWindowing,
            EnableSemanticExpansion = EnableSemanticExpansion,
            EnableHierarchicalExpansion = EnableHierarchicalExpansion,
            EnableSequentialExpansion = EnableSequentialExpansion,
            ContextQualityThreshold = ContextQualityThreshold,
            DeduplicationThreshold = DeduplicationThreshold,
            TimeoutMs = TimeoutMs
        };
    }
}

/// <summary>
/// 쿼리 복잡도 분석 결과 (SDK용)
/// </summary>
public class QueryComplexityResult
{
    /// <summary>
    /// 전체 복잡도 점수 (0.0 - 1.0)
    /// </summary>
    public double OverallComplexity { get; set; }

    /// <summary>
    /// 어휘적 복잡도
    /// </summary>
    public double LexicalComplexity { get; set; }

    /// <summary>
    /// 구문적 복잡도
    /// </summary>
    public double SyntacticComplexity { get; set; }

    /// <summary>
    /// 의미적 복잡도
    /// </summary>
    public double SemanticComplexity { get; set; }

    /// <summary>
    /// 추론 복잡도
    /// </summary>
    public double ReasoningComplexity { get; set; }

    /// <summary>
    /// 권장 윈도우 크기
    /// </summary>
    public int RecommendedWindowSize { get; set; }

    /// <summary>
    /// 분석 신뢰도
    /// </summary>
    public double AnalysisConfidence { get; set; }

    /// <summary>
    /// 복잡도 분류
    /// </summary>
    public string ComplexityLevel =>
        OverallComplexity switch
        {
            <= 0.2 => "매우 간단",
            <= 0.4 => "간단",
            <= 0.6 => "보통",
            <= 0.8 => "복잡",
            _ => "매우 복잡"
        };

    /// <summary>
    /// 권장 검색 전략
    /// </summary>
    public string RecommendedStrategy =>
        OverallComplexity switch
        {
            <= 0.3 => "Conservative",
            <= 0.7 => "Balanced",
            _ => "Aggressive"
        };
}