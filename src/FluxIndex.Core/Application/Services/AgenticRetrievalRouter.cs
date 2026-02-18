using System;
using System.Collections.Concurrent;
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
using System.Globalization;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// Agentic Retrieval Router implementation.
/// Intelligently routes queries to the most appropriate retrieval strategy.
/// </summary>
public partial class AgenticRetrievalRouter : IAgenticRetrievalRouter
{
    private static readonly char[] QuerySplitSeparators = [' ', ',', '.', '?', '!'];
    private static readonly string[] ConjunctionSeparators = [" and ", " AND "];

    private readonly IHybridSearchService _hybridSearchService;
    private readonly ISelfRAGService? _selfRAGService;
    private readonly ICorrectiveRAGService? _correctiveRAGService;
    private readonly ISmallToBigRetriever? _smallToBigRetriever;
    private readonly IIterativeRetrievalService? _iterativeRetrievalService;
    private readonly IEmbeddingService _embeddingService;
    private readonly AgenticRetrievalRouterOptions _options;
    private readonly ILogger<AgenticRetrievalRouter> _logger;

    private readonly ConcurrentDictionary<string, RoutingFeedback> _feedbackHistory = new();
    private readonly ConcurrentDictionary<RetrievalStrategy, StrategyPerformance> _strategyPerformance = new();

    public AgenticRetrievalRouter(
        IHybridSearchService hybridSearchService,
        IEmbeddingService embeddingService,
        ISelfRAGService? selfRAGService,
        ICorrectiveRAGService? correctiveRAGService,
        ISmallToBigRetriever? smallToBigRetriever,
        IIterativeRetrievalService? iterativeRetrievalService,
        IOptions<AgenticRetrievalRouterOptions> options,
        ILogger<AgenticRetrievalRouter> logger)
    {
        _hybridSearchService = hybridSearchService ?? throw new ArgumentNullException(nameof(hybridSearchService));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _selfRAGService = selfRAGService;
        _correctiveRAGService = correctiveRAGService;
        _smallToBigRetriever = smallToBigRetriever;
        _iterativeRetrievalService = iterativeRetrievalService;
        _options = options?.Value ?? new AgenticRetrievalRouterOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        InitializeStrategyPerformance();
    }

    private void InitializeStrategyPerformance()
    {
        foreach (var strategy in Enum.GetValues<RetrievalStrategy>())
        {
            _strategyPerformance[strategy] = new StrategyPerformance
            {
                Strategy = strategy,
                AverageLatency = EstimateBaseLatency(strategy),
                AverageQuality = EstimateBaseQuality(strategy),
                SuccessRate = 0.9
            };
        }
    }

    private static TimeSpan EstimateBaseLatency(RetrievalStrategy strategy)
    {
        return strategy switch
        {
            RetrievalStrategy.KeywordSearch => TimeSpan.FromMilliseconds(50),
            RetrievalStrategy.SemanticSearch => TimeSpan.FromMilliseconds(100),
            RetrievalStrategy.HybridSearch => TimeSpan.FromMilliseconds(150),
            RetrievalStrategy.SmallToBig => TimeSpan.FromMilliseconds(200),
            RetrievalStrategy.SelfRAG => TimeSpan.FromMilliseconds(500),
            RetrievalStrategy.CorrectiveRAG => TimeSpan.FromMilliseconds(600),
            RetrievalStrategy.MultiHopRetrieval => TimeSpan.FromMilliseconds(800),
            RetrievalStrategy.IterativeRetrieval => TimeSpan.FromMilliseconds(1000),
            RetrievalStrategy.QueryDecomposition => TimeSpan.FromMilliseconds(1200),
            RetrievalStrategy.GraphTraversal => TimeSpan.FromMilliseconds(400),
            RetrievalStrategy.Ensemble => TimeSpan.FromMilliseconds(1500),
            _ => TimeSpan.FromMilliseconds(200)
        };
    }

    private static double EstimateBaseQuality(RetrievalStrategy strategy)
    {
        return strategy switch
        {
            RetrievalStrategy.KeywordSearch => 0.65,
            RetrievalStrategy.SemanticSearch => 0.75,
            RetrievalStrategy.HybridSearch => 0.80,
            RetrievalStrategy.SmallToBig => 0.82,
            RetrievalStrategy.SelfRAG => 0.88,
            RetrievalStrategy.CorrectiveRAG => 0.90,
            RetrievalStrategy.MultiHopRetrieval => 0.85,
            RetrievalStrategy.IterativeRetrieval => 0.87,
            RetrievalStrategy.QueryDecomposition => 0.88,
            RetrievalStrategy.GraphTraversal => 0.78,
            RetrievalStrategy.Ensemble => 0.92,
            _ => 0.75
        };
    }

