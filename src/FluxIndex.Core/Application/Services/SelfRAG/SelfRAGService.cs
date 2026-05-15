using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Core.Services.SelfRAG;

/// <summary>
/// Self-RAG (Self-Reflective Retrieval Augmented Generation) 서비스 구현
/// </summary>
public partial class SelfRAGService : ISelfRAGService
{
    private static readonly char[] QueryTokenSeparators = [' ', '\t', '\n', ',', '.', '!', '?'];

    private readonly IAdaptiveSearchService _adaptiveSearch;
    private readonly IQueryComplexityAnalyzer _queryAnalyzer;
    private readonly ITextCompletionService? _textCompletion;
    private readonly ILogger<SelfRAGService> _logger;

    public SelfRAGService(
        IAdaptiveSearchService adaptiveSearch,
        IQueryComplexityAnalyzer queryAnalyzer,
        ILogger<SelfRAGService> logger,
        ITextCompletionService? textCompletion = null)
    {
        _adaptiveSearch = adaptiveSearch ?? throw new ArgumentNullException(nameof(adaptiveSearch));
        _queryAnalyzer = queryAnalyzer ?? throw new ArgumentNullException(nameof(queryAnalyzer));
        _textCompletion = textCompletion;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SelfRAGResult> SearchAsync(
        string query, 
        SelfRAGOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new SelfRAGOptions();
        var stopwatch = Stopwatch.StartNew();

        if (_logger.IsEnabled(LogLevel.Information))
            LogSelfRAG14(_logger, query);

        var result = new SelfRAGResult
        {
            IsSuccessful = false,
            TerminationReason = "Not started"
        };

        var currentQuery = query;
        var searchOptions = new AdaptiveSearchOptions
        {
            MaxResults = options.MaxResults,
            EnableDetailedLogging = options.EnableDetailedLogging,
            UserContext = options.UserContext
        };

        try
        {
            for (int iteration = 1; iteration <= options.MaxIterations; iteration++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_logger.IsEnabled(LogLevel.Information))
                    LogSelfRAG13(_logger, iteration, options.MaxIterations);

                var iterationStopwatch = Stopwatch.StartNew();
                var searchIteration = new SearchIteration
                {
                    IterationNumber = iteration,
                    Query = currentQuery
                };

                // 1. 적응형 검색 실행
                var searchResult = await _adaptiveSearch.SearchAsync(currentQuery, searchOptions, cancellationToken);
                searchIteration.Results = searchResult.Documents;
                searchIteration.Strategy = searchResult.UsedStrategy;

                // 2. 검색 결과 품질 평가
                var qualityAssessment = await AssessResultQualityAsync(currentQuery, searchResult.Documents, cancellationToken);
                searchIteration.QualityAssessment = qualityAssessment;

                iterationStopwatch.Stop();
                searchIteration.ProcessingTime = iterationStopwatch.Elapsed;

                result.Iterations.Add(searchIteration);

                var docCount = searchResult.Documents.Count();
                LogSelfRAG12(_logger, iteration, qualityAssessment.OverallScore, docCount);

                // 3. 품질 임계값 확인
                if (qualityAssessment.OverallScore >= options.QualityThreshold &&
                    docCount >= options.MinResults)
                {
                    result.FinalResults = searchResult.Documents;
                    result.FinalQualityScore = qualityAssessment.OverallScore;
                    result.IsSuccessful = true;
                    result.TerminationReason = $"Quality threshold reached ({qualityAssessment.OverallScore:F2})";

                    if (_logger.IsEnabled(LogLevel.Information))
                        LogSelfRAG11(_logger, iteration);
                    break;
                }

                // 4. 마지막 반복이면 현재 결과로 마무리
                if (iteration == options.MaxIterations)
                {
                    result.FinalResults = searchResult.Documents;
                    result.FinalQualityScore = qualityAssessment.OverallScore;
                    result.IsSuccessful = qualityAssessment.OverallScore >= (options.QualityThreshold * 0.8); // 80% 수준도 수용
                    result.TerminationReason = "Maximum iterations reached";
                    break;
                }

                // 5. 개선 필요 - 쿼리 개선 및 다음 반복 준비
                if (options.EnableAutoRefinement)
                {
                    var refinementSuggestions = await SuggestQueryRefinementsAsync(currentQuery, qualityAssessment, cancellationToken);
                    
                    if (refinementSuggestions.RefinedQueries.Count != 0)
                    {
                        // 가장 유망한 개선 쿼리 선택
                        var bestRefinement = refinementSuggestions.RefinedQueries
                            .OrderByDescending(rq => rq.ExpectedImprovementScore)
                            .First();

                        currentQuery = bestRefinement.QueryText;
                        searchOptions.ForceStrategy = bestRefinement.RecommendedStrategy;

                        var refinementAction = new RefinementAction
                        {
                            ActionType = RefinementActionType.QueryRefinement,
                            StartTime = DateTime.UtcNow,
                            EndTime = DateTime.UtcNow,
                            Description = $"Refined query using {bestRefinement.RefinementType}",
                            Input = { ["original_query"] = searchIteration.Query },
                            Output = { ["refined_query"] = currentQuery },
                            IsSuccessful = true
                        };

                        result.RefinementActions.Add(refinementAction);
                        searchIteration.ImprovementNotes.Add($"Query refined: {bestRefinement.Rationale}");
                        searchIteration.NextIterationPlan = $"Retry with refined query: {currentQuery}";

                        if (_logger.IsEnabled(LogLevel.Information))
                            LogSelfRAG10(_logger, currentQuery);
                    }
                    else
                    {
                        // 개선할 수 없으면 다른 전략 시도
                        searchOptions.ForceStrategy = GetAlternativeStrategy(searchResult.UsedStrategy);
                        searchIteration.NextIterationPlan = $"Try alternative strategy: {searchOptions.ForceStrategy}";
                    }
                }
                else
                {
                    // 자동 개선 없이는 대안 전략만 시도
                    searchOptions.ForceStrategy = GetAlternativeStrategy(searchResult.UsedStrategy);
                }
            }
        }
        catch (Exception ex)
        {
            LogSelfRAG9(_logger, ex);
            result.IsSuccessful = false;
            result.TerminationReason = $"Error: {ex.Message}";
            throw;
        }
        finally
        {
            stopwatch.Stop();
            result.TotalProcessingTime = stopwatch.Elapsed;
            
            if (_logger.IsEnabled(LogLevel.Information))
                LogSelfRAG8(_logger, result.IsSuccessful, result.Iterations.Count, result.FinalQualityScore, result.TotalProcessingTime.TotalMilliseconds);
        }

        return result;
    }

