using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Core.Services.SelfRAG;

/// <summary>
/// Self-RAG (Self-Reflective Retrieval Augmented Generation) 서비스 구현
/// </summary>
public class SelfRAGService : ISelfRAGService
{
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

        _logger.LogInformation("Starting Self-RAG search for query: {Query}", query);

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

                _logger.LogDebug("Self-RAG iteration {Iteration}/{MaxIterations}", iteration, options.MaxIterations);

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

                _logger.LogDebug("Iteration {Iteration} completed: Quality={Quality:F2}, Results={Count}",
                    iteration, qualityAssessment.OverallScore, searchResult.Documents.Count());

                // 3. 품질 임계값 확인
                if (qualityAssessment.OverallScore >= options.QualityThreshold &&
                    searchResult.Documents.Count() >= options.MinResults)
                {
                    result.FinalResults = searchResult.Documents;
                    result.FinalQualityScore = qualityAssessment.OverallScore;
                    result.IsSuccessful = true;
                    result.TerminationReason = $"Quality threshold reached ({qualityAssessment.OverallScore:F2})";

                    _logger.LogInformation("Self-RAG completed successfully after {Iteration} iteration(s)", iteration);
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
                    
                    if (refinementSuggestions.RefinedQueries.Any())
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

                        _logger.LogDebug("Query refined for next iteration: {RefinedQuery}", currentQuery);
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
            _logger.LogError(ex, "Error in Self-RAG search");
            result.IsSuccessful = false;
            result.TerminationReason = $"Error: {ex.Message}";
            throw;
        }
        finally
        {
            stopwatch.Stop();
            result.TotalProcessingTime = stopwatch.Elapsed;
            
            _logger.LogInformation("Self-RAG search completed: Success={Success}, Iterations={Count}, Quality={Quality:F2}, Time={Time}ms",
                result.IsSuccessful, result.Iterations.Count, result.FinalQualityScore, result.TotalProcessingTime.TotalMilliseconds);
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

        _logger.LogDebug("Assessing quality of {Count} results for query: {Query}", documents.Count, query);

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

            _logger.LogDebug("Quality assessment completed: Overall={Overall:F2}, Relevance={Relevance:F2}, Issues={IssueCount}",
                assessment.OverallScore, assessment.RelevanceScore, assessment.Issues.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error in quality assessment, using fallback evaluation");
            
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
        _logger.LogDebug("Generating query refinement suggestions for: {Query}", originalQuery);

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

            _logger.LogDebug("Generated {Count} query refinement suggestions", suggestions.RefinedQueries.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error generating query refinements");
        }

        return suggestions;
    }

    private async Task<double> AssessRelevanceAsync(string query, List<Document> documents, CancellationToken cancellationToken)
    {
        if (!documents.Any()) return 0.0;

        // 키워드 매칭 기반 관련성 평가
        var queryTerms = ExtractQueryTerms(query);
        var relevanceScores = new List<double>();

        foreach (var doc in documents)
        {
            var content = doc.Content.ToLowerInvariant();
            var matchingTerms = queryTerms.Count(term => content.Contains(term.ToLowerInvariant()));
            var relevance = queryTerms.Any() ? (double)matchingTerms / queryTerms.Count : 0.0;
            relevanceScores.Add(relevance);
        }

        await Task.CompletedTask;
        return relevanceScores.Average();
    }

    private async Task<double> AssessCompletenessAsync(string query, List<Document> documents, CancellationToken cancellationToken)
    {
        // 완전성을 결과 수와 내용 다양성으로 평가
        var completeness = 0.0;

        // 결과 개수 기반 점수
        var countScore = Math.Min(documents.Count / 10.0, 1.0); // 10개 이상이면 만점
        completeness += countScore * 0.4;

        // 내용 길이 기반 점수
        if (documents.Any())
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

    private double AssessDiversity(List<Document> documents)
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

        if (!similarities.Any()) return 1.0;

        // 낮은 유사성 = 높은 다양성
        var avgSimilarity = similarities.Average();
        return Math.Max(0.0, 1.0 - avgSimilarity);
    }

    private double AssessCredibility(List<Document> documents)
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

    private double AssessFreshness(List<Document> documents)
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

    private double CalculateOverallScore(QualityAssessment assessment)
    {
        // 가중 평균으로 전체 점수 계산
        return assessment.RelevanceScore * 0.35 +
               assessment.CompletenessScore * 0.25 +
               assessment.DiversityScore * 0.15 +
               assessment.CredibilityScore * 0.15 +
               assessment.FreshnessScore * 0.10;
    }

    private List<QualityIssue> IdentifyQualityIssues(string query, List<Document> documents, QualityAssessment assessment)
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
        if (duplicates.Any())
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

    private List<ImprovementSuggestion> GenerateImprovementSuggestions(QualityAssessment assessment)
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

    private async Task GenerateRefinementsForIssueAsync(
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

            var completion = await _textCompletion!.GenerateCompletionAsync(prompt, 500, 0.7f, cancellationToken);
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
            _logger.LogWarning(ex, "Failed to generate advanced refinements, using fallback");
            GenerateHeuristicRefinements(originalQuery, suggestions);
        }
    }

