using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Extensions.FileFlux.Adapters;
using FluxIndex.Extensions.FileFlux.Enhancers;
using FluxIndex.Extensions.FileFlux.Indexing;
using FluxIndex.Extensions.FileFlux.Interfaces;
using FluxIndex.Extensions.FileFlux.Retrieval;
using FluxIndex.Extensions.FileFlux.Strategies;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace FluxIndex.Extensions.FileFlux.Pipeline;

/// <summary>
/// End-to-end pipeline for FileFlux integration
/// </summary>
public class FileFluxIntegrationPipeline : IFileFluxIntegrationPipeline
{
    private readonly IFileFluxAdapter _adapter;
    private readonly ISmartIndexer _indexer;
    private readonly IChunkAwareRetriever _retriever;
    private readonly MetadataEnhancer _enhancer;
    private readonly ChunkingStrategyInferrer _strategyInferrer;
    private readonly ILogger<FileFluxIntegrationPipeline> _logger;

    // Pipeline statistics
    private readonly PipelineStatistics _statistics = new();

    public FileFluxIntegrationPipeline(
        IFileFluxAdapter adapter,
        ISmartIndexer indexer,
        IChunkAwareRetriever retriever,
        MetadataEnhancer enhancer,
        ChunkingStrategyInferrer strategyInferrer,
        ILogger<FileFluxIntegrationPipeline> logger)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
        _retriever = retriever ?? throw new ArgumentNullException(nameof(retriever));
        _enhancer = enhancer ?? throw new ArgumentNullException(nameof(enhancer));
        _strategyInferrer = strategyInferrer ?? throw new ArgumentNullException(nameof(strategyInferrer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PipelineResult> ProcessFileFluxOutputAsync(
        dynamic fileFluxOutput,
        PipelineOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= PipelineOptions.Default;
        var stopwatch = Stopwatch.StartNew();
        var result = new PipelineResult();

        _logger.LogInformation("Starting FileFlux integration pipeline");

        try
        {
            // Step 1: Adapt chunks to documents
            var adaptationStage = await ExecuteAdaptationStageAsync(
                fileFluxOutput, 
                options,
                cancellationToken);
            result.AdaptedDocuments = adaptationStage.documents;
            result.InferredStrategies = adaptationStage.strategies;

            // Step 2: Enhance metadata
            if (options.EnableMetadataEnhancement)
            {
                result.AdaptedDocuments = await ExecuteEnhancementStageAsync(
                    result.AdaptedDocuments,
                    result.InferredStrategies,
                    cancellationToken);
            }

            // Step 3: Index documents
            if (options.EnableIndexing)
            {
                var indexingResults = await ExecuteIndexingStageAsync(
                    result.AdaptedDocuments,
                    result.InferredStrategies,
                    options,
                    cancellationToken);
                result.IndexingResults = indexingResults;
            }

            // Step 4: Validate and test retrieval
            if (options.EnableValidation)
            {
                result.ValidationResults = await ExecuteValidationStageAsync(
                    result.AdaptedDocuments,
                    options,
                    cancellationToken);
            }

            // Finalize
            stopwatch.Stop();
            result.Success = true;
            result.ProcessingTime = stopwatch.Elapsed;
            result.Statistics = _statistics.GetSnapshot();

            _logger.LogInformation("Pipeline completed successfully in {Time}ms", 
                stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline failed");
            stopwatch.Stop();
            
            result.Success = false;
            result.Error = ex.Message;
            result.ProcessingTime = stopwatch.Elapsed;
            
            throw;
        }
    }

    public async Task<BatchPipelineResult> ProcessBatchAsync(
        IEnumerable<dynamic> fileFluxBatches,
        BatchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= BatchOptions.Default;
        var batchResult = new BatchPipelineResult();
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Processing batch of {Count} FileFlux outputs", 
            fileFluxBatches.Count());

        // Process in parallel or sequential based on options
        if (options.EnableParallelProcessing)
        {
            await ProcessBatchParallelAsync(
                fileFluxBatches, 
                options, 
                batchResult,
                cancellationToken);
        }
        else
        {
            await ProcessBatchSequentialAsync(
                fileFluxBatches,
                options,
                batchResult,
                cancellationToken);
        }

        stopwatch.Stop();
        batchResult.TotalProcessingTime = stopwatch.Elapsed;
        batchResult.Statistics = _statistics.GetSnapshot();

        _logger.LogInformation("Batch processing completed: {Success}/{Total} successful",
            batchResult.SuccessCount, batchResult.TotalCount);

        return batchResult;
    }

    public async Task<OptimizationResult> OptimizePipelineAsync(
        PipelineMetrics metrics,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Optimizing pipeline based on metrics");

        var result = new OptimizationResult();

        try
        {
            // Analyze performance bottlenecks
            var bottlenecks = AnalyzeBottlenecks(metrics);
            result.IdentifiedBottlenecks = bottlenecks;

            // Generate optimization recommendations
            var recommendations = GenerateOptimizationRecommendations(bottlenecks, metrics);
            result.Recommendations = recommendations;

            // Apply automatic optimizations
            if (metrics.EnableAutoOptimization)
            {
                result.AppliedOptimizations = await ApplyOptimizationsAsync(
                    recommendations,
                    cancellationToken);
            }

            // Update indexing configuration based on metrics
            if (metrics.DocumentCount > 10000)
            {
                var metadata = new ChunkingMetadata
                {
                    QualityScore = metrics.AverageQualityScore,
                    ChunkSize = (int)metrics.AverageChunkSize
                };

                await _indexer.UpdateIndexingConfigAsync(metadata, cancellationToken);
                result.AppliedOptimizations.Add("Updated HNSW parameters for large dataset");
            }

            result.Success = true;
            _logger.LogInformation("Pipeline optimization completed with {Count} recommendations",
                result.Recommendations.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline optimization failed");
            result.Success = false;
            result.Error = ex.Message;
        }

        return result;
    }

    private async Task<(List<Document> documents, Dictionary<string, ChunkingStrategy> strategies)> 
        ExecuteAdaptationStageAsync(
            dynamic fileFluxOutput,
            PipelineOptions options,
            CancellationToken cancellationToken)
    {
        _logger.LogDebug("Executing adaptation stage");
        var stopwatch = Stopwatch.StartNew();

        var documents = new List<Document>();
        var strategies = new Dictionary<string, ChunkingStrategy>();

        try
        {
            // Adapt chunks to documents
            var adaptedDocs = await _adapter.AdaptChunksAsync(fileFluxOutput, cancellationToken);
            documents.AddRange(adaptedDocs);

            // Infer strategies for each document
            foreach (var doc in documents)
            {
                var metadata = _adapter.ExtractMetadata(fileFluxOutput);
                var strategy = _strategyInferrer.InferStrategy(metadata);
                strategies[doc.Id] = strategy;
                
                // Store strategy in document metadata
                doc.Metadata["inferred_strategy"] = strategy.ToString();
            }

            _statistics.AdaptationTime = stopwatch.Elapsed;
            _statistics.DocumentsAdapted = documents.Count;

            _logger.LogDebug("Adapted {Count} documents in {Time}ms",
                documents.Count, stopwatch.ElapsedMilliseconds);

            return (documents, strategies);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Adaptation stage failed");
            throw;
        }
    }

    private async Task<List<Document>> ExecuteEnhancementStageAsync(
        List<Document> documents,
        Dictionary<string, ChunkingStrategy> strategies,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Executing enhancement stage");
        var stopwatch = Stopwatch.StartNew();

        var enhanced = new List<Document>();

        foreach (var doc in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var strategy = strategies.GetValueOrDefault(doc.Id, ChunkingStrategy.Auto);
            
            // Enhance metadata
            var enhancedMetadata = await _enhancer.EnhanceAsync(
                doc.Metadata,
                doc.Content,
                strategy,
                cancellationToken);

            // Create enhanced document
            var enhancedDoc = Document.Create(
                doc.Content,
                enhancedMetadata,
                doc.EmbeddingVector);
            
            enhanced.Add(enhancedDoc);
        }

        _statistics.EnhancementTime = stopwatch.Elapsed;
        _statistics.MetadataFieldsAdded = enhanced.Sum(d => d.Metadata.Count) - 
                                          documents.Sum(d => d.Metadata.Count);

        _logger.LogDebug("Enhanced {Count} documents in {Time}ms",
            enhanced.Count, stopwatch.ElapsedMilliseconds);

        return enhanced;
    }

    private async Task<IndexingResults> ExecuteIndexingStageAsync(
        List<Document> documents,
        Dictionary<string, ChunkingStrategy> strategies,
        PipelineOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Executing indexing stage");
        var stopwatch = Stopwatch.StartNew();
        var results = new IndexingResults();

        try
        {
            // Group documents by strategy for optimized batch processing
            var strategyGroups = documents.GroupBy(d => 
                strategies.GetValueOrDefault(d.Id, ChunkingStrategy.Auto));

            foreach (var group in strategyGroups)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var indexingStrategy = DetermineIndexingStrategy(group.Key);
                
                if (options.EnableBatchIndexing && group.Count() > 5)
                {
                    // Batch index
                    await _indexer.BatchIndexAsync(
                        group,
                        indexingStrategy,
                        cancellationToken);
                    
                    results.BatchIndexedCount += group.Count();
                }
                else
                {
                    // Individual index
                    foreach (var doc in group)
                    {
                        await _indexer.IndexWithStrategyAsync(
                            doc,
                            indexingStrategy,
                            cancellationToken);
                        
                        results.IndividualIndexedCount++;
                    }
                }
            }

            results.TotalIndexed = results.BatchIndexedCount + results.IndividualIndexedCount;
            results.Success = true;
            results.IndexingTime = stopwatch.Elapsed;

            _statistics.IndexingTime = stopwatch.Elapsed;
            _statistics.DocumentsIndexed = results.TotalIndexed;

            _logger.LogDebug("Indexed {Count} documents in {Time}ms",
                results.TotalIndexed, stopwatch.ElapsedMilliseconds);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Indexing stage failed");
            results.Success = false;
            results.Error = ex.Message;
            return results;
        }
    }

    private async Task<ValidationResults> ExecuteValidationStageAsync(
        List<Document> documents,
        PipelineOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Executing validation stage");
        var stopwatch = Stopwatch.StartNew();
        var results = new ValidationResults();

        try
        {
            // Sample documents for validation
            var sampleSize = Math.Min(options.ValidationSampleSize, documents.Count);
            var sampleDocs = documents.Take(sampleSize).ToList();

            foreach (var doc in sampleDocs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Test retrieval
                var query = ExtractTestQuery(doc);
                var retrieved = await _retriever.RetrieveAsync(
                    query,
                    new RetrievalOptions { TopK = 5 },
                    cancellationToken);

                // Check if document was retrieved
                var found = retrieved.Any(r => r.Id == doc.Id);
                if (found)
                {
                    results.SuccessfulRetrievals++;
                }
                else
                {
                    results.FailedRetrievals++;
                    results.FailedDocumentIds.Add(doc.Id);
                }

                results.TotalValidations++;
            }

            results.Success = results.FailedRetrievals == 0;
            results.ValidationTime = stopwatch.Elapsed;
            results.RetrievalAccuracy = results.TotalValidations > 0 
                ? (double)results.SuccessfulRetrievals / results.TotalValidations
                : 0;

            _statistics.ValidationTime = stopwatch.Elapsed;

            _logger.LogDebug("Validated {Count} documents in {Time}ms, accuracy: {Accuracy:P}",
                results.TotalValidations, stopwatch.ElapsedMilliseconds, results.RetrievalAccuracy);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Validation stage failed");
            results.Success = false;
            results.Error = ex.Message;
            return results;
        }
    }

    private async Task ProcessBatchParallelAsync(
        IEnumerable<dynamic> batches,
        BatchOptions options,
        BatchPipelineResult result,
        CancellationToken cancellationToken)
    {
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = options.MaxParallelism
        };

        var batchList = batches.ToList();
        var batchResults = new PipelineResult[batchList.Count];

        await Parallel.ForEachAsync(
            batchList.Select((batch, index) => (batch, index)),
            parallelOptions,
            async (item, ct) =>
            {
                try
                {
                    var pipelineResult = await ProcessFileFluxOutputAsync(
                        item.batch,
                        options.PipelineOptions,
                        ct);
                    
                    batchResults[item.index] = pipelineResult;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Batch {Index} failed", item.index);
                    batchResults[item.index] = new PipelineResult
                    {
                        Success = false,
                        Error = ex.Message
                    };
                }
            });

        // Aggregate results
        foreach (var batchResult in batchResults)
        {
            result.BatchResults.Add(batchResult);
            result.TotalCount++;
            
            if (batchResult.Success)
            {
                result.SuccessCount++;
                result.TotalDocumentsProcessed += batchResult.AdaptedDocuments?.Count ?? 0;
            }
            else
            {
                result.FailedCount++;
            }
        }
    }

    private async Task ProcessBatchSequentialAsync(
        IEnumerable<dynamic> batches,
        BatchOptions options,
        BatchPipelineResult result,
        CancellationToken cancellationToken)
    {
        foreach (var batch in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var pipelineResult = await ProcessFileFluxOutputAsync(
                    batch,
                    options.PipelineOptions,
                    cancellationToken);

                result.BatchResults.Add(pipelineResult);
                result.TotalCount++;

                if (pipelineResult.Success)
                {
                    result.SuccessCount++;
                    result.TotalDocumentsProcessed += pipelineResult.AdaptedDocuments?.Count ?? 0;
                }
                else
                {
                    result.FailedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Batch processing failed");
                result.BatchResults.Add(new PipelineResult
                {
                    Success = false,
                    Error = ex.Message
                });
                result.FailedCount++;
                result.TotalCount++;
            }
        }
    }

    private List<string> AnalyzeBottlenecks(PipelineMetrics metrics)
    {
        var bottlenecks = new List<string>();

        // Analyze stage timings
        if (metrics.AverageAdaptationTime > TimeSpan.FromSeconds(1))
        {
            bottlenecks.Add("Adaptation stage is slow");
        }

        if (metrics.AverageIndexingTime > TimeSpan.FromSeconds(5))
        {
            bottlenecks.Add("Indexing stage is slow");
        }

        // Analyze document characteristics
        if (metrics.AverageChunkSize > 4096)
        {
            bottlenecks.Add("Chunk sizes are too large");
        }

        if (metrics.AverageQualityScore < 0.6)
        {
            bottlenecks.Add("Document quality is low");
        }

        // Analyze retrieval performance
        if (metrics.RetrievalAccuracy < 0.8)
        {
            bottlenecks.Add("Retrieval accuracy is below threshold");
        }

        return bottlenecks;
    }

    private List<string> GenerateOptimizationRecommendations(
        List<string> bottlenecks,
        PipelineMetrics metrics)
    {
        var recommendations = new List<string>();

        foreach (var bottleneck in bottlenecks)
        {
            switch (bottleneck)
            {
                case "Adaptation stage is slow":
                    recommendations.Add("Enable parallel chunk processing");
                    recommendations.Add("Implement chunk caching");
                    break;

                case "Indexing stage is slow":
                    recommendations.Add("Use batch indexing for large datasets");
                    recommendations.Add("Optimize HNSW parameters");
                    break;

                case "Chunk sizes are too large":
                    recommendations.Add("Reduce chunk size to 2048 tokens");
                    recommendations.Add("Implement sliding window chunking");
                    break;

                case "Document quality is low":
                    recommendations.Add("Enable metadata enhancement");
                    recommendations.Add("Implement quality-based filtering");
                    break;

                case "Retrieval accuracy is below threshold":
                    recommendations.Add("Enable hybrid search");
                    recommendations.Add("Implement reranking");
                    recommendations.Add("Increase embedding dimensions");
                    break;
            }
        }

        // Add general recommendations based on metrics
        if (metrics.DocumentCount > 10000)
        {
            recommendations.Add("Enable distributed processing");
        }

        if (metrics.UniqueStrategiesCount > 3)
        {
            recommendations.Add("Implement strategy-specific optimizations");
        }

        return recommendations.Distinct().ToList();
    }

    private async Task<List<string>> ApplyOptimizationsAsync(
        List<string> recommendations,
        CancellationToken cancellationToken)
    {
        var applied = new List<string>();

        foreach (var recommendation in recommendations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                switch (recommendation)
                {
                    case "Optimize HNSW parameters":
                        // This would be applied through the indexer
                        await _indexer.UpdateIndexingConfigAsync(
                            new ChunkingMetadata { QualityScore = 0.8 },
                            cancellationToken);
                        applied.Add(recommendation);
                        break;

                    // Add more automatic optimizations as needed
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to apply optimization: {Recommendation}", 
                    recommendation);
            }
        }

        return applied;
    }

