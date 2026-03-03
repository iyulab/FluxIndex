using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Use explicit SearchStrategy from Application.Interfaces to avoid ambiguity
using SearchStrategy = FluxIndex.Core.Application.Interfaces.SearchStrategy;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// Self-RAG (Self-Reflective Retrieval Augmented Generation) Service Implementation.
/// Implements iterative retrieval with quality assessment and automatic query refinement.
/// </summary>
public partial class SelfRAGService : ISelfRAGService
{
    private readonly IHybridSearchService _searchService;
    private readonly IEmbeddingService _embeddingService;
    private readonly ITextCompletionService? _completionService;
    private readonly SelfRAGServiceOptions _options;
    private readonly ILogger<SelfRAGService> _logger;

    public SelfRAGService(
        IHybridSearchService searchService,
        IEmbeddingService embeddingService,
        ITextCompletionService? completionService,
        IOptions<SelfRAGServiceOptions> options,
        ILogger<SelfRAGService> logger)
    {
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _completionService = completionService;
        _options = options?.Value ?? new SelfRAGServiceOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<SelfRAGResult> SearchAsync(
        string query,
        SelfRAGOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var opts = options ?? new SelfRAGOptions();
        var stopwatch = Stopwatch.StartNew();
        var result = new SelfRAGResult
        {
            Metadata = new Dictionary<string, object>
            {
                ["original_query"] = query,
                ["options"] = opts
            }
        };

        try
        {
            var currentQuery = query;
            var allResults = new List<Document>();
            var iterations = new List<SearchIteration>();
            QualityAssessment? lastAssessment = null;

            for (int i = 0; i < opts.MaxIterations; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_logger.IsEnabled(LogLevel.Debug))
                    LogSelfRAG7(_logger, i + 1, currentQuery);

                // Determine search strategy for this iteration
                var strategy = DetermineSearchStrategy(currentQuery, lastAssessment, i);

                // Perform search
                var iterationStart = Stopwatch.StartNew();
                var searchResults = await ExecuteSearchAsync(currentQuery, strategy, opts, cancellationToken);
                iterationStart.Stop();

                // Assess quality
                lastAssessment = await AssessResultQualityAsync(currentQuery, searchResults, cancellationToken);

                // Create iteration record
                var iteration = new SearchIteration
                {
                    IterationNumber = i + 1,
                    Query = currentQuery,
                    Strategy = strategy,
                    Results = searchResults,
                    QualityAssessment = lastAssessment,
                    ProcessingTime = iterationStart.Elapsed
                };

                iterations.Add(iteration);

                // Log refinement action
                result.RefinementActions.Add(CreateRefinementAction(
                    RefinementActionType.QualityAssessment,
                    $"Assessed quality: {lastAssessment.OverallScore:F2}"));

                // Check if quality threshold is met
                if (lastAssessment.OverallScore >= opts.QualityThreshold)
                {
                    if (_logger.IsEnabled(LogLevel.Debug))
                        LogSelfRAG6(_logger, i + 1, lastAssessment.OverallScore, opts.QualityThreshold);

                    iteration.ImprovementNotes.Add("Quality threshold met - stopping iteration");
                    allResults.AddRange(searchResults);
                    result.TerminationReason = "Quality threshold met";
                    break;
                }

                // Accumulate results
                allResults.AddRange(searchResults);

                // Check if this is the last iteration
                if (i == opts.MaxIterations - 1)
                {
                    iteration.ImprovementNotes.Add("Maximum iterations reached");
                    result.TerminationReason = "Maximum iterations reached";
                    break;
                }

                // Try to refine query if auto-refinement is enabled
                if (opts.EnableAutoRefinement)
                {
                    var refinements = await SuggestQueryRefinementsAsync(currentQuery, lastAssessment, cancellationToken);

                    if (refinements.RefinedQueries.Count != 0)
                    {
                        var bestRefinement = refinements.RefinedQueries
                            .OrderByDescending(r => r.ExpectedImprovementScore)
                            .First();

                        currentQuery = bestRefinement.QueryText;
                        iteration.NextIterationPlan = $"Refined query: {currentQuery} (reason: {bestRefinement.Rationale})";

                        result.RefinementActions.Add(CreateRefinementAction(
                            RefinementActionType.QueryRefinement,
                            $"Refined query from '{query}' to '{currentQuery}'"));

                        if (_logger.IsEnabled(LogLevel.Debug))
                            LogSelfRAG5(_logger, currentQuery);
                    }
                    else
                    {
                        iteration.ImprovementNotes.Add("No further refinements available");
                        result.TerminationReason = "No further refinements available";
                        break;
                    }
                }
            }

            // Deduplicate and rank final results
            var finalResults = DeduplicateAndRankResults(allResults, opts);

            stopwatch.Stop();

            result.FinalResults = finalResults.Take(opts.MaxResults);
            result.Iterations = iterations;
            result.FinalQualityScore = lastAssessment?.OverallScore ?? 0;
            result.TotalProcessingTime = stopwatch.Elapsed;
            result.IsSuccessful = finalResults.Any() && (lastAssessment?.OverallScore ?? 0) >= opts.QualityThreshold * 0.7;

            var resultCount = finalResults.Count();
            LogSelfRAG4(_logger, resultCount, iterations.Count, result.FinalQualityScore);

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                LogSelfRAG3(_logger, ex, query);
            stopwatch.Stop();

            result.IsSuccessful = false;
            result.TerminationReason = $"Error: {ex.Message}";
            result.TotalProcessingTime = stopwatch.Elapsed;

            return result;
        }
    }

