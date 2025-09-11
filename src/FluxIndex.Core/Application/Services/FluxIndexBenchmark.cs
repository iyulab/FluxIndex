using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// Comprehensive benchmarking tool for FluxIndex components and end-to-end performance
/// </summary>
public class FluxIndexBenchmark
{
    private readonly ILogger<FluxIndexBenchmark> _logger;
    private readonly BenchmarkOptions _options;

    public FluxIndexBenchmark(
        BenchmarkOptions? options = null,
        ILogger<FluxIndexBenchmark>? logger = null)
    {
        _options = options ?? new BenchmarkOptions();
        _logger = logger ?? new NullLogger<FluxIndexBenchmark>();
    }

    /// <summary>
    /// Runs comprehensive benchmark suite for FluxIndex components
    /// </summary>
    public async Task<BenchmarkResults> RunFullBenchmarkAsync(
        BenchmarkServices services,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting FluxIndex comprehensive benchmark");
        var overallStopwatch = Stopwatch.StartNew();
        var results = new BenchmarkResults();

        try
        {
            // 1. Vector Index Optimization Benchmark
            if (services.VectorIndexOptimizer != null)
            {
                _logger.LogInformation("Benchmarking HNSW parameter optimization...");
                results.HNSWOptimizationResults = await BenchmarkHNSWOptimizationAsync(
                    services.VectorIndexOptimizer, cancellationToken);
            }

            // 2. Embedding Service Benchmark
            if (services.EmbeddingService != null)
            {
                _logger.LogInformation("Benchmarking embedding generation...");
                results.EmbeddingResults = await BenchmarkEmbeddingServiceAsync(
                    services.EmbeddingService, cancellationToken);
            }

            // 3. Reranking Benchmark
            if (services.Reranker != null)
            {
                _logger.LogInformation("Benchmarking reranking performance...");
                results.RerankingResults = await BenchmarkRerankingAsync(
                    services.Reranker, cancellationToken);
            }

            // 4. Two-Stage Retrieval Benchmark
            if (services.TwoStageRetriever != null)
            {
                _logger.LogInformation("Benchmarking two-stage retrieval...");
                results.TwoStageResults = await BenchmarkTwoStageRetrievalAsync(
                    services.TwoStageRetriever, cancellationToken);
            }

            // 5. Semantic Cache Benchmark
            if (services.SemanticCache != null)
            {
                _logger.LogInformation("Benchmarking semantic cache...");
                results.CacheResults = await BenchmarkSemanticCacheAsync(
                    services.SemanticCache, cancellationToken);
            }

            // 6. Text Completion Benchmark
            if (services.TextCompletionService != null)
            {
                _logger.LogInformation("Benchmarking text completion...");
                results.TextCompletionResults = await BenchmarkTextCompletionAsync(
                    services.TextCompletionService, cancellationToken);
            }

            // 7. End-to-End Integration Benchmark
            _logger.LogInformation("Running end-to-end integration benchmark...");
            results.EndToEndResults = await BenchmarkEndToEndAsync(services, cancellationToken);

            overallStopwatch.Stop();
            results.TotalBenchmarkTime = overallStopwatch.Elapsed;
            results.CompletedAt = DateTime.UtcNow;

            _logger.LogInformation("FluxIndex benchmark completed in {TotalTime}", results.TotalBenchmarkTime);
            LogBenchmarkSummary(results);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during FluxIndex benchmark");
            results.Error = ex.Message;
            results.CompletedAt = DateTime.UtcNow;
            return results;
        }
    }