    public async Task<QualityAssessment> AssessResultQualityAsync(
        string query, 
        IEnumerable<Document> results,
        CancellationToken cancellationToken = default)
    {
        var documents = results.ToList();
        var assessment = new QualityAssessment
        {
            ResultCount = documents.Count
        };

        if (_logger.IsEnabled(LogLevel.Information))
            LogSelfRAG7(_logger, documents.Count, query);

        try
        {
            // 1. 관련성 평가
            assessment.RelevanceScore = await AssessRelevanceAsync(query, documents, cancellationToken);

            // 2. 완전성 평가
            assessment.CompletenessScore = await AssessCompletenessAsync(query, documents, cancellationToken);

            // 3. 다양성 평가
            assessment.DiversityScore = AssessDiversity(documents);

            // 4. 신뢰성 평가
            assessment.CredibilityScore = AssessCredibility(documents);

            // 5. 최신성 평가
            assessment.FreshnessScore = AssessFreshness(documents);

            // 6. 전체 점수 계산
            assessment.OverallScore = CalculateOverallScore(assessment);

            // 7. 문제점 식별
            assessment.Issues = IdentifyQualityIssues(query, documents, assessment);

            // 8. 개선 제안
            assessment.Suggestions = GenerateImprovementSuggestions(assessment);

            // 9. 평가 근거 추가
            PopulateRationale(assessment);

            if (_logger.IsEnabled(LogLevel.Information))
                LogSelfRAG6(_logger, assessment.OverallScore, assessment.RelevanceScore, assessment.Issues.Count);
        }
        catch (Exception ex)
        {
            LogSelfRAG5(_logger, ex);
            
            // 폴백: 기본적인 휴리스틱 평가
            assessment = CreateFallbackAssessment(documents);
        }

        return assessment;
    }

