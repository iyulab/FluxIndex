using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// 쿼리 복잡도 분석기 구현체
/// </summary>
public partial class QueryComplexityAnalyzer : IQueryComplexityAnalyzer
{
    private readonly ILogger<QueryComplexityAnalyzer> _logger;

    // Technical terms by domain for enhanced analysis
    private static readonly Dictionary<string, HashSet<string>> TechnicalTermsByDomain = new()
    {
        ["programming"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "API", "REST", "GraphQL", "HTTP", "HTTPS", "JSON", "XML", "SQL", "NoSQL",
            "OAuth", "JWT", "CORS", "CRUD", "ORM", "MVC", "MVVM", "DI", "IoC",
            "async", "await", "callback", "promise", "thread", "mutex", "semaphore",
            "lambda", "closure", "interface", "abstract", "polymorphism", "inheritance",
            "microservices", "monolith", "serverless", "container", "kubernetes", "docker",
            "algorithm", "data structure", "hash", "linked list", "binary tree", "graph"
        },
        ["ai_ml"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AI", "ML", "NLP", "LLM", "GPT", "BERT", "transformer", "embedding",
            "vector", "RAG", "retrieval", "generation", "fine-tuning", "prompt",
            "neural", "deep learning", "machine learning", "classification",
            "regression", "clustering", "attention", "encoder", "decoder",
            "CNN", "RNN", "LSTM", "GAN", "diffusion", "tokenization",
            "cross-encoder", "bi-encoder", "reranking", "semantic search"
        },
        ["database"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "database", "table", "index", "query", "join", "transaction", "ACID",
            "normalization", "denormalization", "partition", "shard", "replica",
            "PostgreSQL", "MySQL", "MongoDB", "Redis", "Elasticsearch", "pgvector",
            "SQLite", "indexing", "B-tree", "LSM", "MVCC", "isolation level"
        },
        ["devops"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CI/CD", "pipeline", "deployment", "infrastructure", "terraform",
            "ansible", "helm", "ingress", "load balancer", "auto-scaling",
            "monitoring", "logging", "metrics", "tracing", "observability",
            "container", "orchestration", "service mesh", "gitops"
        },
        ["korean"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "알고리즘", "데이터베이스", "프레임워크", "라이브러리", "인터페이스",
            "임베딩", "벡터", "시맨틱", "머신러닝", "딥러닝", "인공지능",
            "클라우드", "컨테이너", "마이크로서비스", "검색", "색인"
        }
    };

    // Legacy flat set for backward compatibility
    private static readonly HashSet<string> TechnicalTerms = TechnicalTermsByDomain.Values
        .SelectMany(x => x)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

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
        var technicalDomains = DetectTechnicalDomains(query, tokens);

        var analysis = new QueryAnalysis
        {
            Type = DetermineQueryType(query, tokens),
            Complexity = DetermineComplexityLevel(query, tokens),
            Specificity = CalculateSpecificity(tokens),
            Entities = ExtractEntities(tokens),
            Concepts = ExtractConcepts(tokens),
            Keywords = ExtractKeywords(tokens),
            TechnicalDomains = technicalDomains,
            RequiresReasoning = RequiresReasoning(query, tokens),
            HasComparativeContext = HasComparativeContext(tokens),
            IsMultiHop = IsMultiHop(query, tokens),
            ConfidenceScore = CalculateConfidenceScore(query, tokens)
        };

        if (_logger.IsEnabled(LogLevel.Debug))
            LogQueryComplexity2(_logger, query, analysis.Type, analysis.Complexity);

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

    #region Private Methods

    private static List<string> DetectTechnicalDomains(string query, string[] tokens)
    {
        var domains = new List<string>();
        var lowerQuery = query.ToLowerInvariant();

        foreach (var (domain, terms) in TechnicalTermsByDomain)
        {
            // Check individual tokens
            var tokenMatches = tokens.Count(t => terms.Contains(t));

            // Check multi-word terms in the query
            var multiWordMatches = terms
                .Where(term => term.Contains(' '))
                .Count(term => lowerQuery.Contains(term, StringComparison.OrdinalIgnoreCase));

            if (tokenMatches > 0 || multiWordMatches > 0)
            {
                domains.Add(domain);
            }
        }

        return domains;
    }

    private static string[] TokenizeQuery(string query)
    {
        // 단순한 토크나이징: 공백 및 구두점으로 분리
        return Regex.Split(query.ToLowerInvariant(), @"\s+|[.,;:!?()""']")
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToArray();
    }

    private static QueryType DetermineQueryType(string query, string[] tokens)
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

    private static ComplexityLevel DetermineComplexityLevel(string query, string[] tokens)
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

