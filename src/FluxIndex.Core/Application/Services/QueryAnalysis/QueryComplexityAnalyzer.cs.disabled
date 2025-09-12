using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Core.Application.Services.QueryAnalysis;

/// <summary>
/// 쿼리 복잡도 분석 및 전략 결정 서비스
/// </summary>
public class QueryComplexityAnalyzer : IQueryComplexityAnalyzer
{
    private readonly ILogger<QueryComplexityAnalyzer> _logger;
    private readonly Dictionary<QueryType, PerformanceStats> _performanceStats = new();
    private readonly Dictionary<string, List<string>> _entityPatterns;
    private readonly Dictionary<string, QueryIntent> _intentPatterns;

    public QueryComplexityAnalyzer(ILogger<QueryComplexityAnalyzer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // 엔티티 패턴 초기화
        _entityPatterns = InitializeEntityPatterns();
        
        // 의도 패턴 초기화
        _intentPatterns = InitializeIntentPatterns();
        
        // 성능 통계 초기화
        InitializePerformanceStats();
    }

    public async Task<QueryAnalysis> AnalyzeAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new QueryAnalysis
            {
                Type = QueryType.SimpleKeyword,
                Complexity = ComplexityLevel.Simple,
                Specificity = 0.0,
                Intent = QueryIntent.Informational,
                Language = Language.Other,
                ConfidenceScore = 0.0
            };
        }

        _logger.LogDebug("Analyzing query: {Query}", query);

        var analysis = new QueryAnalysis();

        // 기본 분석
        await AnalyzeBasicPropertiesAsync(query, analysis, cancellationToken);
        
        // 언어 감지
        analysis.Language = DetectLanguage(query);
        
        // 쿼리 유형 분류
        analysis.Type = ClassifyQueryType(query);
        
        // 복잡도 계산
        analysis.Complexity = CalculateComplexity(query, analysis);
        
        // 특이도 계산
        analysis.Specificity = CalculateSpecificity(query, analysis);
        
        // 엔티티 추출
        analysis.Entities = ExtractEntities(query);
        
        // 개념 추출
        analysis.Concepts = ExtractConcepts(query);
        
        // 키워드 추출
        analysis.Keywords = ExtractKeywords(query);
        
        // 의도 파악
        analysis.Intent = DetermineIntent(query);
        
        // 추론형 특성 분석
        AnalyzeReasoningCharacteristics(query, analysis);
        
        // 처리 시간 예측
        analysis.EstimatedProcessingTime = EstimateProcessingTime(analysis);
        
        // 신뢰도 계산
        analysis.ConfidenceScore = CalculateConfidenceScore(analysis);

        _logger.LogDebug("Query analysis completed: Type={Type}, Complexity={Complexity}, Confidence={Confidence}",
            analysis.Type, analysis.Complexity, analysis.ConfidenceScore);

        return analysis;
    }

    public SearchStrategy RecommendStrategy(QueryAnalysis analysis)
    {
        _logger.LogDebug("Recommending strategy for query type {Type}, complexity {Complexity}",
            analysis.Type, analysis.Complexity);

        // 복잡도와 유형 기반 전략 결정
        var strategy = (analysis.Complexity, analysis.Type) switch
        {
            // 단순 쿼리
            (ComplexityLevel.Simple, QueryType.SimpleKeyword) => SearchStrategy.KeywordOnly,
            (ComplexityLevel.Simple, _) => SearchStrategy.DirectVector,
            
            // 보통 복잡도
            (ComplexityLevel.Moderate, QueryType.NaturalQuestion) => SearchStrategy.Hybrid,
            (ComplexityLevel.Moderate, QueryType.ComplexSearch) => SearchStrategy.MultiQuery,
            
            // 복잡한 쿼리
            (ComplexityLevel.Complex, QueryType.ComparisonQuery) => SearchStrategy.TwoStage,
            (ComplexityLevel.Complex, QueryType.TemporalQuery) => SearchStrategy.HyDE,
            (ComplexityLevel.Complex, _) => SearchStrategy.Adaptive,
            
            // 매우 복잡한 쿼리
            (ComplexityLevel.VeryComplex, QueryType.ReasoningQuery) => SearchStrategy.SelfRAG,
            (ComplexityLevel.VeryComplex, QueryType.MultiHopQuery) => SearchStrategy.SelfRAG,
            (ComplexityLevel.VeryComplex, _) => SearchStrategy.StepBack,
            
            _ => SearchStrategy.Hybrid
        };

        // 추론 필요시 Self-RAG 우선
        if (analysis.RequiresReasoning || analysis.IsMultiHop)
        {
            strategy = SearchStrategy.SelfRAG;
        }

        // 성능 기반 조정
        var perfStats = _performanceStats.GetValueOrDefault(analysis.Type);
        if (perfStats != null && perfStats.BestStrategy != SearchStrategy.Hybrid)
        {
            // 성능이 검증된 전략이 있다면 고려
            if (perfStats.StrategyPerformance[perfStats.BestStrategy].SuccessRate > 0.8)
            {
                strategy = perfStats.BestStrategy;
            }
        }

        _logger.LogDebug("Recommended strategy: {Strategy}", strategy);
        return strategy;
    }

    public async Task UpdatePerformanceAsync(
        string query, 
        QueryAnalysis analysis, 
        SearchResult result, 
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // 현재는 동기 처리

        var stats = _performanceStats.GetValueOrDefault(analysis.Type);
        if (stats == null)
        {
            stats = new PerformanceStats { QueryType = analysis.Type };
            _performanceStats[analysis.Type] = stats;
        }

        // 성능 통계 업데이트
        stats.TotalQueries++;
        stats.AverageProcessingTime = TimeSpan.FromMilliseconds(
            (stats.AverageProcessingTime.TotalMilliseconds * (stats.TotalQueries - 1) + 
             result.ProcessingTime.TotalMilliseconds) / stats.TotalQueries);

        if (result.UserSatisfied)
        {
            stats.SuccessfulQueries++;
        }

        // 전략별 성능 추적 (현재 사용된 전략 가정)
        var currentStrategy = RecommendStrategy(analysis);
        if (!stats.StrategyPerformance.ContainsKey(currentStrategy))
        {
            stats.StrategyPerformance[currentStrategy] = new StrategyStats();
        }

        var strategyStats = stats.StrategyPerformance[currentStrategy];
        strategyStats.TotalUses++;
        if (result.UserSatisfied)
        {
            strategyStats.SuccessfulUses++;
        }
        strategyStats.SuccessRate = (double)strategyStats.SuccessfulUses / strategyStats.TotalUses;

        // 최적 전략 업데이트
        stats.BestStrategy = stats.StrategyPerformance
            .Where(kvp => kvp.Value.TotalUses >= 5) // 최소 5회 이상 사용
            .OrderByDescending(kvp => kvp.Value.SuccessRate)
            .FirstOrDefault().Key;

        _logger.LogDebug("Updated performance for {QueryType}: Success rate {SuccessRate:P}",
            analysis.Type, (double)stats.SuccessfulQueries / stats.TotalQueries);
    }

    private async Task AnalyzeBasicPropertiesAsync(string query, QueryAnalysis analysis, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        
        analysis.Metadata["length"] = query.Length;
        analysis.Metadata["word_count"] = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        analysis.Metadata["sentence_count"] = query.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private Language DetectLanguage(string query)
    {
        // 한글 문자 비율 계산
        var koreanChars = query.Count(c => c >= 0xAC00 && c <= 0xD7AF);
        var totalChars = query.Length;
        var koreanRatio = totalChars > 0 ? (double)koreanChars / totalChars : 0;

        // 영문 문자 비율 계산
        var englishChars = query.Count(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'));
        var englishRatio = totalChars > 0 ? (double)englishChars / totalChars : 0;

        if (koreanRatio > 0.3) return englishRatio > 0.2 ? Language.Mixed : Language.Korean;
        if (englishRatio > 0.5) return Language.English;
        
        return Language.Other;
    }

    private QueryType ClassifyQueryType(string query)
    {
        var lowerQuery = query.ToLowerInvariant();

        // 추론형 쿼리 패턴
        var reasoningPatterns = new[]
        {
            "why", "how", "what if", "explain", "compare", "analyze", "evaluate",
            "왜", "어떻게", "만약", "설명", "비교", "분석", "평가"
        };

        // 비교 쿼리 패턴
        var comparisonPatterns = new[]
        {
            "vs", "versus", "compared to", "difference", "similar", "better", "worse",
            "대비", "비교", "차이", "유사", "더 좋", "더 나쁘"
        };

        // 시간 관련 패턴
        var temporalPatterns = new[]
        {
            "before", "after", "during", "when", "since", "until", "recently", "latest",
            "이전", "이후", "동안", "언제", "부터", "까지", "최근", "최신"
        };

        // 멀티홉 패턴
        var multiHopPatterns = new[]
        {
            "and", "then", "also", "furthermore", "additionally", "moreover",
            "그리고", "또한", "더불어", "뿐만 아니라"
        };

        if (reasoningPatterns.Any(pattern => lowerQuery.Contains(pattern)))
        {
            return QueryType.ReasoningQuery;
        }

        if (comparisonPatterns.Any(pattern => lowerQuery.Contains(pattern)))
        {
            return QueryType.ComparisonQuery;
        }

        if (temporalPatterns.Any(pattern => lowerQuery.Contains(pattern)))
        {
            return QueryType.TemporalQuery;
        }

        if (multiHopPatterns.Any(pattern => lowerQuery.Contains(pattern)))
        {
            return QueryType.MultiHopQuery;
        }

        // 복합 조건 검색 (Boolean 연산자)
        if (Regex.IsMatch(lowerQuery, @"\b(and|or|not|AND|OR|NOT)\b"))
        {
            return QueryType.ComplexSearch;
        }

        // 자연어 질문 (의문문)
        if (lowerQuery.Contains("?") || lowerQuery.StartsWith("what") || lowerQuery.StartsWith("how") ||
            lowerQuery.StartsWith("뭐") || lowerQuery.StartsWith("무엇") || lowerQuery.StartsWith("어떻"))
        {
            return QueryType.NaturalQuestion;
        }

        return QueryType.SimpleKeyword;
    }

    private ComplexityLevel CalculateComplexity(string query, QueryAnalysis analysis)
    {
        var complexity = 0;

        // 길이 기반 복잡도
        if (query.Length > 100) complexity++;
        if (query.Length > 200) complexity++;

        // 단어 수 기반 복잡도
        var wordCount = (int)analysis.Metadata["word_count"];
        if (wordCount > 10) complexity++;
        if (wordCount > 20) complexity++;

        // 구조적 복잡도
        if (query.Contains("(") || query.Contains("[")) complexity++;
        if (query.Split(',').Length > 3) complexity++;

        // 쿼리 유형별 복잡도
        complexity += analysis.Type switch
        {
            QueryType.SimpleKeyword => 0,
            QueryType.NaturalQuestion => 1,
            QueryType.ComplexSearch => 2,
            QueryType.ReasoningQuery => 3,
            QueryType.ComparisonQuery => 2,
            QueryType.TemporalQuery => 2,
            QueryType.MultiHopQuery => 3,
            _ => 0
        };

        return complexity switch
        {
            <= 2 => ComplexityLevel.Simple,
            <= 4 => ComplexityLevel.Moderate,
            <= 6 => ComplexityLevel.Complex,
            _ => ComplexityLevel.VeryComplex
        };
    }

    private double CalculateSpecificity(string query, QueryAnalysis analysis)
    {
        var specificity = 0.0;

        // 엔티티 존재
        if (analysis.Entities.Any()) specificity += 0.3;

        // 수치/날짜 존재
        if (Regex.IsMatch(query, @"\d+")) specificity += 0.2;

        // 전문 용어 (대문자로 시작하는 단어)
        var capitalizedWords = Regex.Matches(query, @"\b[A-Z][a-z]+\b").Count;
        specificity += Math.Min(capitalizedWords * 0.1, 0.3);

        // 따옴표 사용 (정확한 구문 검색)
        if (query.Contains("\"")) specificity += 0.2;

        return Math.Min(specificity, 1.0);
    }

    private List<string> ExtractEntities(string query)
    {
        var entities = new List<string>();

        foreach (var pattern in _entityPatterns)
        {
            foreach (var keyword in pattern.Value)
            {
                if (query.ToLowerInvariant().Contains(keyword.ToLowerInvariant()))
                {
                    entities.Add(pattern.Key);
                    break;
                }
            }
        }

        // 고유명사 추출 (대문자로 시작)
        var properNouns = Regex.Matches(query, @"\b[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*\b")
            .Cast<Match>()
            .Select(m => m.Value)
            .Where(s => s.Length > 2)
            .Distinct();

        entities.AddRange(properNouns);

        return entities.Distinct().ToList();
    }

    private List<string> ExtractConcepts(string query)
    {
        // 개념 추출을 위한 키워드 패턴
        var conceptPatterns = new Dictionary<string, List<string>>
        {
            ["Technology"] = new() { "AI", "machine learning", "blockchain", "cloud", "인공지능", "머신러닝", "블록체인", "클라우드" },
            ["Business"] = new() { "business", "market", "strategy", "revenue", "비즈니스", "시장", "전략", "수익" },
            ["Science"] = new() { "research", "study", "analysis", "theory", "연구", "분석", "이론" },
            ["Education"] = new() { "learning", "education", "training", "course", "학습", "교육", "훈련", "과정" }
        };

        var concepts = new List<string>();

        foreach (var pattern in conceptPatterns)
        {
            if (pattern.Value.Any(keyword => query.ToLowerInvariant().Contains(keyword.ToLowerInvariant())))
            {
                concepts.Add(pattern.Key);
            }
        }

        return concepts;
    }

    private List<string> ExtractKeywords(string query)
    {
        // 불용어 제거
        var stopWords = new HashSet<string>
        {
            "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with", "by", "from",
            "은", "는", "이", "가", "을", "를", "에", "에서", "로", "으로", "와", "과", "도", "만", "까지", "부터"
        };

        return query.Split(new[] { ' ', ',', '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length > 2 && !stopWords.Contains(word.ToLowerInvariant()))
            .Select(word => word.Trim())
            .Distinct()
            .ToList();
    }

    private QueryIntent DetermineIntent(string query)
    {
        var lowerQuery = query.ToLowerInvariant();

        foreach (var pattern in _intentPatterns)
        {
            if (pattern.Value.Any(keyword => lowerQuery.Contains(keyword)))
            {
                return pattern.Value;
            }
        }

        return QueryIntent.Informational; // 기본값
    }

    private void AnalyzeReasoningCharacteristics(string query, QueryAnalysis analysis)
    {
        var lowerQuery = query.ToLowerInvariant();

        // 추론 필요 패턴
        var reasoningPatterns = new[]
        {
            "why", "how", "explain", "analyze", "evaluate", "prove", "demonstrate",
            "왜", "어떻게", "설명", "분석", "평가", "증명", "보여"
        };

        analysis.RequiresReasoning = reasoningPatterns.Any(pattern => lowerQuery.Contains(pattern));

        // 시간적 컨텍스트
        var temporalPatterns = new[]
        {
            "before", "after", "during", "when", "since", "until", "timeline", "history",
            "이전", "이후", "동안", "언제", "부터", "까지", "시간", "역사"
        };

        analysis.HasTemporalContext = temporalPatterns.Any(pattern => lowerQuery.Contains(pattern));

        // 비교적 컨텍스트
        var comparativePatterns = new[]
        {
            "compare", "contrast", "versus", "vs", "similar", "different", "better", "worse",
            "비교", "대조", "대비", "유사", "다른", "더 좋", "더 나쁜"
        };

        analysis.HasComparativeContext = comparativePatterns.Any(pattern => lowerQuery.Contains(pattern));

        // 멀티홉 추론
        var multiHopPatterns = new[]
        {
            "and then", "furthermore", "additionally", "also", "moreover", "besides",
            "그리고", "또한", "더불어", "게다가", "뿐만 아니라"
        };

        analysis.IsMultiHop = multiHopPatterns.Any(pattern => lowerQuery.Contains(pattern)) ||
                             query.Split('.', '!', '?').Length > 2;
    }

    private TimeSpan EstimateProcessingTime(QueryAnalysis analysis)
    {
        // 기본 처리 시간 + 복잡도별 가중치
        var baseTime = TimeSpan.FromMilliseconds(50);
        
        var complexityMultiplier = analysis.Complexity switch
        {
            ComplexityLevel.Simple => 1.0,
            ComplexityLevel.Moderate => 2.0,
            ComplexityLevel.Complex => 4.0,
            ComplexityLevel.VeryComplex => 8.0,
            _ => 1.0
        };

        var typeMultiplier = analysis.Type switch
        {
            QueryType.SimpleKeyword => 1.0,
            QueryType.NaturalQuestion => 1.5,
            QueryType.ComplexSearch => 2.0,
            QueryType.ReasoningQuery => 4.0,
            QueryType.MultiHopQuery => 5.0,
            _ => 1.0
        };

        var totalMultiplier = complexityMultiplier * typeMultiplier;
        return TimeSpan.FromMilliseconds(baseTime.TotalMilliseconds * totalMultiplier);
    }

    private double CalculateConfidenceScore(QueryAnalysis analysis)
    {
        var confidence = 0.5; // 기본 신뢰도

        // 언어 감지 신뢰도
        if (analysis.Language != Language.Other) confidence += 0.2;

        // 엔티티 추출 신뢰도
        if (analysis.Entities.Any()) confidence += 0.1;

        // 키워드 품질
        if (analysis.Keywords.Count >= 3) confidence += 0.1;

        // 구조적 완정성
        var wordCount = (int)analysis.Metadata["word_count"];
        if (wordCount >= 3 && wordCount <= 50) confidence += 0.1;

        return Math.Min(confidence, 1.0);
    }

    private Dictionary<string, List<string>> InitializeEntityPatterns()
    {
        return new Dictionary<string, List<string>>
        {
            ["Person"] = new() { "kim", "lee", "park", "choi", "john", "mary", "김", "이", "박", "최" },
            ["Organization"] = new() { "company", "corp", "inc", "ltd", "university", "school", "회사", "대학교", "학교" },
            ["Location"] = new() { "seoul", "busan", "korea", "usa", "china", "japan", "서울", "부산", "한국", "미국" },
            ["Technology"] = new() { "AI", "ML", "IoT", "API", "SDK", "framework", "library", "프레임워크", "라이브러리" }
        };
    }

    private Dictionary<string, QueryIntent> InitializeIntentPatterns()
    {
        return new Dictionary<string, QueryIntent>
        {
            ["what is"] = QueryIntent.Informational,
            ["how to"] = QueryIntent.Informational,
            ["where"] = QueryIntent.Navigational,
            ["buy"] = QueryIntent.Transactional,
            ["purchase"] = QueryIntent.Transactional,
            ["analyze"] = QueryIntent.Analytical,
            ["compare"] = QueryIntent.Analytical,
            ["explore"] = QueryIntent.Exploratory,
            ["뭐야"] = QueryIntent.Informational,
            ["어떻게"] = QueryIntent.Informational,
            ["어디"] = QueryIntent.Navigational,
            ["구매"] = QueryIntent.Transactional,
            ["분석"] = QueryIntent.Analytical,
            ["비교"] = QueryIntent.Analytical,
            ["탐색"] = QueryIntent.Exploratory
        };
    }

    private void InitializePerformanceStats()
    {
        foreach (QueryType queryType in Enum.GetValues<QueryType>())
        {
            _performanceStats[queryType] = new PerformanceStats
            {
                QueryType = queryType,
                BestStrategy = SearchStrategy.Hybrid
            };
        }
    }
}

/// <summary>
/// 쿼리 유형별 성능 통계
/// </summary>
internal class PerformanceStats
{
    public QueryType QueryType { get; set; }
    public int TotalQueries { get; set; }
    public int SuccessfulQueries { get; set; }
    public TimeSpan AverageProcessingTime { get; set; }
    public SearchStrategy BestStrategy { get; set; } = SearchStrategy.Hybrid;
    public Dictionary<SearchStrategy, StrategyStats> StrategyPerformance { get; set; } = new();
}

/// <summary>
/// 전략별 성능 통계
/// </summary>
internal class StrategyStats
{
    public int TotalUses { get; set; }
    public int SuccessfulUses { get; set; }
    public double SuccessRate { get; set; }
}