    private IndexingStrategy DetermineIndexingStrategy(ChunkingStrategy chunkingStrategy)
    {
        return chunkingStrategy switch
        {
            ChunkingStrategy.Intelligent => IndexingStrategy.HighQuality,
            ChunkingStrategy.Smart => IndexingStrategy.Hybrid,
            ChunkingStrategy.Semantic => IndexingStrategy.Semantic,
            ChunkingStrategy.MemoryOptimized => IndexingStrategy.Compressed,
            ChunkingStrategy.FixedSize => IndexingStrategy.Keyword,
            _ => IndexingStrategy.Hybrid
        };
    }

    private string ExtractTestQuery(Document document)
    {
        // Extract first sentence or first 50 words as test query
        var content = document.Content;
        var sentences = content.Split(new[] { '.', '!', '?' }, 
            StringSplitOptions.RemoveEmptyEntries);
        
        if (sentences.Length > 0)
        {
            return sentences[0].Trim();
        }

        var words = content.Split(' ').Take(50);
        return string.Join(" ", words);
    }
}

/// <summary>
/// Pipeline execution options
/// </summary>
public class PipelineOptions
{
    public bool EnableMetadataEnhancement { get; set; } = true;
    public bool EnableIndexing { get; set; } = true;
    public bool EnableValidation { get; set; } = true;
    public bool EnableBatchIndexing { get; set; } = true;
    public int ValidationSampleSize { get; set; } = 10;