    public async Task<QueryRefinementSuggestions> SuggestQueryRefinementsAsync(
        string originalQuery, 
        QualityAssessment assessment,
        CancellationToken cancellationToken = default)
    {
        if (_logger.IsEnabled(LogLevel.Information))
            LogSelfRAG4(_logger, originalQuery);

        var suggestions = new QueryRefinementSuggestions
        {
            OriginalQuery = originalQuery
        };

        try
        {
            // 1. 문제 분석 기반 개선 제안
            foreach (var issue in assessment.Issues)
            {
                await GenerateRefinementsForIssueAsync(originalQuery, issue, suggestions, cancellationToken);
            }

            // 2. 일반적인 개선 전략
            await GenerateGeneralRefinementsAsync(originalQuery, assessment, suggestions, cancellationToken);

            // 3. 결과 개수 기반 조정
            if (assessment.ResultCount < 5)
            {
                suggestions.RefinedQueries.Add(new RefinedQuery
                {
                    QueryText = GeneralizeQuery(originalQuery),
                    RefinementType = RefinementType.Generalization,
                    Rationale = "Too few results - generalizing query",
                    ExpectedImprovementScore = 0.7,
                    RecommendedStrategy = SearchStrategy.MultiQuery
                });
            }
            else if (assessment.ResultCount > 50)
            {
                suggestions.RefinedQueries.Add(new RefinedQuery
                {
                    QueryText = SpecifyQuery(originalQuery),
                    RefinementType = RefinementType.Specification,
                    Rationale = "Too many results - making query more specific",
                    ExpectedImprovementScore = 0.6,
                    RecommendedStrategy = SearchStrategy.TwoStage
                });
            }

            // 4. 점수순 정렬
            suggestions.RefinedQueries = suggestions.RefinedQueries
                .OrderByDescending(rq => rq.ExpectedImprovementScore)
                .Take(5) // 최대 5개 제안
                .ToList();

            LogSelfRAG3(_logger, suggestions.RefinedQueries.Count);
        }
        catch (Exception ex)
        {
            LogSelfRAG2(_logger, ex);
        }

        return suggestions;
    }

    private static async Task<double> AssessRelevanceAsync(string query, List<Document> documents, CancellationToken cancellationToken)
    {
        if (documents.Count == 0) return 0.0;

        // 키워드 매칭 기반 관련성 평가
        var queryTerms = ExtractQueryTerms(query);
        var relevanceScores = new List<double>();

        foreach (var doc in documents)
        {
            var content = doc.Content.ToLowerInvariant();
            var matchingTerms = queryTerms.Count(term => content.Contains(term, StringComparison.OrdinalIgnoreCase));
            var relevance = queryTerms.Count != 0 ? (double)matchingTerms / queryTerms.Count : 0.0;
            relevanceScores.Add(relevance);
        }

        await Task.CompletedTask;
        return relevanceScores.Average();
    }

    private static async Task<double> AssessCompletenessAsync(string query, List<Document> documents, CancellationToken cancellationToken)
    {
        // 완전성을 결과 수와 내용 다양성으로 평가
        var completeness = 0.0;

        // 결과 개수 기반 점수
        var countScore = Math.Min(documents.Count / 10.0, 1.0); // 10개 이상이면 만점
        completeness += countScore * 0.4;

        // 내용 길이 기반 점수
        if (documents.Count != 0)
        {
            var avgLength = documents.Average(d => d.Content.Length);
            var lengthScore = Math.Min(avgLength / 500.0, 1.0); // 500자 이상이면 만점
            completeness += lengthScore * 0.3;
        }

        // 토픽 커버리지 기반 점수
        var topicScore = AssessTopicCoverage(query, documents);
        completeness += topicScore * 0.3;

        await Task.CompletedTask;
        return Math.Min(completeness, 1.0);
    }

    private static double AssessDiversity(List<Document> documents)
    {
        if (documents.Count <= 1) return 0.0;

        // 내용 유사성 기반 다양성 계산
        var similarities = new List<double>();
        
        for (int i = 0; i < Math.Min(documents.Count, 10); i++)
        {
            for (int j = i + 1; j < Math.Min(documents.Count, 10); j++)
            {
                var similarity = CalculateTextSimilarity(documents[i].Content, documents[j].Content);
                similarities.Add(similarity);
            }
        }

        if (similarities.Count == 0) return 1.0;

        // 낮은 유사성 = 높은 다양성
        var avgSimilarity = similarities.Average();
        return Math.Max(0.0, 1.0 - avgSimilarity);
    }

