using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Models;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using TokenMeter.Abstractions;

// Use types from Models namespace for token-aware search
using QueryAnalysis = FluxIndex.Core.Application.Models.QueryAnalysis;
using QueryIntent = FluxIndex.Core.Application.Models.QueryIntent;
using SearchStrategy = FluxIndex.Core.Application.Models.SearchStrategy;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// 질의 분석 서비스
/// </summary>
public partial class QueryAnalysisService : IQueryAnalysisService
{
    private readonly ITokenCounter _tokenCounter;
    private readonly ILogger<QueryAnalysisService> _logger;

    // 의도 패턴
    private static readonly Dictionary<QueryIntent, Regex[]> IntentPatterns = new()
    {
        [QueryIntent.HowTo] = new[]
        {
            new Regex(@"^how\s+(to|do|can|should)", RegexOptions.IgnoreCase),
            new Regex(@"방법|어떻게|하는\s*법", RegexOptions.IgnoreCase)
        },
        [QueryIntent.Comparison] = new[]
        {
            new Regex(@"\bvs\.?\b|\bversus\b|비교|차이", RegexOptions.IgnoreCase),
            new Regex(@"better|worse|difference|compared", RegexOptions.IgnoreCase)
        },
        [QueryIntent.Definition] = new[]
        {
            new Regex(@"^what\s+is|^define|정의|뜻|의미", RegexOptions.IgnoreCase),
            new Regex(@"이란\??$|무엇", RegexOptions.IgnoreCase)
        },
        [QueryIntent.Troubleshooting] = new[]
        {
            new Regex(@"error|issue|problem|fix|solve|debug", RegexOptions.IgnoreCase),
            new Regex(@"오류|에러|문제|해결|고치", RegexOptions.IgnoreCase)
        },
        [QueryIntent.Listing] = new[]
        {
            new Regex(@"^list|종류|유형|examples?|types?\s+of", RegexOptions.IgnoreCase)
        }
    };

    // 기술 엔티티 패턴
    private static readonly string[] TechPatterns = new[]
    {
        "RAG", "LLM", "GPT", "BERT", "Transformer", "CNN", "RNN", "LSTM",
        "API", "REST", "GraphQL", "SQL", "NoSQL", "MongoDB", "PostgreSQL",
        "React", "Vue", "Angular", "Node", "Python", "Java", "C#", ".NET",
        "Docker", "Kubernetes", "AWS", "Azure", "GCP", "Redis", "Kafka"
    };

    public QueryAnalysisService(
        ITokenCounter tokenCounter,
        ILogger<QueryAnalysisService> logger)
    {
        _tokenCounter = tokenCounter;
        _logger = logger;
    }