    public static PipelineOptions Default => new();
}

/// <summary>
/// Batch processing options
/// </summary>
public class BatchOptions
{
    public bool EnableParallelProcessing { get; set; } = true;
    public int MaxParallelism { get; set; } = Environment.ProcessorCount;
    public PipelineOptions PipelineOptions { get; set; } = PipelineOptions.Default;

    public static BatchOptions Default => new();
}

/// <summary>
/// Pipeline execution result
/// </summary>
public class PipelineResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<Document>? AdaptedDocuments { get; set; }
    public Dictionary<string, ChunkingStrategy>? InferredStrategies { get; set; }
    public IndexingResults? IndexingResults { get; set; }
    public ValidationResults? ValidationResults { get; set; }
    public TimeSpan ProcessingTime { get; set; }
    public PipelineStatistics? Statistics { get; set; }
}

/// <summary>
/// Batch pipeline result
/// </summary>
public class BatchPipelineResult
{
    public List<PipelineResult> BatchResults { get; set; } = new();
    public int TotalCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public int TotalDocumentsProcessed { get; set; }
    public TimeSpan TotalProcessingTime { get; set; }
    public PipelineStatistics? Statistics { get; set; }
}

/// <summary>
/// Indexing stage results
/// </summary>
public class IndexingResults
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int TotalIndexed { get; set; }
    public int BatchIndexedCount { get; set; }
    public int IndividualIndexedCount { get; set; }
    public TimeSpan IndexingTime { get; set; }
}