    private static double AssessCredibility(List<Document> documents)
    {
        // 문서 메타데이터 기반 신뢰성 평가
        var credibilityScore = 0.8; // 기본 점수

        foreach (var doc in documents.Take(10))
        {
            // 메타데이터에서 신뢰성 지표 확인
            if (doc.Metadata.ContainsKey("source_reliability"))
            {
                if (doc.Metadata["source_reliability"] is string reliabilityStr && 
                    double.TryParse(reliabilityStr, out var reliability))
                {
                    credibilityScore = (credibilityScore + reliability) / 2;
                }
            }
        }

        return credibilityScore;
    }

    private static double AssessFreshness(List<Document> documents)
    {
        // 문서의 최신성 평가
        var freshnessScore = 0.7; // 기본 점수

        foreach (var doc in documents.Take(10))
        {
            if (doc.Metadata.ContainsKey("last_modified"))
            {
                if (doc.Metadata["last_modified"] is string dateStr && 
                    DateTime.TryParse(dateStr, out var lastModified))
                {
                    var daysSinceModified = (DateTime.UtcNow - lastModified).TotalDays;
                    var docFreshness = Math.Max(0.0, 1.0 - (daysSinceModified / 365.0)); // 1년 이내면 신선
                    freshnessScore = (freshnessScore + docFreshness) / 2;
                }
            }
        }

        return freshnessScore;
    }

    private static double CalculateOverallScore(QualityAssessment assessment)
    {
        // 가중 평균으로 전체 점수 계산
        return assessment.RelevanceScore * 0.35 +
               assessment.CompletenessScore * 0.25 +
               assessment.DiversityScore * 0.15 +
               assessment.CredibilityScore * 0.15 +
               assessment.FreshnessScore * 0.10;
    }

    private static List<QualityIssue> IdentifyQualityIssues(string query, List<Document> documents, QualityAssessment assessment)
    {
        var issues = new List<QualityIssue>();

        // 결과 부족
        if (assessment.ResultCount < 5)
        {
            issues.Add(new QualityIssue
            {
                Type = QualityIssueType.InsufficientResults,
                Severity = 4,
                Description = $"Only {assessment.ResultCount} results found, minimum 5 expected",
                RecommendedAction = "Generalize query or try different search strategy"
            });
        }

        // 관련성 부족
        if (assessment.RelevanceScore < 0.5)
        {
            issues.Add(new QualityIssue
            {
                Type = QualityIssueType.InsufficientRelevance,
                Severity = 5,
                Description = $"Low relevance score: {assessment.RelevanceScore:F2}",
                RecommendedAction = "Refine query terms or use semantic search"
            });
        }

        // 다양성 부족
        if (assessment.DiversityScore < 0.3)
        {
            issues.Add(new QualityIssue
            {
                Type = QualityIssueType.LackOfDiversity,
                Severity = 3,
                Description = $"Low diversity score: {assessment.DiversityScore:F2}",
                RecommendedAction = "Use different search strategies or expand query scope"
            });
        }

        // 중복된 결과
        var duplicates = FindDuplicateResults(documents);
        if (duplicates.Count != 0)
        {
            issues.Add(new QualityIssue
            {
                Type = QualityIssueType.DuplicateResults,
                Severity = 2,
                Description = $"Found {duplicates.Count} potential duplicates",
                AffectedResultIndices = duplicates,
                RecommendedAction = "Apply deduplication"
            });
        }

        return issues;
    }

    private static List<ImprovementSuggestion> GenerateImprovementSuggestions(QualityAssessment assessment)
    {
        var suggestions = new List<ImprovementSuggestion>();

        foreach (var issue in assessment.Issues)
        {
            var suggestion = new ImprovementSuggestion
            {
                Priority = issue.Severity,
                Suggestion = issue.RecommendedAction ?? "No specific action recommended",
                Complexity = ImplementationComplexity.Medium
            };

            suggestion.Type = issue.Type switch
            {
                QualityIssueType.InsufficientResults => ImprovementType.ExpandSearch,
                QualityIssueType.InsufficientRelevance => ImprovementType.QueryModification,
                QualityIssueType.DuplicateResults => ImprovementType.Deduplication,
                QualityIssueType.LackOfDiversity => ImprovementType.StrategyChange,
                _ => ImprovementType.QueryModification
            };

            suggestions.Add(suggestion);
        }

        return suggestions.OrderByDescending(s => s.Priority).ToList();
    }