    public Task<Models.QueryAnalysis> AnalyzeAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult(new QueryAnalysis
            {
                OriginalQuery = query,
                Complexity = QueryComplexityLevel.Simple,
                RecommendedStrategy = SearchStrategy.Vector
            });
        }

        var analysis = new QueryAnalysis
        {
            OriginalQuery = query,
            NormalizedQuery = NormalizeQuery(query),
            TokenCount = _tokenCounter.Count(query)
        };

        // 언어 감지
        (analysis.Language, analysis.LanguageConfidence) = DetectLanguage(query);

        // 의도 분석
        analysis.Intent = DetectIntent(query);

        // 복잡도 분석
        analysis.Complexity = AnalyzeComplexity(query, analysis.TokenCount);

        // 키워드 추출
        analysis.Keywords = ExtractKeywords(query);

        // 엔티티 추출
        analysis.Entities = ExtractEntities(query);

        // 전략 추천
        analysis.RecommendedStrategy = RecommendStrategy(analysis);
        analysis.RecommendedTopK = RecommendTopK(analysis);

        LogQueryAnalyzed(_logger, analysis.Language, analysis.Intent, analysis.Complexity, analysis.RecommendedStrategy);

        return Task.FromResult(analysis);
    }

    private static string NormalizeQuery(string query)
    {
        // 불필요한 공백 제거, 소문자 변환
        return Regex.Replace(query.Trim(), @"\s+", " ");
    }

    private static (string language, double confidence) DetectLanguage(string text)
    {
        // 한글 문자 비율로 언어 감지
        var koreanChars = text.Count(c => c >= 0xAC00 && c <= 0xD7A3);
        var totalChars = text.Count(c => !char.IsWhiteSpace(c));

        if (totalChars == 0)
            return ("en", 0.5);

        var koreanRatio = (double)koreanChars / totalChars;

        if (koreanRatio > 0.3)
            return ("ko", Math.Min(koreanRatio + 0.3, 1.0));

        return ("en", 1.0 - koreanRatio);
    }

    private static QueryIntent DetectIntent(string query)
    {
        foreach (var (intent, patterns) in IntentPatterns)
        {
            if (patterns.Any(p => p.IsMatch(query)))
                return intent;
        }

        return QueryIntent.Informational;
    }

    private static QueryComplexityLevel AnalyzeComplexity(string query, int tokenCount)
    {
        // 토큰 수 기반
        if (tokenCount <= 3)
            return QueryComplexityLevel.Simple;
        if (tokenCount <= 8)
            return QueryComplexityLevel.Moderate;
        if (tokenCount <= 15)
            return QueryComplexityLevel.Complex;

        return QueryComplexityLevel.VeryComplex;
    }

    private static List<string> ExtractKeywords(string query)
    {
        // 간단한 키워드 추출 (불용어 제거)
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "the", "is", "are", "was", "were", "be", "been",
            "to", "of", "in", "for", "on", "with", "at", "by", "from",
            "what", "how", "why", "when", "where", "which", "who",
            "은", "는", "이", "가", "을", "를", "의", "에", "와", "과"
        };

        var words = Regex.Split(query.ToLowerInvariant(), @"[\s\p{P}]+")
            .Where(w => w.Length > 1 && !stopWords.Contains(w))
            .Distinct()
            .ToList();

        return words;
    }

    private static List<QueryEntity> ExtractEntities(string query)
    {
        var entities = new List<QueryEntity>();

        foreach (var tech in TechPatterns)
        {
            if (Regex.IsMatch(query, $@"\b{Regex.Escape(tech)}\b", RegexOptions.IgnoreCase))
            {
                entities.Add(new QueryEntity
                {
                    Text = tech,
                    Type = EntityType.Technology,
                    Confidence = 0.9
                });
            }
        }

        return entities;
    }

    private static SearchStrategy RecommendStrategy(QueryAnalysis analysis)
    {
        // 복잡도가 낮으면 키워드 검색이 효과적
        if (analysis.Complexity == QueryComplexityLevel.Simple &&
            analysis.Keywords.Count <= 2)
        {
            return SearchStrategy.Keyword;
        }

        // 기술 엔티티가 많으면 키워드 검색 가중치 높임
        if (analysis.Entities.Count >= 2)
        {
            return SearchStrategy.Hybrid;
        }

        // 복잡한 질문은 시맨틱 검색
        if (analysis.Complexity >= QueryComplexityLevel.Complex)
        {
            return SearchStrategy.Vector;
        }

        // 기본은 하이브리드
        return SearchStrategy.Hybrid;
    }

    private static int RecommendTopK(QueryAnalysis analysis)
    {
        return analysis.Complexity switch
        {
            QueryComplexityLevel.Simple => 5,
            QueryComplexityLevel.Moderate => 10,
            QueryComplexityLevel.Complex => 15,
            QueryComplexityLevel.VeryComplex => 20,
            _ => 10
        };
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Query analyzed: Language={Language}, Intent={Intent}, Complexity={Complexity}, Strategy={Strategy}")]
    private static partial void LogQueryAnalyzed(ILogger logger, string language, QueryIntent intent, QueryComplexityLevel complexity, SearchStrategy strategy);

    #endregion
}

/// <summary>
/// 간단한 토큰 카운터 (근사치).
/// Implements <see cref="TokenMeter.Abstractions.ITokenCounter"/> for FluxIndex usage.
/// </summary>
public class SimpleTokenCounter : ITokenCounter
{
    /// <inheritdoc />
    public int Count(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        // 영어: ~4글자 = 1토큰, 한글: ~2글자 = 1토큰 (근사치)
        var koreanChars = text.Count(c => c >= 0xAC00 && c <= 0xD7A3);
        var otherChars = text.Length - koreanChars;

        return (koreanChars / 2) + (otherChars / 4) + 1;
    }

    /// <inheritdoc />
    public int Count(IEnumerable<string> texts)
    {
        return texts.Sum(Count);
    }

    /// <inheritdoc />
    public bool SupportsModel(string modelId) => false;
}