    private static double CalculateSpecificity(string[] tokens)
    {
        // 고유 토큰 비율과 기술 용어 비율로 특정성 계산
        var uniqueTokens = tokens.Distinct().Count();
        var totalTokens = tokens.Length;
        var technicalTerms = tokens.Count(t => TechnicalTerms.Contains(t));

        var uniquenessRatio = totalTokens > 0 ? (double)uniqueTokens / totalTokens : 0.0;
        var technicalRatio = totalTokens > 0 ? (double)technicalTerms / totalTokens : 0.0;

        return Math.Min(1.0, (uniquenessRatio + technicalRatio) / 2.0);
    }

    private static List<string> ExtractEntities(string[] tokens)
    {
        // 대문자로 시작하는 단어를 개체명으로 간주
        return tokens.Where(t => t.Length > 1 && char.IsUpper(t[0])).ToList();
    }

    private static List<string> ExtractConcepts(string[] tokens)
    {
        // 기술 용어와 긴 단어들을 개념으로 간주
        return tokens.Where(t => TechnicalTerms.Contains(t) || t.Length > 6).ToList();
    }

    private static List<string> ExtractKeywords(string[] tokens)
    {
        // 불용어 제외한 의미 있는 키워드들
        var stopWords = new HashSet<string> { "the", "a", "an", "is", "are", "was", "were", "을", "를", "이", "가", "은", "는" };
        return tokens.Where(t => !stopWords.Contains(t) && t.Length > 2).ToList();
    }

    private static bool RequiresReasoning(string query, string[] tokens)
    {
        var reasoningPatterns = new[]
        {
            "why", "how does", "how do", "explain", "reason", "because", "cause", "effective",
            "왜", "어떻게", "설명", "이유", "때문"
        };

        return reasoningPatterns.Any(pattern => query.Contains(pattern, StringComparison.OrdinalIgnoreCase)) ||
               tokens.Any(t => new[] { "why", "how", "explain", "reason", "effective", "because" }.Contains(t, StringComparer.OrdinalIgnoreCase));
    }

    private static bool HasTemporalContext(string[] tokens)
    {
        return tokens.Any(t => TemporalWords.Contains(t));
    }

    private static bool HasComparativeContext(string[] tokens)
    {
        return tokens.Any(t => ComparisonWords.Contains(t));
    }

    private static bool IsMultiHop(string query, string[] tokens)
    {
        // "and then", "after that", "다음에" 등의 패턴
        var multiHopPatterns = new[]
        {
            "and then", "after that", "다음에", "그리고", "또한"
        };

        return multiHopPatterns.Any(pattern => query.Contains(pattern, StringComparison.OrdinalIgnoreCase)) ||
               tokens.Count(t => LogicalOperators.Contains(t)) > 1;
    }

    private static double CalculateConfidenceScore(string query, string[] tokens)
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

    private static SearchStrategy RecommendSimpleStrategy(QueryAnalysis analysis)
    {
        // A short query needs its keyword half most: an exact term ("invoices", a product code) is what vector search
        // matches worst. Vector-only stays a strategy a caller can force. Until the technical flag was corrected almost every
        // query counted as technical, so Simple queries ran Hybrid; this keeps that and does not move ranking unmeasured.
        _ = analysis;
        return SearchStrategy.Hybrid;
    }

    private static SearchStrategy RecommendModerateStrategy(QueryAnalysis analysis)
    {
        // An AI/ML-domain query was recommended HyDE here, which adaptive search does not execute: it ran
        // as Hybrid. The recommendation now names the strategy that runs (DAT already shifts ai_ml toward vector).

        // Comparative queries need multi-query
        if (analysis.HasComparativeContext)
            return SearchStrategy.MultiQuery;

        return SearchStrategy.Hybrid;
    }

    private static SearchStrategy RecommendComplexStrategy(QueryAnalysis analysis)
    {
        // Multi-domain technical queries
        if (analysis.TechnicalDomains.Count >= 2)
            return SearchStrategy.TwoStage;

        if (analysis.RequiresReasoning)
            return SearchStrategy.TwoStage;

        if (analysis.IsMultiHop)
            return SearchStrategy.MultiQuery;

        return SearchStrategy.Hybrid;
    }

    private static SearchStrategy RecommendVeryComplexStrategy(QueryAnalysis analysis)
    {
        // Multi-hop reasoning and reasoning over a technical domain were recommended SelfRAG, and anything else
        // Adaptive; adaptive search executes neither, and both ran as Hybrid. The recommendation now names the
        // strategy that runs, so the retrieval these queries get is unchanged.
        if (analysis.RequiresReasoning && !analysis.IsMultiHop && analysis.TechnicalDomains.Count == 0)
            return SearchStrategy.TwoStage;

        return SearchStrategy.Hybrid;
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Query analysis completed: {Query} -> {Type}, {Complexity}")]
    private static partial void LogQueryComplexity2(ILogger logger, string query, QueryType type, ComplexityLevel complexity);

    #endregion
}