    /// <inheritdoc />
    public async Task<RoutingResult> RouteAndRetrieveAsync(
        string query,
        RoutingContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var totalStopwatch = Stopwatch.StartNew();
        var routingStopwatch = Stopwatch.StartNew();

        try
        {
            LogRoutingQuery(_logger, query);

            // Step 1: Analyze query and make routing decision
            var decision = await AnalyzeQueryAsync(query, context, cancellationToken);
            routingStopwatch.Stop();

            // Step 2: Execute retrieval with the selected strategy
            var retrievalStopwatch = Stopwatch.StartNew();
            var documents = new List<RoutedDocument>();
            var usedFallback = false;
            var fallbacksTried = new List<RetrievalStrategy>();
            var executedStrategy = decision.PrimaryStrategy;
            string? errorMessage = null;

            try
            {
                documents = await ExecuteStrategyAsync(
                    decision.PrimaryStrategy, query, context, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogPrimaryStrategyFailed(_logger, ex, decision.PrimaryStrategy);

                // Try fallback strategies
                foreach (var fallback in decision.FallbackStrategies)
                {
                    try
                    {
                        fallbacksTried.Add(fallback);
                        documents = await ExecuteStrategyAsync(fallback, query, context, cancellationToken);
                        executedStrategy = fallback;
                        usedFallback = true;
                        break;
                    }
                    catch (Exception fallbackEx) when (fallbackEx is not OperationCanceledException)
                    {
                        LogFallbackStrategyFailed(_logger, fallbackEx, fallback);
                    }
                }

                if (documents.Count == 0)
                {
                    errorMessage = ex.Message;
                }
            }

            retrievalStopwatch.Stop();
            totalStopwatch.Stop();

            // Calculate quality score
            var qualityScore = CalculateQualityScore(documents);

            // Update strategy performance metrics
            UpdateStrategyPerformance(executedStrategy, retrievalStopwatch.Elapsed, qualityScore, documents.Count > 0);

            var result = new RoutingResult
            {
                RoutingId = Guid.NewGuid().ToString(),
                Documents = documents,
                Decision = decision,
                ExecutedStrategy = executedStrategy,
                UsedFallback = usedFallback,
                FallbacksTriedList = fallbacksTried,
                TotalTime = totalStopwatch.Elapsed,
                RoutingTime = routingStopwatch.Elapsed,
                RetrievalTime = retrievalStopwatch.Elapsed,
                QualityScore = qualityScore,
                IsSuccessful = documents.Count > 0,
                ErrorMessage = errorMessage,
                RoutingExplanation = decision.Explanation
            };

            LogRoutingCompleted(_logger, executedStrategy, documents.Count, qualityScore, totalStopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            totalStopwatch.Stop();
            LogRoutingFailed(_logger, ex, query);

            return new RoutingResult
            {
                IsSuccessful = false,
                ErrorMessage = ex.Message,
                TotalTime = totalStopwatch.Elapsed
            };
        }
    }

    /// <inheritdoc />
    public async Task<RoutingDecision> AnalyzeQueryAsync(
        string query,
        RoutingContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        // Analyze query characteristics
        var queryAnalysis = await AnalyzeQueryCharacteristicsAsync(query, cancellationToken);

        // Detect query features
        var features = DetectQueryFeatures(query, queryAnalysis);

        // Select primary strategy based on analysis
        var (primaryStrategy, confidence, explanation) =
            SelectStrategy(queryAnalysis, features, context);

        // Determine fallback strategies
        var fallbacks = DetermineFallbackStrategies(primaryStrategy, queryAnalysis, context);

        // Estimate performance metrics
        var performance = _strategyPerformance[primaryStrategy];

        return new RoutingDecision
        {
            PrimaryStrategy = primaryStrategy,
            FallbackStrategies = fallbacks,
            Confidence = confidence,
            QueryAnalysis = queryAnalysis,
            Explanation = explanation,
            EstimatedLatency = performance.AverageLatency,
            EstimatedQuality = performance.AverageQuality,
            DetectedFeatures = features
        };
    }

    /// <inheritdoc />
    public async Task<RetrievalPlan> GenerateRetrievalPlanAsync(
        string query,
        RoutingContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var queryAnalysis = await AnalyzeQueryCharacteristicsAsync(query, cancellationToken);
        var steps = new List<RetrievalStep>();
        var dependencies = new List<StepDependency>();
        var stepNumber = 0;

        // Check if query needs decomposition
        if (queryAnalysis.SubQueries.Count > 1)
        {
            // Create steps for each sub-query
            var subQuerySteps = new List<string>();
            foreach (var subQuery in queryAnalysis.SubQueries)
            {
                var stepId = Guid.NewGuid().ToString();
                subQuerySteps.Add(stepId);
                steps.Add(new RetrievalStep
                {
                    StepId = stepId,
                    StepNumber = ++stepNumber,
                    Strategy = RetrievalStrategy.HybridSearch,
                    Query = subQuery,
                    MaxResults = 3,
                    Purpose = $"Retrieve results for sub-query: {subQuery}",
                    CanParallelize = true
                });
            }

            // Add merge step
            var mergeStepId = Guid.NewGuid().ToString();
            steps.Add(new RetrievalStep
            {
                StepId = mergeStepId,
                StepNumber = ++stepNumber,
                Strategy = RetrievalStrategy.Ensemble,
                Query = query,
                MaxResults = context?.MaxResults ?? 10,
                Purpose = "Merge and rank results from sub-queries",
                CanParallelize = false
            });

            // Add dependencies
            foreach (var subQueryStepId in subQuerySteps)
            {
                dependencies.Add(new StepDependency
                {
                    DependentStepId = mergeStepId,
                    PrerequisiteStepId = subQueryStepId,
                    Type = DependencyType.DataFlow
                });
            }
        }
        else if (queryAnalysis.RequiresMultiHop)
        {
            // Create multi-hop retrieval plan
            var firstStepId = Guid.NewGuid().ToString();
            steps.Add(new RetrievalStep
            {
                StepId = firstStepId,
                StepNumber = ++stepNumber,
                Strategy = RetrievalStrategy.HybridSearch,
                Query = query,
                MaxResults = 5,
                Purpose = "Initial retrieval for seed documents",
                CanParallelize = false
            });

            var secondStepId = Guid.NewGuid().ToString();
            steps.Add(new RetrievalStep
            {
                StepId = secondStepId,
                StepNumber = ++stepNumber,
                Strategy = RetrievalStrategy.GraphTraversal,
                Query = query,
                MaxResults = 5,
                Purpose = "Expand retrieval through related documents",
                CanParallelize = false
            });

            dependencies.Add(new StepDependency
            {
                DependentStepId = secondStepId,
                PrerequisiteStepId = firstStepId,
                Type = DependencyType.Sequential
            });
        }
        else
        {
            // Simple single-step plan
            steps.Add(new RetrievalStep
            {
                StepId = Guid.NewGuid().ToString(),
                StepNumber = ++stepNumber,
                Strategy = SelectStrategy(queryAnalysis, new List<QueryFeature>(), context).strategy,
                Query = query,
                MaxResults = context?.MaxResults ?? 10,
                Purpose = "Direct retrieval",
                CanParallelize = false
            });
        }

        var estimatedDuration = steps.Aggregate(
            TimeSpan.Zero,
            (total, step) => total + EstimateBaseLatency(step.Strategy));

        return new RetrievalPlan
        {
            OriginalQuery = query,
            Steps = steps,
            Dependencies = dependencies,
            EstimatedDuration = estimatedDuration,
            PlanExplanation = GeneratePlanExplanation(queryAnalysis, steps)
        };
    }

    /// <inheritdoc />
    public async Task<MultiStepRetrievalResult> ExecuteRetrievalPlanAsync(
        string query,
        RetrievalPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        var stepResults = new List<StepResult>();
        var stepDocuments = new Dictionary<string, IReadOnlyList<RoutedDocument>>();

        try
        {
            // Sort steps by dependencies
            var orderedSteps = TopologicalSort(plan.Steps, plan.Dependencies);

            foreach (var step in orderedSteps)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var stepStopwatch = Stopwatch.StartNew();
                try
                {
                    // Check if prerequisites are completed
                    var prerequisites = plan.Dependencies
                        .Where(d => d.DependentStepId == step.StepId)
                        .Select(d => d.PrerequisiteStepId)
                        .ToList();

                    var allPrerequisitesComplete = prerequisites.All(p =>
                        stepResults.Any(r => r.StepId == p && r.IsSuccessful));

                    if (!allPrerequisitesComplete && prerequisites.Count != 0)
                    {
                        throw new InvalidOperationException(
                            $"Prerequisites for step {step.StepNumber} not completed");
                    }

                    // Execute the step
                    var documents = await ExecuteStrategyAsync(
                        step.Strategy, step.Query, null, cancellationToken);

                    // Limit results
                    documents = documents.Take(step.MaxResults).ToList();

                    stepStopwatch.Stop();
                    stepDocuments[step.StepId] = documents;

                    stepResults.Add(new StepResult
                    {
                        StepId = step.StepId,
                        StepNumber = step.StepNumber,
                        Documents = documents,
                        ExecutionTime = stepStopwatch.Elapsed,
                        IsSuccessful = true
                    });
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    stepStopwatch.Stop();
                    LogStepFailed(_logger, ex, step.StepNumber);

                    stepResults.Add(new StepResult
                    {
                        StepId = step.StepId,
                        StepNumber = step.StepNumber,
                        Documents = Array.Empty<RoutedDocument>(),
                        ExecutionTime = stepStopwatch.Elapsed,
                        IsSuccessful = false,
                        ErrorMessage = ex.Message
                    });
                }
            }

            stopwatch.Stop();

            // Merge all documents
            var mergedDocuments = MergeStepResults(stepDocuments.Values);
            var completedSteps = stepResults.Count(r => r.IsSuccessful);
            var failedSteps = stepResults.Count(r => !r.IsSuccessful);

            return new MultiStepRetrievalResult
            {
                Plan = plan,
                StepResults = stepResults,
                MergedDocuments = mergedDocuments,
                TotalTime = stopwatch.Elapsed,
                CompletedSteps = completedSteps,
                FailedSteps = failedSteps,
                IsSuccessful = completedSteps > 0
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            LogPlanExecutionFailed(_logger, ex);

            return new MultiStepRetrievalResult
            {
                Plan = plan,
                StepResults = stepResults,
                TotalTime = stopwatch.Elapsed,
                IsSuccessful = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <inheritdoc />
    public Task RecordRoutingFeedbackAsync(
        string routingId,
        RoutingFeedback feedback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(routingId);
        ArgumentNullException.ThrowIfNull(feedback);
        cancellationToken.ThrowIfCancellationRequested();

        _feedbackHistory[routingId] = feedback;

        LogRecordedFeedback(_logger, routingId, feedback.WasSatisfactory, feedback.QualityRating);

        return Task.CompletedTask;
    }

    #region Private Helper Methods

    private static async Task<RoutingQueryAnalysis> AnalyzeQueryCharacteristicsAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var type = ClassifyQueryType(query);
        var intent = ClassifyQueryIntent(query);
        var complexity = CalculateQueryComplexity(query);
        var entities = ExtractEntities(query);
        var concepts = ExtractKeyConcepts(query);
        var subQueries = DecomposeQuery(query);

        var requiresMultiHop = complexity > 0.7 ||
                               query.Contains(" and ") ||
                               query.Contains(" relationship ") ||
                               query.Contains(" compare ");

        var isTimeSensitive = query.Contains("latest") ||
                              query.Contains("recent") ||
                              query.Contains("current") ||
                              query.Contains("today") ||
                              Regex.IsMatch(query, @"\d{4}");

        return new RoutingQueryAnalysis
        {
            Type = type,
            Intent = intent,
            Complexity = complexity,
            RequiresMultiHop = requiresMultiHop,
            IsTimeSensitive = isTimeSensitive,
            DetectedEntities = entities,
            KeyConcepts = concepts,
            SubQueries = subQueries,
            EstimatedOptimalResultCount = EstimateOptimalResultCount(type, complexity)
        };
    }

    private static RoutingQueryType ClassifyQueryType(string query)
    {
        var lowerQuery = query.ToLowerInvariant();

        if (lowerQuery.StartsWith("what is", StringComparison.Ordinal) || lowerQuery.StartsWith("define", StringComparison.Ordinal))
            return RoutingQueryType.Definition;

        if (lowerQuery.StartsWith("how to", StringComparison.Ordinal) || lowerQuery.StartsWith("how do", StringComparison.Ordinal))
            return RoutingQueryType.Procedural;

        if (lowerQuery.StartsWith("why", StringComparison.Ordinal) || lowerQuery.Contains("reason"))
            return RoutingQueryType.Causal;

        if (lowerQuery.Contains("compare") || lowerQuery.Contains("difference") ||
            lowerQuery.Contains("versus") || lowerQuery.Contains(" vs "))
            return RoutingQueryType.Comparison;

        if (lowerQuery.Contains("recommend") || lowerQuery.Contains("best") ||
            lowerQuery.Contains("should"))
            return RoutingQueryType.Opinion;

        if (lowerQuery.Contains(" and ") && lowerQuery.Contains('?'))
            return RoutingQueryType.Complex;

        if (lowerQuery.Contains("list") || lowerQuery.Contains("all") ||
            lowerQuery.Contains("summary"))
            return RoutingQueryType.Aggregation;

        if (lowerQuery.StartsWith("where", StringComparison.Ordinal) || lowerQuery.StartsWith("find", StringComparison.Ordinal))
            return RoutingQueryType.Navigation;

        // Check for factual patterns
        if (lowerQuery.StartsWith("who", StringComparison.Ordinal) || lowerQuery.StartsWith("when", StringComparison.Ordinal) ||
            lowerQuery.StartsWith("what", StringComparison.Ordinal) || lowerQuery.Contains('?'))
            return RoutingQueryType.Factual;

        return RoutingQueryType.Unknown;
    }

    private static RoutingQueryIntent ClassifyQueryIntent(string query)
    {
        var lowerQuery = query.ToLowerInvariant();

        if (lowerQuery.Contains("help") || lowerQuery.Contains("problem") ||
            lowerQuery.Contains("error") || lowerQuery.Contains("fix"))
            return RoutingQueryIntent.Support;

        if (lowerQuery.Contains("research") || lowerQuery.Contains("study") ||
            lowerQuery.Contains("explore"))
            return RoutingQueryIntent.Research;

        if (lowerQuery.Contains("buy") || lowerQuery.Contains("price") ||
            lowerQuery.Contains("subscribe"))
            return RoutingQueryIntent.Transactional;

        if (lowerQuery.Contains("go to") || lowerQuery.Contains("navigate") ||
            lowerQuery.Contains("link"))
            return RoutingQueryIntent.Navigational;

        return RoutingQueryIntent.Informational;
    }

    private static double CalculateQueryComplexity(string query)
    {
        var factors = new List<double>();

        // Word count factor
        var wordCount = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        factors.Add(Math.Min(wordCount / 20.0, 1.0));

        // Question word count
        var questionWords = new[] { "what", "why", "how", "when", "where", "who", "which" };
        var questionWordCount = questionWords.Count(w =>
            query.Contains(w, StringComparison.OrdinalIgnoreCase));
        factors.Add(Math.Min(questionWordCount / 3.0, 1.0));

        // Conjunction count (suggests compound query)
        var conjunctions = new[] { " and ", " or ", " but ", " because " };
        var conjunctionCount = conjunctions.Count(c => query.Contains(c, StringComparison.OrdinalIgnoreCase));
        factors.Add(Math.Min(conjunctionCount / 2.0, 1.0));

        // Technical terms (rough heuristic)
        var technicalPattern = new Regex(@"\b[A-Z][a-z]+[A-Z]\w*\b|\b\w+\.\w+\b");
        var technicalCount = technicalPattern.Count(query);
        factors.Add(Math.Min(technicalCount / 3.0, 1.0));

        return factors.Average();
    }

    private static List<string> ExtractEntities(string query)
    {
        var entities = new List<string>();

        // Simple entity extraction based on capitalization and patterns
        var capitalizedWords = Regex.Matches(query, @"\b[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*\b")
            .Select(m => m.Value)
            .Where(w => w.Length > 2);
        entities.AddRange(capitalizedWords);

        // Extract quoted strings
        var quotedStrings = Regex.Matches(query, @"""([^""]+)""")
            .Select(m => m.Groups[1].Value);
        entities.AddRange(quotedStrings);

        return entities.Distinct().ToList();
    }

    private static List<string> ExtractKeyConcepts(string query)
    {
        // Simple concept extraction - remove stop words and get significant terms
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "is", "are", "was", "were", "be", "been", "being",
            "have", "has", "had", "do", "does", "did", "will", "would", "could",
            "should", "may", "might", "must", "shall", "can", "need", "dare",
            "to", "of", "in", "for", "on", "with", "at", "by", "from", "as",
            "into", "through", "during", "before", "after", "above", "below",
            "between", "under", "again", "further", "then", "once", "here",
            "there", "when", "where", "why", "how", "all", "each", "few",
            "more", "most", "other", "some", "such", "no", "nor", "not", "only",
            "same", "so", "than", "too", "very", "just", "and", "but", "if", "or",
            "because", "as", "until", "while", "although", "what", "which", "who"
        };

        var words = query.ToLowerInvariant()
            .Split(QuerySplitSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !stopWords.Contains(w))
            .Distinct()
            .ToList();

        return words;
    }

    private static IReadOnlyList<string> DecomposeQuery(string query)
    {
        // Check for explicit sub-questions
        if (query.Contains('?') && query.Count(c => c == '?') > 1)
        {
            return query.Split('?', StringSplitOptions.RemoveEmptyEntries)
                .Select(q => q.Trim() + "?")
                .Where(q => q.Length > 5)
                .ToList();
        }

        // Check for conjunctions suggesting compound query
        if (query.Contains(" and ", StringComparison.OrdinalIgnoreCase) && query.Length > 50)
        {
            var parts = query.Split(ConjunctionSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1 && parts.All(p => p.Length > 10))
            {
                return parts.Select(p => p.Trim()).ToList();
            }
        }

        // Return original query as single item
        return new[] { query };
    }

    private static int EstimateOptimalResultCount(RoutingQueryType type, double complexity)
    {
        var baseCount = type switch
        {
            RoutingQueryType.Factual => 3,
            RoutingQueryType.Definition => 2,
            RoutingQueryType.Procedural => 5,
            RoutingQueryType.Comparison => 4,
            RoutingQueryType.Causal => 4,
            RoutingQueryType.Opinion => 5,
            RoutingQueryType.Complex => 8,
            RoutingQueryType.Aggregation => 10,
            RoutingQueryType.Navigation => 1,
            _ => 5
        };

        // Adjust based on complexity
        var adjustedCount = (int)(baseCount * (1 + complexity * 0.5));
        return Math.Min(adjustedCount, 15);
    }

    private static List<QueryFeature> DetectQueryFeatures(string query, RoutingQueryAnalysis analysis)
    {
        var features = new List<QueryFeature>();

        // Complexity feature
        if (analysis.Complexity > 0.7)
        {
            features.Add(new QueryFeature
            {
                Name = "High Complexity",
                Value = $"Complexity score: {analysis.Complexity:F2}",
                Confidence = analysis.Complexity,
                Impact = RoutingImpact.StrongIndicator
            });
        }

        // Multi-hop requirement
        if (analysis.RequiresMultiHop)
        {
            features.Add(new QueryFeature
            {
                Name = "Multi-Hop Required",
                Value = "Query requires connecting multiple documents",
                Confidence = 0.8,
                Impact = RoutingImpact.Requires
            });
        }

        // Time sensitivity
        if (analysis.IsTimeSensitive)
        {
            features.Add(new QueryFeature
            {
                Name = "Time Sensitive",
                Value = "Query contains temporal indicators",
                Confidence = 0.85,
                Impact = RoutingImpact.Suggests
            });
        }

        // Entity presence
        if (analysis.DetectedEntities.Count > 0)
        {
            features.Add(new QueryFeature
            {
                Name = "Named Entities",
                Value = string.Join(", ", analysis.DetectedEntities.Take(3)),
                Confidence = 0.7,
                Impact = RoutingImpact.Suggests
            });
        }

        // Sub-query presence
        if (analysis.SubQueries.Count > 1)
        {
            features.Add(new QueryFeature
            {
                Name = "Compound Query",
                Value = $"{analysis.SubQueries.Count} sub-queries detected",
                Confidence = 0.75,
                Impact = RoutingImpact.StrongIndicator
            });
        }

        return features;
    }

    private (RetrievalStrategy strategy, double confidence, string explanation) SelectStrategy(
        RoutingQueryAnalysis analysis,
        IReadOnlyList<QueryFeature> features,
        RoutingContext? context)
    {
        // Check for required capabilities
        if (context?.RequiredCapabilities.HasFlag(RetrievalCapabilities.SelfCorrection) == true)
        {
            return (RetrievalStrategy.SelfRAG, 0.9,
                "Self-correction capability required - using Self-RAG");
        }

        if (context?.PreferredStrategy != null)
        {
            return (context.PreferredStrategy.Value, 0.85,
                $"Using preferred strategy: {context.PreferredStrategy.Value}");
        }

        // Decision based on query analysis
        if (analysis.SubQueries.Count > 1)
        {
            return (RetrievalStrategy.QueryDecomposition, 0.88,
                "Complex query with multiple parts - decomposing into sub-queries");
        }

        if (analysis.RequiresMultiHop)
        {
            return (RetrievalStrategy.MultiHopRetrieval, 0.85,
                "Query requires connecting information from multiple documents");
        }

        if (analysis.Complexity > 0.7)
        {
            if (_correctiveRAGService != null)
            {
                return (RetrievalStrategy.CorrectiveRAG, 0.82,
                    "High complexity query - using Corrective RAG for quality assurance");
            }
            if (_selfRAGService != null)
            {
                return (RetrievalStrategy.SelfRAG, 0.80,
                    "High complexity query - using Self-RAG for iterative refinement");
            }
        }

        // Type-based selection
        var strategy = analysis.Type switch
        {
            RoutingQueryType.Factual => RetrievalStrategy.HybridSearch,
            RoutingQueryType.Definition => RetrievalStrategy.SemanticSearch,
            RoutingQueryType.Procedural => _smallToBigRetriever != null
                ? RetrievalStrategy.SmallToBig
                : RetrievalStrategy.HybridSearch,
            RoutingQueryType.Comparison => RetrievalStrategy.HybridSearch,
            RoutingQueryType.Causal => RetrievalStrategy.HybridSearch,
            RoutingQueryType.Opinion => RetrievalStrategy.SemanticSearch,
            RoutingQueryType.Complex => RetrievalStrategy.HybridSearch,
            RoutingQueryType.Aggregation => RetrievalStrategy.HybridSearch,
            RoutingQueryType.Navigation => RetrievalStrategy.KeywordSearch,
            _ => RetrievalStrategy.HybridSearch
        };

        var explanation = $"Selected {strategy} based on query type ({analysis.Type}) " +
                          $"and complexity ({analysis.Complexity:F2})";

        return (strategy, 0.75, explanation);
    }

    private static List<RetrievalStrategy> DetermineFallbackStrategies(
        RetrievalStrategy primary,
        RoutingQueryAnalysis analysis,
        RoutingContext? context)
    {
        var fallbacks = new List<RetrievalStrategy>();

        // Always include hybrid search as a reliable fallback
        if (primary != RetrievalStrategy.HybridSearch)
        {
            fallbacks.Add(RetrievalStrategy.HybridSearch);
        }

        // Add semantic search for semantic-related strategies
        if (primary != RetrievalStrategy.SemanticSearch &&
            primary != RetrievalStrategy.KeywordSearch)
        {
            fallbacks.Add(RetrievalStrategy.SemanticSearch);
        }

        // Add keyword search as last resort
        if (primary != RetrievalStrategy.KeywordSearch)
        {
            fallbacks.Add(RetrievalStrategy.KeywordSearch);
        }

        return fallbacks.Take(3).ToList();
    }

    private async Task<List<RoutedDocument>> ExecuteStrategyAsync(
        RetrievalStrategy strategy,
        string query,
        RoutingContext? context,
        CancellationToken cancellationToken)
    {
        var documents = new List<RoutedDocument>();
        var maxResults = context?.MaxResults ?? 10;

        switch (strategy)
        {
            case RetrievalStrategy.HybridSearch:
            case RetrievalStrategy.SemanticSearch:
            case RetrievalStrategy.KeywordSearch:
                {
                    var vectorWeight = strategy == RetrievalStrategy.KeywordSearch ? 0.0f
                        : strategy == RetrievalStrategy.SemanticSearch ? 1.0f : 0.5f;
                    var options = new HybridSearchOptions
                    {
                        MaxResults = maxResults,
                        VectorWeight = vectorWeight,
                        SparseWeight = 1.0f - vectorWeight
                    };

                    var results = await _hybridSearchService.SearchAsync(query, options, cancellationToken);
                    documents = results.Select((r, i) => new RoutedDocument
                    {
                        Chunk = r.Chunk,
                        RelevanceScore = r.FusedScore,
                        RetrievedBy = strategy,
                        RetrievalStep = 1,
                        Confidence = Math.Max(0, 1 - (i * 0.1)),
                        RetrievalReason = $"Retrieved via {strategy}"
                    }).ToList();
                    break;
                }

            case RetrievalStrategy.SelfRAG:
                if (_selfRAGService != null)
                {
                    var selfRagResult = await _selfRAGService.SearchAsync(query, null, cancellationToken);
                    var selfRagDocuments = selfRagResult.FinalResults.ToList();
                    documents = selfRagDocuments
                        .SelectMany((doc, docIdx) => doc.Chunks.Select((chunk, chunkIdx) => new { doc, chunk, docIdx, chunkIdx }))
                        .Select((item, i) => new RoutedDocument
                        {
                            Chunk = item.chunk,
                            RelevanceScore = selfRagResult.FinalQualityScore,
                            RetrievedBy = strategy,
                            RetrievalStep = 1,
                            Confidence = selfRagResult.FinalQualityScore,
                            RetrievalReason = "Retrieved via Self-RAG with quality assessment"
                        })
                        .Take(maxResults)
                        .ToList();
                }
                else
                {
                    throw new NotSupportedException("Self-RAG service not available");
                }
                break;

            case RetrievalStrategy.CorrectiveRAG:
                if (_correctiveRAGService != null)
                {
                    var cragResult = await _correctiveRAGService.RetrieveWithCorrectionAsync(
                        query, null, cancellationToken);
                    documents = cragResult.Documents.Select((d, i) => new RoutedDocument
                    {
                        Chunk = d.Chunk,
                        RelevanceScore = d.RelevanceScore,
                        RetrievedBy = strategy,
                        RetrievalStep = 1,
                        Confidence = d.RelevanceScore,
                        RetrievalReason = d.InclusionReason ?? "Retrieved via Corrective RAG"
                    }).Take(maxResults).ToList();
                }
                else
                {
                    throw new NotSupportedException("Corrective RAG service not available");
                }
                break;

            case RetrievalStrategy.SmallToBig:
                if (_smallToBigRetriever != null)
                {
                    var s2bOptions = new Domain.Models.SmallToBigOptions { MaxResults = maxResults };
                    var s2bResults = await _smallToBigRetriever.SearchAsync(
                        query, s2bOptions, cancellationToken);
                    documents = s2bResults.Select((r, i) => new RoutedDocument
                    {
                        Chunk = r.PrimaryChunk,
                        RelevanceScore = r.RelevanceScore,
                        RetrievedBy = strategy,
                        RetrievalStep = 1,
                        Confidence = r.RelevanceScore,
                        RetrievalReason = "Retrieved via Small-to-Big contextual expansion"
                    }).ToList();
                }
                else
                {
                    throw new NotSupportedException("Small-to-Big retriever not available");
                }
                break;

            case RetrievalStrategy.IterativeRetrieval:
                if (_iterativeRetrievalService != null)
                {
                    var iterResult = await _iterativeRetrievalService.RetrieveWithReasoningAsync(
                        query, null, cancellationToken);
                    documents = iterResult.Documents.Select((r, i) => new RoutedDocument
                    {
                        Chunk = new DocumentChunk
                        {
                            Id = r.ChunkId,
                            DocumentId = r.DocumentId,
                            Content = r.Content,
                            ChunkIndex = r.ChunkIndex
                        },
                        RelevanceScore = r.Score,
                        RetrievedBy = strategy,
                        RetrievalStep = iterResult.Iterations.Count,
                        Confidence = r.Score,
                        RetrievalReason = "Retrieved via iterative refinement"
                    }).Take(maxResults).ToList();
                }
                else
                {
                    throw new NotSupportedException("Iterative retrieval service not available");
                }
                break;

            default:
                // Fallback to hybrid search for unsupported strategies
                LogStrategyNotSupported(_logger, strategy);
                return await ExecuteStrategyAsync(RetrievalStrategy.HybridSearch, query, context, cancellationToken);
        }

        return documents;
    }

    private static double CalculateQualityScore(List<RoutedDocument> documents)
    {
        if (documents.Count == 0) return 0;

        var avgRelevance = documents.Average(d => d.RelevanceScore);
        var avgConfidence = documents.Average(d => d.Confidence);
        var countFactor = Math.Min(documents.Count / 5.0, 1.0);

        return (avgRelevance * 0.5 + avgConfidence * 0.3 + countFactor * 0.2);
    }

    private void UpdateStrategyPerformance(
        RetrievalStrategy strategy,
        TimeSpan latency,
        double quality,
        bool success)
    {
        if (_strategyPerformance.TryGetValue(strategy, out var performance))
        {
            // Exponential moving average for metrics
            const double alpha = 0.1;
            performance.AverageLatency = TimeSpan.FromMilliseconds(
                performance.AverageLatency.TotalMilliseconds * (1 - alpha) +
                latency.TotalMilliseconds * alpha);
            performance.AverageQuality = performance.AverageQuality * (1 - alpha) + quality * alpha;
            performance.SuccessRate = performance.SuccessRate * (1 - alpha) + (success ? 1 : 0) * alpha;
            performance.UsageCount++;
        }
    }

    private static List<RetrievalStep> TopologicalSort(
        IReadOnlyList<RetrievalStep> steps,
        IReadOnlyList<StepDependency> dependencies)
    {
        var result = new List<RetrievalStep>();
        var visited = new HashSet<string>();
        var stepMap = steps.ToDictionary(s => s.StepId);
        var dependencyMap = dependencies
            .GroupBy(d => d.DependentStepId)
            .ToDictionary(g => g.Key, g => g.Select(d => d.PrerequisiteStepId).ToList());

        void Visit(string stepId)
        {
            if (visited.Contains(stepId)) return;
            visited.Add(stepId);

            if (dependencyMap.TryGetValue(stepId, out var prereqs))
            {
                foreach (var prereq in prereqs)
                {
                    Visit(prereq);
                }
            }

            if (stepMap.TryGetValue(stepId, out var step))
            {
                result.Add(step);
            }
        }

        foreach (var step in steps)
        {
            Visit(step.StepId);
        }

        return result;
    }

    private static List<RoutedDocument> MergeStepResults(
        IEnumerable<IReadOnlyList<RoutedDocument>> stepResults)
    {
        var allDocs = stepResults.SelectMany(r => r).ToList();

        // Remove duplicates based on chunk ID, keeping highest relevance score
        var merged = allDocs
            .GroupBy(d => d.Chunk.Id)
            .Select(g => g.OrderByDescending(d => d.RelevanceScore).First())
            .OrderByDescending(d => d.RelevanceScore)
            .ToList();

        return merged;
    }

    private static string GeneratePlanExplanation(RoutingQueryAnalysis analysis, List<RetrievalStep> steps)
    {
        var explanation = new System.Text.StringBuilder();
        explanation.AppendLine(CultureInfo.InvariantCulture, $"Query Type: {analysis.Type}");
        explanation.AppendLine(CultureInfo.InvariantCulture, $"Complexity: {analysis.Complexity:F2}");
        explanation.AppendLine(CultureInfo.InvariantCulture, $"Steps: {steps.Count}");

        foreach (var step in steps)
        {
            explanation.AppendLine(CultureInfo.InvariantCulture, $"  Step {step.StepNumber}: {step.Strategy} - {step.Purpose}");
        }

        return explanation.ToString();
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Routing query: {Query}")]
    private static partial void LogRoutingQuery(ILogger logger, string query);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Primary strategy {Strategy} failed, trying fallbacks")]
    private static partial void LogPrimaryStrategyFailed(ILogger logger, Exception ex, RetrievalStrategy strategy);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Fallback strategy {Strategy} also failed")]
    private static partial void LogFallbackStrategyFailed(ILogger logger, Exception ex, RetrievalStrategy strategy);

    [LoggerMessage(Level = LogLevel.Information, Message = "Routing completed: Strategy={Strategy}, Documents={Count}, Quality={Quality}, Time={Time}ms")]
    private static partial void LogRoutingCompleted(ILogger logger, RetrievalStrategy strategy, int count, double quality, long time);

    [LoggerMessage(Level = LogLevel.Error, Message = "Routing failed for query: {Query}")]
    private static partial void LogRoutingFailed(ILogger logger, Exception ex, string query);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Recorded feedback for routing {RoutingId}: Satisfactory={Satisfactory}, Rating={Rating}")]
    private static partial void LogRecordedFeedback(ILogger logger, string routingId, bool satisfactory, double rating);

    [LoggerMessage(Level = LogLevel.Error, Message = "Step {StepNumber} failed")]
    private static partial void LogStepFailed(ILogger logger, Exception ex, int stepNumber);

    [LoggerMessage(Level = LogLevel.Error, Message = "Plan execution failed")]
    private static partial void LogPlanExecutionFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Strategy {Strategy} not fully supported, falling back to hybrid search")]
    private static partial void LogStrategyNotSupported(ILogger logger, RetrievalStrategy strategy);

    #endregion
}

/// <summary>
/// Performance metrics for a retrieval strategy.
/// </summary>
internal sealed class StrategyPerformance
{
    public RetrievalStrategy Strategy { get; init; }
    public TimeSpan AverageLatency { get; set; }
    public double AverageQuality { get; set; }
    public double SuccessRate { get; set; }
    public int UsageCount { get; set; }
}

/// <summary>
/// Options for the Agentic Retrieval Router.
/// </summary>
public class AgenticRetrievalRouterOptions
{
    /// <summary>
    /// Default maximum results to return.
    /// </summary>
    public int DefaultMaxResults { get; set; } = 10;

    /// <summary>
    /// Enable adaptive strategy selection based on feedback.
    /// </summary>
    public bool EnableAdaptiveRouting { get; set; } = true;

    /// <summary>
    /// Minimum confidence threshold for routing decisions.
    /// </summary>
    public double MinRoutingConfidence { get; set; } = 0.5;

    /// <summary>
    /// Maximum number of fallback strategies to try.
    /// </summary>
    public int MaxFallbackAttempts { get; set; } = 3;

    /// <summary>
    /// Timeout for individual strategy execution.
    /// </summary>
    public TimeSpan StrategyTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Enable detailed routing explanations.
    /// </summary>
    public bool EnableDetailedExplanations { get; set; }

    /// <summary>
    /// Enable performance tracking.
    /// </summary>
    public bool EnablePerformanceTracking { get; set; } = true;
}
