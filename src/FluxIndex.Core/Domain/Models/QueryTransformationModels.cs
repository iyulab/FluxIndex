using System;
using System.Collections.Generic;

namespace FluxIndex.Core.Domain.Models;

/// <summary>
/// 쿼리 분해 결과
/// </summary>
public class QueryDecompositionResult
{
    /// <summary>
    /// 원본 쿼리
    /// </summary>
    public string OriginalQuery { get; set; } = string.Empty;

    /// <summary>
    /// 분해된 하위 쿼리들
    /// </summary>
    public IReadOnlyList<string> SubQueries { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 신뢰도 (0-1)
    /// </summary>
    public float Confidence { get; set; }

    /// <summary>
    /// 쿼리 관계 타입
    /// </summary>
    public QueryRelationshipType RelationshipType { get; set; } = QueryRelationshipType.Sequential;
}

/// <summary>
/// 쿼리 관계 타입
/// </summary>
public enum QueryRelationshipType
{
    /// <summary>
    /// 순차적 관계
    /// </summary>
    Sequential,

    /// <summary>
    /// 병렬 관계
    /// </summary>
    Parallel,

    /// <summary>
    /// 계층적 관계
    /// </summary>
    Hierarchical,

    /// <summary>
    /// 조건부 관계
    /// </summary>
    Conditional
}

/// <summary>
/// 쿼리 타입
/// </summary>
public enum QueryType
{
    /// <summary>
    /// 정보 검색
    /// </summary>
    Informational,

    /// <summary>
    /// 방법 설명
    /// </summary>
    Procedural,

    /// <summary>
    /// 문제 해결
    /// </summary>
    Troubleshooting,

    /// <summary>
    /// 비교 분석
    /// </summary>
    Comparative,

    /// <summary>
    /// 의견/평가
    /// </summary>
    Evaluative,

    /// <summary>
    /// 구문 검색
    /// </summary>
    Phrase,

    /// <summary>
    /// 불리언 검색
    /// </summary>
    Boolean,

    /// <summary>
    /// 키워드 검색
    /// </summary>
    Keyword,

    /// <summary>
    /// 자연어 검색
    /// </summary>
    Natural
}

/// <summary>
/// 쿼리 의도
/// </summary>
public enum QueryIntent
{
    /// <summary>
    /// 학습/교육
    /// </summary>
    Learning,

    /// <summary>
    /// 문제 해결
    /// </summary>
    ProblemSolving,

    /// <summary>
    /// 연구/탐색
    /// </summary>
    Research,

    /// <summary>
    /// 결정 지원
    /// </summary>
    DecisionSupport,

    /// <summary>
    /// 참조/확인
    /// </summary>
    Reference
}