/// <summary>
/// Validation stage results
/// </summary>
public class ValidationResults
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int TotalValidations { get; set; }
    public int SuccessfulRetrievals { get; set; }
    public int FailedRetrievals { get; set; }
    public double RetrievalAccuracy { get; set; }
    public List<string> FailedDocumentIds { get; set; } = new();
    public TimeSpan ValidationTime { get; set; }
}

/// <summary>
/// Pipeline optimization result
/// </summary>
public class OptimizationResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<string> IdentifiedBottlenecks { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
    public List<string> AppliedOptimizations { get; set; } = new();
}

/// <summary>
/// Pipeline performance metrics
/// </summary>
public class PipelineMetrics
{
    public int DocumentCount { get; set; }
    public double AverageChunkSize { get; set; }
    public double AverageQualityScore { get; set; }
    public int UniqueStrategiesCount { get; set; }
    public TimeSpan AverageAdaptationTime { get; set; }
    public TimeSpan AverageIndexingTime { get; set; }
    public double RetrievalAccuracy { get; set; }
    public bool EnableAutoOptimization { get; set; }
}

/// <summary>
/// Pipeline execution statistics
/// </summary>
public class PipelineStatistics
{
    public int DocumentsAdapted { get; set; }
    public int DocumentsIndexed { get; set; }
    public int MetadataFieldsAdded { get; set; }
    public TimeSpan AdaptationTime { get; set; }
    public TimeSpan EnhancementTime { get; set; }
    public TimeSpan IndexingTime { get; set; }
    public TimeSpan ValidationTime { get; set; }

    public PipelineStatistics GetSnapshot()
    {
        return new PipelineStatistics
        {
            DocumentsAdapted = DocumentsAdapted,
            DocumentsIndexed = DocumentsIndexed,
            MetadataFieldsAdded = MetadataFieldsAdded,
            AdaptationTime = AdaptationTime,
            EnhancementTime = EnhancementTime,
            IndexingTime = IndexingTime,
            ValidationTime = ValidationTime
        };
    }
}