using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Services;

/// <summary>
/// 쿼리 복잡도 분석기 구현체
/// </summary>
public class QueryComplexityAnalyzer : IQueryComplexityAnalyzer
{
    private readonly ILogger<QueryComplexityAnalyzer> _logger;

    // 기술 용어 패턴
    private static readonly HashSet<string> TechnicalTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "API", "HTTP", "JSON", "SQL", "AI", "ML", "CNN", "RNN", "LSTM", "GPU", "CPU",
        "알고리즘", "데이터베이스", "프레임워크", "라이브러리", "인터페이스",
        "machine learning", "deep learning", "neural network", "transformer"
    };

    // 질문 단어
    private static readonly HashSet<string> QuestionWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "what", "who", "when", "where", "why", "how", "which", "whose",
        "무엇", "누구", "언제", "어디", "왜", "어떻게", "무슨", "어느"
    };

    // 비교 단어
    private static readonly HashSet<string> ComparisonWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "compare", "versus", "vs", "difference", "similar", "different", "better", "worse",
        "비교", "차이", "유사", "다른", "더", "덜", "보다"
    };

    // 시간적 단어
    private static readonly HashSet<string> TemporalWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "when", "before", "after", "during", "recent", "latest", "future", "past",
        "언제", "전", "후", "동안", "최근", "최신", "미래", "과거"
    };

    // 논리 연산자
    private static readonly HashSet<string> LogicalOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "or", "not", "but", "however", "therefore", "because",
        "그리고", "또는", "하지만", "그러나", "따라서", "때문에"
    };

    public QueryComplexityAnalyzer(ILogger<QueryComplexityAnalyzer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 쿼리 복잡도 분석
    /// </summary>
    public async Task<QueryAnalysis> AnalyzeAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new QueryAnalysis
            {
                Type = QueryType.SimpleKeyword,
                Complexity = ComplexityLevel.Simple,
                ConfidenceScore = 1.0
            };
        }

        await Task.CompletedTask; // 비동기 인터페이스 준수

        var tokens = TokenizeQuery(query);
        var analysis = new QueryAnalysis
        {
            Type = DetermineQueryType(query, tokens),
            Complexity = DetermineComplexityLevel(query, tokens),
            Specificity = CalculateSpecificity(tokens),
            Entities = ExtractEntities(tokens),
            Concepts = ExtractConcepts(tokens),
            Keywords = ExtractKeywords(tokens),
            Intent = DetermineIntent(query, tokens),
            Language = DetectLanguage(query),
            RequiresReasoning = RequiresReasoning(query, tokens),
            HasTemporalContext = HasTemporalContext(tokens),
            HasComparativeContext = HasComparativeContext(tokens),
            IsMultiHop = IsMultiHop(query, tokens),
            EstimatedProcessingTime = EstimateProcessingTime(query, tokens),
            ConfidenceScore = CalculateConfidenceScore(query, tokens),
            Metadata = new Dictionary<string, object>
            {
                ["token_count"] = tokens.Length,
                ["char_count"] = query.Length,
                ["question_words"] = tokens.Count(t => QuestionWords.Contains(t)),
                ["technical_terms"] = tokens.Count(t => TechnicalTerms.Contains(t)),
                ["analyzed_at"] = DateTime.UtcNow
            }
        };

        _logger.LogDebug("쿼리 분석 완료: {Query} → {Type}, {Complexity}", query, analysis.Type, analysis.Complexity);

        return analysis;
    }

    /// <summary>
    /// 분석 결과 기반 검색 전략 추천
    /// </summary>
    public SearchStrategy RecommendStrategy(QueryAnalysis analysis)
    {
        // 복잡도와 쿼리 유형에 따른 전략 추천
        return analysis.Complexity switch
        {
            ComplexityLevel.Simple => RecommendSimpleStrategy(analysis),
            ComplexityLevel.Moderate => RecommendModerateStrategy(analysis),
            ComplexityLevel.Complex => RecommendComplexStrategy(analysis),
            ComplexityLevel.VeryComplex => RecommendVeryComplexStrategy(analysis),
            _ => SearchStrategy.Hybrid
        };
    }

    /// <summary>
    /// 쿼리 유형별 성능 통계 업데이트
    /// </summary>
    public async Task UpdatePerformanceAsync(
        string query,
        QueryAnalysis analysis,
        QueryAnalysisResult result,
        CancellationToken cancellationToken = default)
    {
        // 실제 구현에서는 성능 데이터베이스에 저장
        _logger.LogDebug("성능 통계 업데이트: {QueryType}, 결과 수: {ResultCount}",
            analysis.Type, result.ResultCount);

        await Task.CompletedTask;
    }

    #region Private Methods

    private string[] TokenizeQuery(string query)
    {
        // 단순한 토크나이징: 공백 및 구두점으로 분리
        return Regex.Split(query.ToLowerInvariant(), @"\s+|[.,;:!?()""']")
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToArray();
    }

    private QueryType DetermineQueryType(string query, string[] tokens)
    {
        // 질문 단어가 있으면 자연어 질문
        if (tokens.Any(t => QuestionWords.Contains(t)))
        {
            if (IsMultiHop(query, tokens))
                return QueryType.MultiHopQuery;

            if (HasComparativeContext(tokens))
                return QueryType.ComparisonQuery;

            if (HasTemporalContext(tokens))
                return QueryType.TemporalQuery;

            if (RequiresReasoning(query, tokens))
                return QueryType.ReasoningQuery;

            return QueryType.NaturalQuestion;
        }

        // 논리 연산자나 복합 조건이 있으면 복합 검색
        if (tokens.Any(t => LogicalOperators.Contains(t)) || query.Contains("AND") || query.Contains("OR"))
        {
            return QueryType.ComplexSearch;
        }

        // 비교 단어가 있으면 비교 쿼리
        if (HasComparativeContext(tokens))
        {
            return QueryType.ComparisonQuery;
        }

        // 추론이 필요한 패턴
        if (RequiresReasoning(query, tokens))
        {
            return QueryType.ReasoningQuery;
        }

        // 기본적으로 단순 키워드
        return QueryType.SimpleKeyword;
    }

    private ComplexityLevel DetermineComplexityLevel(string query, string[] tokens)
    {
        var complexityScore = 0;

        // 토큰 수에 따른 복잡도
        if (tokens.Length > 10) complexityScore += 2;
        else if (tokens.Length > 5) complexityScore += 1;

        // 질문 단어 개수
        var questionWordCount = tokens.Count(t => QuestionWords.Contains(t));
        if (questionWordCount > 1) complexityScore += 2;
        else if (questionWordCount > 0) complexityScore += 1;

        // 기술 용어 개수 - 구문과 개별 토큰 모두 확인
        var technicalTermCount = tokens.Count(t => TechnicalTerms.Contains(t));

        // 복합 기술 용어 확인 (예: "machine learning")
        foreach (var term in TechnicalTerms)
        {
            if (term.Contains(' ') && query.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                technicalTermCount++;
            }
        }

        if (technicalTermCount > 2) complexityScore += 3;
        else if (technicalTermCount > 1) complexityScore += 2;
        else if (technicalTermCount > 0) complexityScore += 1;

        // "detailed", "explanation" 같은 상세한 설명을 요구하는 단어들
        var detailedWords = new[] { "detailed", "explanation", "analyze", "comprehensive", "thorough" };
        if (tokens.Any(t => detailedWords.Contains(t, StringComparer.OrdinalIgnoreCase))) complexityScore += 2;

        // 논리 연산자
        if (tokens.Any(t => LogicalOperators.Contains(t))) complexityScore += 1;

        // 특수 패턴들
        if (HasComparativeContext(tokens)) complexityScore += 1;
        if (HasTemporalContext(tokens)) complexityScore += 1;
        if (IsMultiHop(query, tokens)) complexityScore += 2;
        if (RequiresReasoning(query, tokens)) complexityScore += 2;

        return complexityScore switch
        {
            <= 1 => ComplexityLevel.Simple,
            <= 3 => ComplexityLevel.Moderate,
            <= 5 => ComplexityLevel.Complex,
            _ => ComplexityLevel.VeryComplex
        };
    }

    private double CalculateSpecificity(string[] tokens)
    {
        // 고유 토큰 비율과 기술 용어 비율로 특정성 계산
        var uniqueTokens = tokens.Distinct().Count();
        var totalTokens = tokens.Length;
        var technicalTerms = tokens.Count(t => TechnicalTerms.Contains(t));

        var uniquenessRatio = totalTokens > 0 ? (double)uniqueTokens / totalTokens : 0.0;
        var technicalRatio = totalTokens > 0 ? (double)technicalTerms / totalTokens : 0.0;

        return Math.Min(1.0, (uniquenessRatio + technicalRatio) / 2.0);
    }

    private List<string> ExtractEntities(string[] tokens)
    {
        // 대문자로 시작하는 단어를 개체명으로 간주
        return tokens.Where(t => t.Length > 1 && char.IsUpper(t[0])).ToList();
    }

    private List<string> ExtractConcepts(string[] tokens)
    {
        // 기술 용어와 긴 단어들을 개념으로 간주
        return tokens.Where(t => TechnicalTerms.Contains(t) || t.Length > 6).ToList();
    }

    private List<string> ExtractKeywords(string[] tokens)
    {
        // 불용어 제외한 의미 있는 키워드들
        var stopWords = new HashSet<string> { "the", "a", "an", "is", "are", "was", "were", "을", "를", "이", "가", "은", "는" };
        return tokens.Where(t => !stopWords.Contains(t) && t.Length > 2).ToList();
    }

    private QueryIntent DetermineIntent(string query, string[] tokens)
    {
        // "how to", "what is" → 정보적
        if (tokens.Any(t => QuestionWords.Contains(t)))
            return QueryIntent.Informational;

        // "compare", "analyze" → 분석적
        if (HasComparativeContext(tokens) || tokens.Any(t => t.Contains("analyz")))
            return QueryIntent.Analytical;

        // "find", "search" → 탐색적
        if (tokens.Any(t => t.Contains("find") || t.Contains("search")))
            return QueryIntent.Exploratory;

        return QueryIntent.Informational;
    }

    private Language DetectLanguage(string query)
    {
        var koreanPattern = @"[가-힣]";
        var englishPattern = @"[a-zA-Z]";

        var hasKorean = Regex.IsMatch(query, koreanPattern);
        var hasEnglish = Regex.IsMatch(query, englishPattern);

        if (hasKorean && hasEnglish) return Language.Mixed;
        if (hasKorean) return Language.Korean;
        if (hasEnglish) return Language.English;

        return Language.Other;
    }

    private bool RequiresReasoning(string query, string[] tokens)
    {
        var reasoningPatterns = new[]
        {
            "why", "how does", "how do", "explain", "reason", "because", "cause", "effective",
            "왜", "어떻게", "설명", "이유", "때문"
        };

        return reasoningPatterns.Any(pattern => query.Contains(pattern, StringComparison.OrdinalIgnoreCase)) ||
               tokens.Any(t => new[] { "why", "how", "explain", "reason", "effective", "because" }.Contains(t, StringComparer.OrdinalIgnoreCase));
    }

    private bool HasTemporalContext(string[] tokens)
    {
        return tokens.Any(t => TemporalWords.Contains(t));
    }

    private bool HasComparativeContext(string[] tokens)
    {
        return tokens.Any(t => ComparisonWords.Contains(t));
    }

    private bool IsMultiHop(string query, string[] tokens)
    {
        // "and then", "after that", "다음에" 등의 패턴
        var multiHopPatterns = new[]
        {
            "and then", "after that", "다음에", "그리고", "또한"
        };

        return multiHopPatterns.Any(pattern => query.Contains(pattern, StringComparison.OrdinalIgnoreCase)) ||
               tokens.Count(t => LogicalOperators.Contains(t)) > 1;
    }

    private TimeSpan EstimateProcessingTime(string query, string[] tokens)
    {
        var baseTimeMs = 500; // 기본 500ms

        // 토큰 수에 따른 추가 시간
        baseTimeMs += tokens.Length * 10;

        // 복잡도에 따른 추가 시간
        if (RequiresReasoning(query, tokens)) baseTimeMs += 1000;
        if (IsMultiHop(query, tokens)) baseTimeMs += 1500;
        if (HasComparativeContext(tokens)) baseTimeMs += 500;

        return TimeSpan.FromMilliseconds(Math.Min(baseTimeMs, 10000)); // 최대 10초
    }

    private double CalculateConfidenceScore(string query, string[] tokens)
    {
        var confidence = 0.5; // 기본 신뢰도

        // 토큰 수가 적절하면 신뢰도 증가
        if (tokens.Length >= 2 && tokens.Length <= 15) confidence += 0.2;

        // 명확한 패턴이 있으면 신뢰도 증가
        if (tokens.Any(t => QuestionWords.Contains(t))) confidence += 0.1;
        if (tokens.Any(t => TechnicalTerms.Contains(t))) confidence += 0.1;

        // 너무 짧거나 길면 신뢰도 감소
        if (tokens.Length < 2) confidence -= 0.2;
        if (tokens.Length > 20) confidence -= 0.1;

        return Math.Max(0.1, Math.Min(1.0, confidence));
    }

    private SearchStrategy RecommendSimpleStrategy(QueryAnalysis analysis)
    {
        // 단순한 쿼리는 벡터 검색이 더 효과적
        return SearchStrategy.DirectVector;
    }

    private SearchStrategy RecommendModerateStrategy(QueryAnalysis analysis)
    {
        // 보통 복잡도는 하이브리드 검색이 최적
        return analysis.HasComparativeContext
            ? SearchStrategy.MultiQuery
            : SearchStrategy.Hybrid;
    }

    private SearchStrategy RecommendComplexStrategy(QueryAnalysis analysis)
    {
        if (analysis.RequiresReasoning)
            return SearchStrategy.TwoStage;

        if (analysis.IsMultiHop)
            return SearchStrategy.MultiQuery;

        return SearchStrategy.Hybrid;
    }

    private SearchStrategy RecommendVeryComplexStrategy(QueryAnalysis analysis)
    {
        if (analysis.IsMultiHop && analysis.RequiresReasoning)
            return SearchStrategy.SelfRAG;

        if (analysis.RequiresReasoning)
            return SearchStrategy.TwoStage;

        return SearchStrategy.Adaptive;
    }

    #endregion
}