    /// <inheritdoc />
    public async Task<QualityAssessment> AssessResultQualityAsync(
        string query,
        IEnumerable<Document> results,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(results);

        var resultList = results.ToList();
        var assessment = new QualityAssessment
        {
            ResultCount = resultList.Count
        };

        try
        {
            // Calculate relevance score using embedding similarity
            var relevanceScore = await CalculateRelevanceScoreAsync(query, resultList, cancellationToken);
            assessment.RelevanceScore = relevanceScore;

            // Calculate completeness score
            assessment.CompletenessScore = CalculateCompletenessScore(query, resultList);

            // Calculate diversity score
            assessment.DiversityScore = await CalculateDiversityScoreAsync(resultList, cancellationToken);

            // Calculate credibility score
            assessment.CredibilityScore = CalculateCredibilityScore(resultList);

            // Calculate freshness score
            assessment.FreshnessScore = CalculateFreshnessScore(resultList);

            // Calculate overall score (weighted average)
            assessment.OverallScore = CalculateOverallScore(assessment);

            // Identify issues
            assessment.Issues = IdentifyQualityIssues(assessment, resultList);

            // Generate suggestions
            assessment.Suggestions = GenerateImprovementSuggestions(assessment);

            // Build rationale
            assessment.Rationale = BuildAssessmentRationale(assessment);

            return assessment;
        }
        catch (Exception ex)
        {
            LogSelfRAG2(_logger, ex);
            return assessment;
        }
    }

    /// <inheritdoc />
    public async Task<QueryRefinementSuggestions> SuggestQueryRefinementsAsync(
        string originalQuery,
        QualityAssessment assessment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalQuery);
        ArgumentNullException.ThrowIfNull(assessment);

        var suggestions = new QueryRefinementSuggestions
        {
            OriginalQuery = originalQuery
        };