    private static async Task GenerateRefinementsForIssueAsync(
        string originalQuery,
        QualityIssue issue,
        QueryRefinementSuggestions suggestions,
        CancellationToken cancellationToken)
    {
        switch (issue.Type)
        {
            case QualityIssueType.InsufficientResults:
                suggestions.RefinedQueries.Add(new RefinedQuery
                {
                    QueryText = GeneralizeQuery(originalQuery),
                    RefinementType = RefinementType.Generalization,
                    Rationale = "Generalized to get more results",
                    ExpectedImprovementScore = 0.7,
                    RecommendedStrategy = SearchStrategy.MultiQuery
                });
                break;

            case QualityIssueType.InsufficientRelevance:
                suggestions.RefinedQueries.Add(new RefinedQuery
                {
                    QueryText = AddContextToQuery(originalQuery),
                    RefinementType = RefinementType.ContextAddition,
                    Rationale = "Added context to improve relevance",
                    ExpectedImprovementScore = 0.8,
                    RecommendedStrategy = SearchStrategy.Hybrid
                });
                break;

            case QualityIssueType.LackOfDiversity:
                suggestions.AlternativeStrategies.Add(SearchStrategy.MultiQuery);
                suggestions.RefinedQueries.Add(new RefinedQuery
                {
                    QueryText = RestructureQuery(originalQuery),
                    RefinementType = RefinementType.Restructuring,
                    Rationale = "Restructured for better diversity",
                    ExpectedImprovementScore = 0.6,
                    RecommendedStrategy = SearchStrategy.MultiQuery
                });
                break;
        }
        await Task.CompletedTask;
    }

    private async Task GenerateGeneralRefinementsAsync(
        string originalQuery,
        QualityAssessment assessment, 
        QueryRefinementSuggestions suggestions,
        CancellationToken cancellationToken)
    {
        // LLM이 있으면 고급 쿼리 개선 사용
        if (_textCompletion != null)
        {
            await GenerateAdvancedRefinementsAsync(originalQuery, assessment, suggestions, cancellationToken);
        }
        else
        {
            // 휴리스틱 기반 개선
            GenerateHeuristicRefinements(originalQuery, suggestions);
        }
    }

    private async Task GenerateAdvancedRefinementsAsync(
        string originalQuery,
        QualityAssessment assessment,
        QueryRefinementSuggestions suggestions,
        CancellationToken cancellationToken)
    {
        try
        {
            var prompt = $"""
                Original query: "{originalQuery}"
                Quality issues: {string.Join(", ", assessment.Issues.Select(i => i.Description))}
                
                Generate 3 improved versions of this query to address the quality issues.
                Each version should be on a separate line.
                """;

            var completion = await _textCompletion!.CompleteAsync(prompt, new Flux.Abstractions.TextCompletionOptions { MaxTokens = 500, Temperature = 0.7f }, cancellationToken);
            var refinedQueries = completion.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(q => !string.IsNullOrWhiteSpace(q))
                .Take(3)
                .ToList();

            for (int i = 0; i < refinedQueries.Count; i++)
            {
                suggestions.RefinedQueries.Add(new RefinedQuery
                {
                    QueryText = refinedQueries[i].Trim(),
                    RefinementType = RefinementType.Restructuring,
                    Rationale = "LLM-generated refinement",
                    ExpectedImprovementScore = 0.8 - (i * 0.1), // 첫 번째가 가장 좋다고 가정
                    RecommendedStrategy = SearchStrategy.Hybrid
                });
            }
        }
        catch (Exception ex)
        {
            LogSelfRAG1(_logger, ex);
            GenerateHeuristicRefinements(originalQuery, suggestions);
        }
    }