    private void GenerateHeuristicRefinements(string originalQuery, QueryRefinementSuggestions suggestions)
    {
        // 동의어 추가
        var synonyms = GetSynonyms(originalQuery);
        if (synonyms.Any())
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

    private List<string> ExtractQueryTerms(string query)
    {
        // 간단한 토큰화
        return query.Split(new[] { ' ', '\t', '\n', ',', '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(term => term.Length > 2)
            .ToList();
    }

    private double AssessTopicCoverage(string query, List<Document> documents)
    {
        var queryTerms = ExtractQueryTerms(query);
        if (!queryTerms.Any()) return 0.0;

        var coveredTerms = new HashSet<string>();
        foreach (var doc in documents.Take(20))
        {
            foreach (var term in queryTerms)
            {
                if (doc.Content.ToLowerInvariant().Contains(term.ToLowerInvariant()))
                {
                    coveredTerms.Add(term);
                }
            }
        }

        return (double)coveredTerms.Count / queryTerms.Count;
    }

    private double CalculateTextSimilarity(string text1, string text2)
    {
        // 간단한 Jaccard 유사도
        var set1 = new HashSet<string>(text1.ToLowerInvariant().Split(' '));
        var set2 = new HashSet<string>(text2.ToLowerInvariant().Split(' '));
        
        var intersection = set1.Intersect(set2).Count();
        var union = set1.Union(set2).Count();
        
        return union == 0 ? 0.0 : (double)intersection / union;
    }

    private List<int> FindDuplicateResults(List<Document> documents)
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

    private string GeneralizeQuery(string query)
    {
        // 구체적인 용어를 일반적인 용어로 변경
        var generalizedQuery = query
            .Replace(" 구체적인 ", " ")
            .Replace(" 특정 ", " ")
            .Replace(" 정확한 ", " ");
            
        // 추가적인 일반화 로직 구현 가능
        return generalizedQuery.Trim();
    }

    private string SpecifyQuery(string query)
    {
        // 쿼리를 더 구체적으로 만들기
        var specifiedQuery = query;
        
        // 시간 제약 추가
        if (!query.ToLowerInvariant().Contains("최근") && !query.ToLowerInvariant().Contains("recent"))
        {
            specifiedQuery = $"최근 {query}";
        }
        
        return specifiedQuery;
    }

    private string AddContextToQuery(string query)
    {
        // 쿼리에 컨텍스트 정보 추가
        return $"{query} 상세 정보 설명";
    }

    private string RestructureQuery(string query)
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

    private List<string> GetSynonyms(string query)
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

    private SearchStrategy GetAlternativeStrategy(SearchStrategy currentStrategy)
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

    private QualityAssessment CreateFallbackAssessment(List<Document> documents)
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

    private void PopulateRationale(QualityAssessment assessment)
    {
        assessment.Rationale["relevance"] = $"Based on keyword matching: {assessment.RelevanceScore:F2}";
        assessment.Rationale["completeness"] = $"Based on result count and content length: {assessment.CompletenessScore:F2}";
        assessment.Rationale["diversity"] = $"Based on content similarity analysis: {assessment.DiversityScore:F2}";
        assessment.Rationale["credibility"] = $"Based on source metadata: {assessment.CredibilityScore:F2}";
        assessment.Rationale["freshness"] = $"Based on document timestamps: {assessment.FreshnessScore:F2}";
    }
}