        try
        {
            // Generate refined queries based on issues
            foreach (var issue in assessment.Issues)
            {
                var refinements = GenerateRefinementsForIssue(originalQuery, issue);
                suggestions.RefinedQueries.AddRange(refinements);
            }

            // Use LLM for query expansion if available
            if (_completionService != null && _options.UseLlmForRefinement)
            {
                var llmRefinements = await GenerateLlmRefinementsAsync(
                    originalQuery, assessment, cancellationToken);
                suggestions.RefinedQueries.AddRange(llmRefinements);
            }

            // Add keyword suggestions based on query analysis
            suggestions.SuggestedKeywords = ExtractSuggestedKeywords(originalQuery, assessment);

            // Identify keywords to exclude
            suggestions.KeywordsToExclude = IdentifyKeywordsToExclude(originalQuery, assessment);

            // Suggest alternative strategies
            suggestions.AlternativeStrategies = SuggestAlternativeStrategies(assessment);

            // Add context expansions
            if (assessment.CompletenessScore < 0.6)
            {
                suggestions.ContextExpansions = GenerateContextExpansions(originalQuery);
            }

            // Remove duplicates and sort by expected improvement
            suggestions.RefinedQueries = suggestions.RefinedQueries
                .GroupBy(r => r.QueryText)
                .Select(g => g.First())
                .OrderByDescending(r => r.ExpectedImprovementScore)
                .Take(5)
                .ToList();

            return suggestions;
        }
        catch (Exception ex)
        {
            LogSelfRAG1(_logger, ex);
            return suggestions;
        }
    }

    #region Private Helper Methods

    private static SearchStrategy DetermineSearchStrategy(string query, QualityAssessment? lastAssessment, int iteration)
    {
        // First iteration - use hybrid search
        if (iteration == 0 || lastAssessment == null)
        {
            return SearchStrategy.Hybrid;
        }

        // Low relevance - try MultiQuery for broader coverage
        if (lastAssessment.RelevanceScore < 0.5)
        {
            return SearchStrategy.MultiQuery;
        }

        // Low diversity - try StepBack for different perspective
        if (lastAssessment.DiversityScore < 0.5)
        {
            return SearchStrategy.StepBack;
        }

        // Complex query - use HyDE for hypothetical document embedding
        if (query.Split(' ').Length > 10)
        {
            return SearchStrategy.HyDE;
        }

        // Default to TwoStage with reranking
        return SearchStrategy.TwoStage;
    }

    private async Task<IEnumerable<Document>> ExecuteSearchAsync(
        string query,
        SearchStrategy strategy,
        SelfRAGOptions opts,
        CancellationToken cancellationToken)
    {
        // Configure search options based on strategy
        var searchOptions = new HybridSearchOptions
        {
            MaxResults = opts.MaxResults * 2, // Get more results for filtering
            EnableDiversity = true
        };

        // Adjust weights based on strategy
        switch (strategy)
        {
            case SearchStrategy.DirectVector:
                searchOptions.VectorWeight = 0.9;
                searchOptions.SparseWeight = 0.1;
                break;
            case SearchStrategy.KeywordOnly:
                searchOptions.VectorWeight = 0.2;
                searchOptions.SparseWeight = 0.8;
                break;
            case SearchStrategy.MultiQuery:
            case SearchStrategy.HyDE:
            case SearchStrategy.StepBack:
                // These advanced strategies use vector-heavy search
                searchOptions.VectorWeight = 0.8;
                searchOptions.SparseWeight = 0.2;
                break;
            case SearchStrategy.TwoStage:
                // Two-stage uses balanced approach with reranking
                searchOptions.VectorWeight = 0.6;
                searchOptions.SparseWeight = 0.4;
                break;
            case SearchStrategy.Adaptive:
                searchOptions.EnableAutoStrategy = true;
                break;
            case SearchStrategy.Hybrid:
            default:
                searchOptions.VectorWeight = 0.7;
                searchOptions.SparseWeight = 0.3;
                break;
        }

        var hybridResults = await _searchService.SearchAsync(query, searchOptions, cancellationToken);

        // Convert HybridSearchResults to Documents
        return hybridResults
            .GroupBy(r => r.Chunk.DocumentId)
            .Select(g => new Document
            {
                Id = g.Key,
                FileName = g.First().Chunk.Metadata?.GetValueOrDefault("title")?.ToString() ?? string.Empty,
                Content = string.Join("\n", g.Select(r => r.Chunk.Content))
            })
            .Take(opts.MaxResults);
    }

    private async Task<double> CalculateRelevanceScoreAsync(
        string query,
        List<Document> results,
        CancellationToken cancellationToken)
    {
        if (results.Count == 0) return 0.0;

        try
        {
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
            var scores = new List<double>();

            foreach (var doc in results.Take(10)) // Sample first 10 for efficiency
            {
                var content = doc.Content.Length > 1000 ? doc.Content[..1000] : doc.Content;
                var docEmbedding = await _embeddingService.GenerateEmbeddingAsync(content, cancellationToken);
                var similarity = CosineSimilarity(queryEmbedding, docEmbedding);
                scores.Add(similarity);
            }

            return scores.Count != 0 ? scores.Average() : 0.0;
        }
        catch
        {
            return 0.5; // Default score on error
        }
    }

    private static double CalculateCompletenessScore(string query, List<Document> results)
    {
        if (results.Count == 0) return 0.0;

        // Extract query terms
        var queryTerms = ExtractQueryTerms(query);
        if (queryTerms.Count == 0) return 1.0;

        // Calculate term coverage
        var combinedContent = string.Join(" ", results.Select(r => r.Content.ToLowerInvariant()));
        var coveredTerms = queryTerms.Count(term =>
            combinedContent.Contains(term, StringComparison.OrdinalIgnoreCase));

        var termCoverage = (double)coveredTerms / queryTerms.Count;

        // Factor in result count
        var resultCountScore = Math.Min(1.0, results.Count / 5.0);

        return (termCoverage * 0.7) + (resultCountScore * 0.3);
    }

    private async Task<double> CalculateDiversityScoreAsync(List<Document> results, CancellationToken cancellationToken)
    {
        if (results.Count < 2) return 1.0;

        try
        {
            var embeddings = new List<float[]>();
            foreach (var doc in results.Take(10))
            {
                var content = doc.Content.Length > 500 ? doc.Content[..500] : doc.Content;
                var embedding = await _embeddingService.GenerateEmbeddingAsync(content, cancellationToken);
                embeddings.Add(embedding);
            }

            // Calculate average pairwise distance
            var totalDistance = 0.0;
            var count = 0;

            for (int i = 0; i < embeddings.Count; i++)
            {
                for (int j = i + 1; j < embeddings.Count; j++)
                {
                    totalDistance += 1 - CosineSimilarity(embeddings[i], embeddings[j]);
                    count++;
                }
            }

            return count > 0 ? totalDistance / count : 1.0;
        }
        catch
        {
            return 0.5; // Default on error
        }
    }

    private static double CalculateCredibilityScore(List<Document> results)
    {
        if (results.Count == 0) return 0.0;

        var scores = results.Select(doc =>
        {
            double score = 0.5; // Base score

            // Check for file path metadata
            if (!string.IsNullOrEmpty(doc.FilePath))
                score += 0.1;

            // Check for filename quality
            if (!string.IsNullOrEmpty(doc.FileName) && doc.FileName.Length > 10)
                score += 0.1;

            // Check for content length (substantial content)
            if (doc.Content.Length > 200)
                score += 0.1;

            // Check for structured content
            if (doc.Content.Contains('\n') || doc.Content.Contains('.'))
                score += 0.1;

            return Math.Min(1.0, score);
        });

        return scores.Average();
    }

    private static double CalculateFreshnessScore(List<Document> results)
    {
        if (results.Count == 0) return 0.5;

        var now = DateTimeOffset.UtcNow;
        var scores = results.Select(doc =>
        {
            // Use CreatedAt if available
            if (doc.CreatedAt != default)
            {
                var age = now - doc.CreatedAt;
                if (age.TotalDays < 30) return 1.0;
                if (age.TotalDays < 90) return 0.8;
                if (age.TotalDays < 365) return 0.6;
                return 0.4;
            }
            return 0.5; // Unknown age
        });

        return scores.Average();
    }

    private double CalculateOverallScore(QualityAssessment assessment)
    {
        // Weighted average with relevance being most important
        return (assessment.RelevanceScore * _options.RelevanceWeight) +
               (assessment.CompletenessScore * _options.CompletenessWeight) +
               (assessment.DiversityScore * _options.DiversityWeight) +
               (assessment.CredibilityScore * _options.CredibilityWeight) +
               (assessment.FreshnessScore * _options.FreshnessWeight);
    }

    private static List<QualityIssue> IdentifyQualityIssues(QualityAssessment assessment, List<Document> results)
    {
        var issues = new List<QualityIssue>();

        if (assessment.RelevanceScore < 0.5)
        {
            issues.Add(new QualityIssue
            {
                Type = QualityIssueType.InsufficientRelevance,
                Severity = assessment.RelevanceScore < 0.3 ? 5 : 3,
                Description = "Search results have low relevance to the query",
                RecommendedAction = "Refine query with more specific terms"
            });
        }

        if (results.Count < 3)
        {
            issues.Add(new QualityIssue
            {
                Type = QualityIssueType.InsufficientResults,
                Severity = results.Count == 0 ? 5 : 3,
                Description = $"Only {results.Count} results found",
                RecommendedAction = "Broaden search terms or check index coverage"
            });
        }

        if (assessment.DiversityScore < 0.3)
        {
            issues.Add(new QualityIssue
            {
                Type = QualityIssueType.LackOfDiversity,
                Severity = 3,
                Description = "Results are too similar to each other",
                RecommendedAction = "Use multi-perspective search or expand query"
            });
        }

        if (assessment.CompletenessScore < 0.5)
        {
            issues.Add(new QualityIssue
            {
                Type = QualityIssueType.IncompleteAnswer,
                Severity = 3,
                Description = "Results may not fully cover the query",
                RecommendedAction = "Add more specific search terms"
            });
        }

        if (assessment.FreshnessScore < 0.4)
        {
            issues.Add(new QualityIssue
            {
                Type = QualityIssueType.OutdatedInformation,
                Severity = 2,
                Description = "Some results may contain outdated information",
                RecommendedAction = "Filter by date or seek more recent sources"
            });
        }

        return issues;
    }

    private static List<ImprovementSuggestion> GenerateImprovementSuggestions(QualityAssessment assessment)
    {
        var suggestions = new List<ImprovementSuggestion>();

        if (assessment.RelevanceScore < 0.6)
        {
            suggestions.Add(new ImprovementSuggestion
            {
                Type = ImprovementType.QueryModification,
                Priority = 5,
                Suggestion = "Add more specific keywords to the query",
                ExpectedImpact = "Improved result relevance",
                Complexity = ImplementationComplexity.Low
            });
        }

        if (assessment.DiversityScore < 0.5)
        {
            suggestions.Add(new ImprovementSuggestion
            {
                Type = ImprovementType.ExpandSearch,
                Priority = 3,
                Suggestion = "Use synonym expansion or multi-perspective search",
                ExpectedImpact = "More diverse results",
                Complexity = ImplementationComplexity.Medium
            });
        }

        if (assessment.CompletenessScore < 0.6)
        {
            suggestions.Add(new ImprovementSuggestion
            {
                Type = ImprovementType.ContextExpansion,
                Priority = 4,
                Suggestion = "Expand search with related concepts",
                ExpectedImpact = "Better query coverage",
                Complexity = ImplementationComplexity.Medium
            });
        }

        return suggestions.OrderByDescending(s => s.Priority).ToList();
    }

    private static Dictionary<string, string> BuildAssessmentRationale(QualityAssessment assessment)
    {
        return new Dictionary<string, string>
        {
            ["relevance"] = $"Semantic similarity: {assessment.RelevanceScore:F2}",
            ["completeness"] = $"Query term coverage: {assessment.CompletenessScore:F2}",
            ["diversity"] = $"Result variety: {assessment.DiversityScore:F2}",
            ["credibility"] = $"Source quality: {assessment.CredibilityScore:F2}",
            ["freshness"] = $"Information recency: {assessment.FreshnessScore:F2}",
            ["overall"] = $"Weighted average: {assessment.OverallScore:F2}"
        };
    }

    private static List<RefinedQuery> GenerateRefinementsForIssue(string originalQuery, QualityIssue issue)
    {
        var refinements = new List<RefinedQuery>();

        switch (issue.Type)
        {
            case QualityIssueType.InsufficientRelevance:
                // Add specificity
                refinements.Add(new RefinedQuery
                {
                    QueryText = $"{originalQuery} detailed explanation",
                    RefinementType = RefinementType.Specification,
                    Rationale = "Added specificity to improve relevance",
                    ExpectedImprovementScore = 0.7,
                    RecommendedStrategy = SearchStrategy.Hybrid
                });
                break;

            case QualityIssueType.InsufficientResults:
                // Generalize query
                var generalizedQuery = GeneralizeQuery(originalQuery);
                refinements.Add(new RefinedQuery
                {
                    QueryText = generalizedQuery,
                    RefinementType = RefinementType.Generalization,
                    Rationale = "Generalized query to find more results",
                    ExpectedImprovementScore = 0.6,
                    RecommendedStrategy = SearchStrategy.Adaptive
                });
                break;

            case QualityIssueType.LackOfDiversity:
                // Try multi-perspective
                refinements.Add(new RefinedQuery
                {
                    QueryText = $"{originalQuery} alternatives comparison",
                    RefinementType = RefinementType.Restructuring,
                    Rationale = "Restructured to seek diverse perspectives",
                    ExpectedImprovementScore = 0.65,
                    RecommendedStrategy = SearchStrategy.KeywordOnly
                });
                break;
        }

        return refinements;
    }

    private async Task<List<RefinedQuery>> GenerateLlmRefinementsAsync(
        string originalQuery,
        QualityAssessment assessment,
        CancellationToken cancellationToken)
    {
        if (_completionService == null)
            return new List<RefinedQuery>();

        try
        {
            var prompt = $$"""
                Given the original query: "{{originalQuery}}"
                And quality assessment:
                - Relevance: {{assessment.RelevanceScore:F2}}
                - Completeness: {{assessment.CompletenessScore:F2}}
                - Diversity: {{assessment.DiversityScore:F2}}

                Suggest 2-3 refined queries that could improve search results.
                Return as JSON array: [{"query": "...", "rationale": "..."}]
                """;

            var response = await _completionService.CompleteJsonAsync(prompt, new Flux.Abstractions.TextCompletionOptions { MaxTokens = 500 }, cancellationToken);

            // Parse response and create refined queries
            // This is simplified - actual implementation would properly parse JSON
            return new List<RefinedQuery>
            {
                new RefinedQuery
                {
                    QueryText = originalQuery + " comprehensive guide",
                    RefinementType = RefinementType.ContextAddition,
                    Rationale = "LLM-suggested expansion",
                    ExpectedImprovementScore = 0.6,
                    RecommendedStrategy = SearchStrategy.Hybrid
                }
            };
        }
        catch
        {
            return new List<RefinedQuery>();
        }
    }

    private static List<string> ExtractSuggestedKeywords(string query, QualityAssessment assessment)
    {
        var keywords = new List<string>();

        // Extract potential expansion keywords based on query analysis
        var queryTerms = ExtractQueryTerms(query);

        // Add related terms (simplified - could use a thesaurus or LLM)
        foreach (var term in queryTerms.Take(3))
        {
            keywords.Add($"related:{term}");
        }

        return keywords;
    }

    private static List<string> IdentifyKeywordsToExclude(string query, QualityAssessment assessment)
    {
        // Identify potentially noisy keywords
        var stopWords = new[] { "the", "a", "an", "is", "are", "was", "were" };
        return stopWords.Where(w => query.Contains(w, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static List<SearchStrategy> SuggestAlternativeStrategies(QualityAssessment assessment)
    {
        var strategies = new List<SearchStrategy>();

        if (assessment.RelevanceScore < 0.5)
            strategies.Add(SearchStrategy.DirectVector);

        if (assessment.DiversityScore < 0.5)
            strategies.Add(SearchStrategy.Adaptive);

        if (assessment.CompletenessScore < 0.5)
            strategies.Add(SearchStrategy.KeywordOnly);

        return strategies;
    }

    private static List<string> GenerateContextExpansions(string query)
    {
        // Generate context expansion suggestions
        return new List<string>
        {
            $"What is {query}",
            $"Examples of {query}",
            $"{query} best practices"
        };
    }

    private static string GeneralizeQuery(string query)
    {
        // Remove specific terms to generalize
        var words = query.Split(' ').ToList();
        if (words.Count > 3)
        {
            // Keep only main terms
            return string.Join(" ", words.Take(words.Count / 2 + 1));
        }
        return query;
    }

    private static List<string> ExtractQueryTerms(string query)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "is", "are", "was", "were", "be", "been", "being",
            "have", "has", "had", "do", "does", "did", "will", "would", "could", "should",
            "may", "might", "must", "shall", "can", "need", "dare", "ought", "used",
            "to", "of", "in", "for", "on", "with", "at", "by", "from", "or", "and"
        };

        return Regex.Split(query.ToLowerInvariant(), @"\W+")
            .Where(w => !string.IsNullOrEmpty(w) && w.Length > 2 && !stopWords.Contains(w))
            .Distinct()
            .ToList();
    }

    private static IEnumerable<Document> DeduplicateAndRankResults(List<Document> results, SelfRAGOptions opts)
    {
        // Deduplicate by content similarity
        var uniqueResults = new List<Document>();

        foreach (var doc in results)
        {
            var isDuplicate = uniqueResults.Any(existing =>
                GetContentSimilarity(existing.Content, doc.Content) > 0.9);

            if (!isDuplicate)
            {
                uniqueResults.Add(doc);
            }
        }

        // Sort by some ranking criteria (simplified)
        return uniqueResults.Take(opts.MaxResults);
    }

    private static double GetContentSimilarity(string content1, string content2)
    {
        if (string.IsNullOrEmpty(content1) || string.IsNullOrEmpty(content2))
            return 0;

        var words1 = new HashSet<string>(content1.ToLowerInvariant().Split(' '));
        var words2 = new HashSet<string>(content2.ToLowerInvariant().Split(' '));

        var intersection = words1.Intersect(words2).Count();
        var union = words1.Union(words2).Count();

        return union > 0 ? (double)intersection / union : 0;
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0;

        double dotProduct = 0;
        double normA = 0;
        double normB = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denominator > 0 ? dotProduct / denominator : 0;
    }

    private static RefinementAction CreateRefinementAction(RefinementActionType type, string description)
    {
        return new RefinementAction
        {
            ActionType = type,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow,
            Description = description,
            IsSuccessful = true
        };
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Starting iteration {Iteration} with query: {Query}")]
    private static partial void LogSelfRAG7(ILogger logger, int iteration, string query);
    [LoggerMessage(Level = LogLevel.Information, Message = "Quality threshold met at iteration {Iteration}: {Score:F2} >= {Threshold:F2}")]
    private static partial void LogSelfRAG6(ILogger logger, int iteration, double score, double threshold);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Query refined to: {Query}")]
    private static partial void LogSelfRAG5(ILogger logger, string query);
    [LoggerMessage(Level = LogLevel.Information, Message = "Self-RAG search completed: {ResultCount} results, {IterationCount} iterations, quality: {Quality:F2}")]
    private static partial void LogSelfRAG4(ILogger logger, int resultCount, int iterationCount, double quality);
    [LoggerMessage(Level = LogLevel.Error, Message = "Self-RAG search failed for query: {Query}")]
    private static partial void LogSelfRAG3(ILogger logger, Exception exception, string query);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Error assessing result quality")]
    private static partial void LogSelfRAG2(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Error generating query refinements")]
    private static partial void LogSelfRAG1(ILogger logger, Exception exception);

    #endregion
}

/// <summary>
/// Configuration options for SelfRAGService.
/// </summary>
public partial class SelfRAGServiceOptions
{
    /// <summary>
    /// Whether to use LLM for query refinement suggestions.
    /// </summary>
    public bool UseLlmForRefinement { get; set; } = true;

    /// <summary>
    /// Default maximum iterations for search.
    /// </summary>
    public int DefaultMaxIterations { get; set; } = 3;

    /// <summary>
    /// Default quality threshold.
    /// </summary>
    public double DefaultQualityThreshold { get; set; } = 0.7;

    /// <summary>
    /// Weight for relevance in overall score calculation.
    /// </summary>
    public double RelevanceWeight { get; set; } = 0.35;

    /// <summary>
    /// Weight for completeness in overall score calculation.
    /// </summary>
    public double CompletenessWeight { get; set; } = 0.25;

    /// <summary>
    /// Weight for diversity in overall score calculation.
    /// </summary>
    public double DiversityWeight { get; set; } = 0.15;

    /// <summary>
    /// Weight for credibility in overall score calculation.
    /// </summary>
    public double CredibilityWeight { get; set; } = 0.15;

    /// <summary>
    /// Weight for freshness in overall score calculation.
    /// </summary>
    public double FreshnessWeight { get; set; } = 0.10;
}
