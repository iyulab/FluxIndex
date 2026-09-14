namespace FluxIndex.Core.Domain.Models;

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