    public async Task<HNSWOptimizationBenchmark> BenchmarkHNSWOptimizationAsync(
        IVectorIndexOptimizer optimizer,
        CancellationToken cancellationToken = default)
    {
        var result = new HNSWOptimizationBenchmark();
        var testCases = GenerateHNSWTestCases();

        foreach (var testCase in testCases)
        {
            var sw = Stopwatch.StartNew();
            
            try
            {
                var parameters = await optimizer.OptimizeParametersAsync(
                    testCase.DatasetSize, testCase.Dimensions, testCase.Target, cancellationToken);

                sw.Stop();

                var testResult = new HNSWTestResult
                {
                    TestCase = testCase,
                    OptimizedParameters = parameters,
                    OptimizationTime = sw.Elapsed,
                    MemoryEstimate = optimizer.EstimateMemoryUsage(parameters, testCase.DatasetSize, testCase.Dimensions),
                    ConstructionTimeEstimate = optimizer.EstimateConstructionTime(parameters, testCase.DatasetSize),
                    QueryLatencyEstimate = optimizer.EstimateQueryLatency(parameters, testCase.Dimensions),
                    ValidationResult = optimizer.ValidateParameters(parameters)
                };

                result.TestResults.Add(testResult);
                _logger.LogDebug("HNSW optimization: {Dataset}x{Dimensions}D, target={Target}, time={Time}ms",
                    testCase.DatasetSize, testCase.Dimensions, testCase.Target, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HNSW optimization failed for test case: {TestCase}", testCase);
                result.Errors.Add($"Test case {testCase}: {ex.Message}");
            }
        }

        result.AverageOptimizationTime = result.TestResults.Any() 
            ? result.TestResults.Average(r => r.OptimizationTime.TotalMilliseconds) : 0;

        return result;
    }

    public async Task<EmbeddingBenchmark> BenchmarkEmbeddingServiceAsync(
        IEmbeddingService embeddingService,
        CancellationToken cancellationToken = default)
    {
        var result = new EmbeddingBenchmark();
        var testTexts = GenerateEmbeddingTestTexts();

        // Single embedding benchmark
        var singleResults = new List<double>();
        foreach (var text in testTexts.Take(_options.EmbeddingSampleSize))
        {
            var sw = Stopwatch.StartNew();
            
            try
            {
                var embedding = await embeddingService.GenerateEmbeddingAsync(text, cancellationToken);
                sw.Stop();
                
                singleResults.Add(sw.ElapsedMilliseconds);
                result.TotalEmbeddings++;
                
                if (result.EmbeddingDimensions == 0)
                    result.EmbeddingDimensions = embedding.Dimensions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Single embedding generation failed");
                result.Errors.Add($"Single embedding: {ex.Message}");
            }
        }

        result.SingleEmbeddingLatencyMs = singleResults.Any() 
            ? new LatencyMetrics(singleResults) : new LatencyMetrics();

        // Batch embedding benchmark
        var batchSizes = new[] { 5, 10, 25, 50 };
        foreach (var batchSize in batchSizes)
        {
            var batch = testTexts.Take(batchSize).ToList();
            var sw = Stopwatch.StartNew();

            try
            {
                var embeddings = await embeddingService.GenerateBatchEmbeddingsAsync(batch, cancellationToken);
                sw.Stop();

                var batchResult = new BatchEmbeddingResult
                {
                    BatchSize = batchSize,
                    TotalLatencyMs = sw.ElapsedMilliseconds,
                    LatencyPerItemMs = (double)sw.ElapsedMilliseconds / batchSize,
                    ThroughputItemsPerSecond = batchSize / (sw.ElapsedMilliseconds / 1000.0),
                    Success = embeddings.Count() == batchSize
                };

                result.BatchResults.Add(batchResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Batch embedding generation failed for batch size {BatchSize}", batchSize);
                result.Errors.Add($"Batch size {batchSize}: {ex.Message}");
            }
        }

        return result;
    }

    public async Task<RerankingBenchmark> BenchmarkRerankingAsync(
        IReranker reranker,
        CancellationToken cancellationToken = default)
    {
        var result = new RerankingBenchmark();
        var candidateSizes = new[] { 10, 25, 50, 100 };

        foreach (var size in candidateSizes)
        {
            var candidates = GenerateRetrievalCandidates(size);
            var query = "machine learning algorithms neural networks";

            var sw = Stopwatch.StartNew();

            try
            {
                var rerankedResults = await reranker.RerankAsync(query, candidates, new RerankOptions
                {
                    TopN = Math.Min(size, 10),
                    IncludeExplanation = false
                }, cancellationToken);

                sw.Stop();

                var rerankResult = new RerankTestResult
                {
                    CandidateCount = size,
                    ResultCount = rerankedResults.Count(),
                    LatencyMs = sw.ElapsedMilliseconds,
                    ThroughputCandidatesPerSecond = size / (sw.ElapsedMilliseconds / 1000.0),
                    AverageScoreChange = rerankedResults.Any() 
                        ? rerankedResults.Average(r => r.ScoreChange) : 0,
                    Success = true
                };

                result.TestResults.Add(rerankResult);
                _logger.LogDebug("Reranking: {Candidates} candidates in {Time}ms", size, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reranking failed for {Candidates} candidates", size);
                result.Errors.Add($"Candidates {size}: {ex.Message}");
            }
        }

        result.ModelInfo = reranker.GetModelInfo();
        return result;
    }

    public async Task<TwoStageBenchmark> BenchmarkTwoStageRetrievalAsync(
        TwoStageRetriever retriever,
        CancellationToken cancellationToken = default)
    {
        var result = new TwoStageBenchmark();
        var testQueries = GenerateSearchQueries();

        foreach (var query in testQueries.Take(_options.SearchQuerySampleSize))
        {
            var options = new TwoStageSearchOptions
            {
                RecallTopK = 50,
                FinalTopK = 10,
                IncludeExplanation = false
            };

            try
            {
                var searchResult = await retriever.SearchAsync(query, options, cancellationToken);

                if (searchResult.IsSuccessful)
                {
                    var testResult = new TwoStageTestResult
                    {
                        Query = query,
                        Stage1LatencyMs = searchResult.Stage1LatencyMs,
                        Stage2LatencyMs = searchResult.Stage2LatencyMs,
                        TotalLatencyMs = searchResult.TotalLatencyMs,
                        RecallCount = searchResult.RecallCount,
                        FinalCount = searchResult.FinalCount,
                        RecallToFinalRatio = searchResult.RecallToRerankRatio,
                        AverageScoreImprovement = searchResult.AverageScoreImprovement,
                        Success = true
                    };

                    result.TestResults.Add(testResult);
                }
                else
                {
                    result.Errors.Add($"Query '{query}': {searchResult.Error}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Two-stage retrieval failed for query: {Query}", query);
                result.Errors.Add($"Query '{query}': {ex.Message}");
            }
        }

        if (result.TestResults.Any())
        {
            result.AverageStage1LatencyMs = result.TestResults.Average(r => r.Stage1LatencyMs);
            result.AverageStage2LatencyMs = result.TestResults.Average(r => r.Stage2LatencyMs);
            result.AverageTotalLatencyMs = result.TestResults.Average(r => r.TotalLatencyMs);
            result.AverageRecallCount = result.TestResults.Average(r => r.RecallCount);
            result.AverageFinalCount = result.TestResults.Average(r => r.FinalCount);
        }

        return result;
    }

    public async Task<CacheBenchmark> BenchmarkSemanticCacheAsync(
        ISemanticCache cache,
        CancellationToken cancellationToken = default)
    {
        var result = new CacheBenchmark();
        var testQueries = GenerateSearchQueries();
        var mockResults = GenerateMockSearchResults();

        // Cache population phase
        var populationSw = Stopwatch.StartNew();
        foreach (var query in testQueries.Take(_options.CachePopulationSize))
        {
            try
            {
                await cache.SetAsync(query, mockResults, cancellationToken: cancellationToken);
                result.CachedQueries++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Cache set for '{query}': {ex.Message}");
            }
        }
        populationSw.Stop();
        result.PopulationTimeMs = populationSw.ElapsedMilliseconds;

        // Cache retrieval benchmark
        var retrievalResults = new List<double>();
        var hitCount = 0;

        foreach (var query in testQueries.Take(_options.CacheRetrievalSampleSize))
        {
            var sw = Stopwatch.StartNew();
            
            try
            {
                var cacheResult = await cache.GetAsync(query, 0.85f, cancellationToken: cancellationToken);
                sw.Stop();
                
                retrievalResults.Add(sw.ElapsedMilliseconds);
                if (cacheResult != null) hitCount++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Cache get for '{query}': {ex.Message}");
            }
        }

        result.RetrievalLatencyMs = retrievalResults.Any() 
            ? new LatencyMetrics(retrievalResults) : new LatencyMetrics();
        result.HitRatio = retrievalResults.Count > 0 ? (double)hitCount / retrievalResults.Count : 0;

        // Get cache statistics
        try
        {
            result.Statistics = await cache.GetStatisticsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Cache statistics: {ex.Message}");
        }

        return result;
    }

    public async Task<TextCompletionBenchmark> BenchmarkTextCompletionAsync(
        ITextCompletionService textService,
        CancellationToken cancellationToken = default)
    {
        var result = new TextCompletionBenchmark();
        var testPrompts = GenerateTextCompletionPrompts();

        foreach (var prompt in testPrompts.Take(_options.TextCompletionSampleSize))
        {
            var sw = Stopwatch.StartNew();
            
            try
            {
                var completion = await textService.GenerateCompletionAsync(
                    prompt, maxTokens: 100, temperature: 0.7f, cancellationToken: cancellationToken);
                
                sw.Stop();

                var testResult = new TextCompletionTestResult
                {
                    Prompt = prompt,
                    Completion = completion,
                    LatencyMs = sw.ElapsedMilliseconds,
                    CompletionLength = completion.Length,
                    Success = !string.IsNullOrEmpty(completion)
                };

                result.TestResults.Add(testResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Text completion failed for prompt: {Prompt}", prompt.Substring(0, Math.Min(50, prompt.Length)));
                result.Errors.Add($"Prompt: {ex.Message}");
            }
        }

        if (result.TestResults.Any())
        {
            result.AverageLatencyMs = result.TestResults.Average(r => r.LatencyMs);
            result.AverageCompletionLength = result.TestResults.Average(r => r.CompletionLength);
            result.SuccessRate = (double)result.TestResults.Count(r => r.Success) / result.TestResults.Count;
        }

        return result;
    }

    private async Task<EndToEndBenchmark> BenchmarkEndToEndAsync(
        BenchmarkServices services,
        CancellationToken cancellationToken = default)
    {
        var result = new EndToEndBenchmark();
        
        if (services.TwoStageRetriever == null)
        {
            result.Errors.Add("Two-stage retriever not available for end-to-end benchmark");
            return result;
        }

        var testQueries = GenerateSearchQueries();
        
        foreach (var query in testQueries.Take(_options.EndToEndSampleSize))
        {
            var overallSw = Stopwatch.StartNew();
            var testResult = new EndToEndTestResult { Query = query };

            try
            {
                // Full search pipeline
                var searchResult = await services.TwoStageRetriever.SearchAsync(
                    query, new TwoStageSearchOptions
                    {
                        RecallTopK = 50,
                        FinalTopK = 10,
                        IncludeExplanation = _options.IncludeExplanations,
                        EnrichWithDocumentInfo = true
                    }, cancellationToken);

                overallSw.Stop();

                if (searchResult.IsSuccessful)
                {
                    testResult.Success = true;
                    testResult.TotalLatencyMs = overallSw.ElapsedMilliseconds;
                    testResult.ResultCount = searchResult.FinalCount;
                    testResult.QualityScore = CalculateResultQuality(searchResult.FinalResults);
                    
                    // Component breakdown
                    testResult.ComponentLatencies = new Dictionary<string, double>
                    {
                        ["stage1_recall"] = searchResult.Stage1LatencyMs,
                        ["stage2_reranking"] = searchResult.Stage2LatencyMs,
                        ["total_pipeline"] = searchResult.TotalLatencyMs
                    };
                }
                else
                {
                    testResult.Error = searchResult.Error;
                }
            }
            catch (Exception ex)
            {
                testResult.Error = ex.Message;
                _logger.LogError(ex, "End-to-end test failed for query: {Query}", query);
            }

            result.TestResults.Add(testResult);
        }

        if (result.TestResults.Any(r => r.Success))
        {
            var successfulResults = result.TestResults.Where(r => r.Success);
            result.AverageLatencyMs = successfulResults.Average(r => r.TotalLatencyMs);
            result.AverageResultCount = successfulResults.Average(r => r.ResultCount);
            result.AverageQualityScore = successfulResults.Average(r => r.QualityScore);
            result.SuccessRate = (double)successfulResults.Count() / result.TestResults.Count;
        }

        return result;
    }

    // Helper methods for generating test data
    private List<HNSWTestCase> GenerateHNSWTestCases()
    {
        return new List<HNSWTestCase>
        {
            new() { DatasetSize = 1000, Dimensions = 384, Target = QualityTarget.Speed },
            new() { DatasetSize = 1000, Dimensions = 384, Target = QualityTarget.Balanced },
            new() { DatasetSize = 1000, Dimensions = 384, Target = QualityTarget.Quality },
            new() { DatasetSize = 10000, Dimensions = 768, Target = QualityTarget.Speed },
            new() { DatasetSize = 10000, Dimensions = 768, Target = QualityTarget.Balanced },
            new() { DatasetSize = 10000, Dimensions = 768, Target = QualityTarget.Quality },
            new() { DatasetSize = 100000, Dimensions = 1536, Target = QualityTarget.Balanced }
        };
    }

    private List<string> GenerateEmbeddingTestTexts()
    {
        return new List<string>
        {
            "Machine learning algorithms for natural language processing",
            "Deep learning neural networks and computer vision",
            "Artificial intelligence systems and applications",
            "Data science and statistical modeling techniques",
            "Reinforcement learning and decision making",
            "Natural language understanding and generation",
            "Computer vision and image recognition",
            "Recommender systems and collaborative filtering",
            "Time series analysis and forecasting",
            "Distributed systems and scalability"
        };
    }

    private List<RetrievalCandidate> GenerateRetrievalCandidates(int count)
    {
        var sampleContents = GenerateEmbeddingTestTexts();
        var random = new Random(42);
        
        return Enumerable.Range(0, count)
            .Select(i => new RetrievalCandidate
            {
                Id = $"candidate_{i}",
                Content = sampleContents[i % sampleContents.Count],
                InitialScore = 0.5f + (float)(random.NextDouble() * 0.4),
                InitialRank = i + 1
            })
            .ToList();
    }

    private List<string> GenerateSearchQueries()
    {
        return new List<string>
        {
            "machine learning algorithms",
            "deep learning neural networks",
            "natural language processing",
            "computer vision techniques",
            "recommendation systems",
            "artificial intelligence applications",
            "data mining and analytics",
            "distributed computing systems"
        };
    }

    private List<object> GenerateMockSearchResults()
    {
        return Enumerable.Range(1, 10)
            .Select(i => new { Id = i, Score = 0.8f - i * 0.05f, Content = $"Mock result {i}" })
            .Cast<object>()
            .ToList();
    }

    private List<string> GenerateTextCompletionPrompts()
    {
        return new List<string>
        {
            "Explain how machine learning works in simple terms:",
            "What are the benefits of using neural networks for:",
            "Describe the key principles of artificial intelligence:",
            "How can deep learning be applied to solve:",
            "What are the main challenges in natural language processing:"
        };
    }

    private double CalculateResultQuality(IEnumerable<TwoStageSearchResult> results)
    {
        if (!results.Any()) return 0.0;
        
        // Simple quality metric based on score improvements and diversity
        var avgScoreImprovement = results.Average(r => r.ScoreImprovement);
        var avgFinalScore = results.Average(r => r.FinalScore);
        
        return Math.Max(0.0, Math.Min(1.0, avgFinalScore + avgScoreImprovement * 0.1));
    }

    private void LogBenchmarkSummary(BenchmarkResults results)
    {
        _logger.LogInformation("=== FluxIndex Benchmark Summary ===");
        _logger.LogInformation("Total benchmark time: {Time}", results.TotalBenchmarkTime);
        
        if (results.HNSWOptimizationResults != null)
        {
            _logger.LogInformation("HNSW Optimization: {Tests} tests, avg {Avg:F1}ms", 
                results.HNSWOptimizationResults.TestResults.Count,
                results.HNSWOptimizationResults.AverageOptimizationTime);
        }

        if (results.EmbeddingResults != null)
        {
            _logger.LogInformation("Embeddings: {Count} generated, avg {Avg:F1}ms", 
                results.EmbeddingResults.TotalEmbeddings,
                results.EmbeddingResults.SingleEmbeddingLatencyMs.Average);
        }

        if (results.RerankingResults != null)
        {
            var avgLatency = results.RerankingResults.TestResults.Any() 
                ? results.RerankingResults.TestResults.Average(r => r.LatencyMs) : 0;
            _logger.LogInformation("Reranking: {Tests} tests, avg {Avg:F1}ms", 
                results.RerankingResults.TestResults.Count, avgLatency);
        }

        if (results.EndToEndResults != null)
        {
            _logger.LogInformation("End-to-End: {Success:P1} success rate, avg {Avg:F1}ms, quality {Quality:F2}", 
                results.EndToEndResults.SuccessRate,
                results.EndToEndResults.AverageLatencyMs,
                results.EndToEndResults.AverageQualityScore);
        }

        _logger.LogInformation("=====================================");
    }
}

// Benchmark data structures and results are defined in separate files for clarity
// This includes BenchmarkOptions, BenchmarkResults, and all component-specific result classes