    private static void GenerateHeuristicRefinements(string originalQuery, QueryRefinementSuggestions suggestions)
    {
        // 동의어 추가
        var synonyms = GetSynonyms(originalQuery);
        if (synonyms.Count != 0)
        {
            var synonymQuery = $"{originalQuery} {string.Join(" ", synonyms.Take(2))}";
            suggestions.RefinedQueries.Add(new RefinedQuery
            {
                QueryText = synonymQuery,
                RefinementType = RefinementType.SynonymReplacement,
                Rationale = "Added synonyms for broader coverage",
                ExpectedImprovementScore = 0.6,
                RecommendedStrategy = SearchStrategy.Hybrid
            });
        }
    }

    private static List<string> ExtractQueryTerms(string query)
    {
        // 간단한 토큰화
        return query.Split(QueryTokenSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Where(term => term.Length > 2)
            .ToList();
    }

    private static double AssessTopicCoverage(string query, List<Document> documents)
    {
        var queryTerms = ExtractQueryTerms(query);
        if (queryTerms.Count == 0) return 0.0;

        var coveredTerms = new HashSet<string>();
        foreach (var doc in documents.Take(20))
        {
            foreach (var term in queryTerms)
            {
                if (doc.Content.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    coveredTerms.Add(term);
                }
            }
        }

        return (double)coveredTerms.Count / queryTerms.Count;
    }

    private static double CalculateTextSimilarity(string text1, string text2)
    {
        // 간단한 Jaccard 유사도
        var set1 = new HashSet<string>(text1.ToLowerInvariant().Split(' '));
        var set2 = new HashSet<string>(text2.ToLowerInvariant().Split(' '));
        
        var intersection = set1.Intersect(set2).Count();
        var union = set1.Union(set2).Count();
        
        return union == 0 ? 0.0 : (double)intersection / union;
    }

    private static List<int> FindDuplicateResults(List<Document> documents)
    {
        var duplicates = new List<int>();
        
        for (int i = 0; i < documents.Count; i++)
        {
            for (int j = i + 1; j < documents.Count; j++)
            {
                var similarity = CalculateTextSimilarity(documents[i].Content, documents[j].Content);
                if (similarity > 0.8) // 80% 이상 유사하면 중복으로 간주
                {
                    duplicates.Add(j);
                }
            }
        }
        
        return duplicates.Distinct().ToList();
    }

    private static string GeneralizeQuery(string query)
    {
        // 구체적인 용어를 일반적인 용어로 변경
        var generalizedQuery = query
            .Replace(" 구체적인 ", " ")
            .Replace(" 특정 ", " ")
            .Replace(" 정확한 ", " ");
            
        // 추가적인 일반화 로직 구현 가능
        return generalizedQuery.Trim();
    }

    private static string SpecifyQuery(string query)
    {
        // 쿼리를 더 구체적으로 만들기
        var specifiedQuery = query;
        
        // 시간 제약 추가
        if (!query.Contains("최근", StringComparison.OrdinalIgnoreCase) && !query.Contains("recent", StringComparison.OrdinalIgnoreCase))
        {
            specifiedQuery = $"최근 {query}";
        }
        
        return specifiedQuery;
    }

    private static string AddContextToQuery(string query)
    {
        // 쿼리에 컨텍스트 정보 추가
        return $"{query} 상세 정보 설명";
    }

    private static string RestructureQuery(string query)
    {
        // 쿼리 구조 변경 (단어 순서 바꾸기 등)
        var words = query.Split(' ');
        if (words.Length > 2)
        {
            // 간단한 재구성: 마지막 단어를 앞으로
            return $"{words.Last()} {string.Join(" ", words.Take(words.Length - 1))}";
        }
        
        return query;
    }

    private static List<string> GetSynonyms(string query)
    {
        // 간단한 동의어 사전
        var synonymDict = new Dictionary<string, List<string>>
        {
            ["AI"] = new() { "인공지능", "artificial intelligence", "machine learning" },
            ["computer"] = new() { "컴퓨터", "PC", "시스템" },
            ["software"] = new() { "소프트웨어", "프로그램", "애플리케이션" },
            ["technology"] = new() { "기술", "테크놀로지", "tech" }
        };

        var synonyms = new List<string>();
        foreach (var word in ExtractQueryTerms(query))
        {
            if (synonymDict.ContainsKey(word.ToLowerInvariant()))
            {
                synonyms.AddRange(synonymDict[word.ToLowerInvariant()].Take(1));
            }
        }

        return synonyms;
    }

    private static SearchStrategy GetAlternativeStrategy(SearchStrategy currentStrategy)
    {
        return currentStrategy switch
        {
            SearchStrategy.DirectVector => SearchStrategy.Hybrid,
            SearchStrategy.Hybrid => SearchStrategy.TwoStage,
            SearchStrategy.TwoStage => SearchStrategy.MultiQuery,
            SearchStrategy.MultiQuery => SearchStrategy.HyDE,
            SearchStrategy.HyDE => SearchStrategy.StepBack,
            _ => SearchStrategy.Hybrid
        };
    }

    private static QualityAssessment CreateFallbackAssessment(List<Document> documents)
    {
        // 기본적인 휴리스틱 평가
        return new QualityAssessment
        {
            ResultCount = documents.Count,
            OverallScore = Math.Min(documents.Count / 10.0, 1.0),
            RelevanceScore = 0.7,
            CompletenessScore = Math.Min(documents.Count / 10.0, 1.0),
            DiversityScore = 0.6,
            CredibilityScore = 0.8,
            FreshnessScore = 0.7,
            Issues = documents.Count < 5 ? new List<QualityIssue>
            {
                new QualityIssue
                {
                    Type = QualityIssueType.InsufficientResults,
                    Severity = 3,
                    Description = "Insufficient results for comprehensive analysis"
                }
            } : new List<QualityIssue>()
        };
    }

    private static void PopulateRationale(QualityAssessment assessment)
    {
        assessment.Rationale["relevance"] = $"Based on keyword matching: {assessment.RelevanceScore:F2}";
        assessment.Rationale["completeness"] = $"Based on result count and content length: {assessment.CompletenessScore:F2}";
        assessment.Rationale["diversity"] = $"Based on content similarity analysis: {assessment.DiversityScore:F2}";
        assessment.Rationale["credibility"] = $"Based on source metadata: {assessment.CredibilityScore:F2}";
        assessment.Rationale["freshness"] = $"Based on document timestamps: {assessment.FreshnessScore:F2}";
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting Self-RAG search for query: {Query}")]
    private static partial void LogSelfRAG14(ILogger logger, string query);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Self-RAG iteration {Iteration}/{MaxIterations}")]
    private static partial void LogSelfRAG13(ILogger logger, int iteration, int maxIterations);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Iteration {Iteration} completed: Quality={Quality:F2}, Results={Count}")]
    private static partial void LogSelfRAG12(ILogger logger, int iteration, double quality, double count);
    [LoggerMessage(Level = LogLevel.Information, Message = "Self-RAG completed successfully after {Iteration} iteration(s)")]
    private static partial void LogSelfRAG11(ILogger logger, int iteration);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Query refined for next iteration: {RefinedQuery}")]
    private static partial void LogSelfRAG10(ILogger logger, string refinedQuery);
    [LoggerMessage(Level = LogLevel.Error, Message = "Error in Self-RAG search")]
    private static partial void LogSelfRAG9(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Information, Message = "Self-RAG search completed: Success={Success}, Iterations={Count}, Quality={Quality:F2}, Time={Time}ms")]
    private static partial void LogSelfRAG8(ILogger logger, bool success, int count, double quality, double time);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Assessing quality of {Count} results for query: {Query}")]
    private static partial void LogSelfRAG7(ILogger logger, int count, string query);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Quality assessment completed: Overall={Overall:F2}, Relevance={Relevance:F2}, Issues={IssueCount}")]
    private static partial void LogSelfRAG6(ILogger logger, double overall, double relevance, double issueCount);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Error in quality assessment, using fallback evaluation")]
    private static partial void LogSelfRAG5(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Generating query refinement suggestions for: {Query}")]
    private static partial void LogSelfRAG4(ILogger logger, string query);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Generated {Count} query refinement suggestions")]
    private static partial void LogSelfRAG3(ILogger logger, int count);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Error generating query refinements")]
    private static partial void LogSelfRAG2(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to generate advanced refinements, using fallback")]
    private static partial void LogSelfRAG1(ILogger logger, Exception exception);

    #endregion
}
