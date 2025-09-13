using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Extensions.FileFlux.Interfaces;
using FluxIndex.SDK;
using FluxIndex.SDK.Interfaces;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Extensions.FileFlux.Indexing;

/// <summary>
/// Smart indexer that optimizes indexing based on chunk characteristics
/// </summary>
public class SmartIndexer : ISmartIndexer
{
    private readonly IIndexingService _indexingService;
    private readonly IVectorStore _vectorStore;
    private readonly IVectorIndexOptimizer? _optimizer;
    private readonly ISemanticCache? _cache;
    private readonly ILogger<SmartIndexer> _logger;

    public SmartIndexer(
        IIndexingService indexingService,
        IVectorStore vectorStore,
        ILogger<SmartIndexer> logger,
        IVectorIndexOptimizer? optimizer = null,
        ISemanticCache? cache = null)
    {
        _indexingService = indexingService ?? throw new ArgumentNullException(nameof(indexingService));
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _optimizer = optimizer;
        _cache = cache;
    }

    public async Task IndexWithStrategyAsync(
        Document document,
        IndexingStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Indexing document {Id} with strategy {Strategy}", 
            document.Id, strategy);

        try
        {
            // Apply strategy-specific optimizations
            await ApplyStrategyOptimizationsAsync(document, strategy, cancellationToken);

            // Check cache for similar documents
            if (_cache != null && strategy != IndexingStrategy.Keyword)
            {
                var cached = await CheckCacheAsync(document, cancellationToken);
                if (cached)
                {
                    _logger.LogDebug("Document {Id} found in cache, skipping indexing", document.Id);
                    return;
                }
            }

            // Perform indexing
            var options = new IndexingOptions
            {
                MaxChunkSize = 512,
                OverlapSize = 50
            };
            await _indexingService.IndexDocumentAsync(document.FilePath, options, cancellationToken);

            // Update cache
            if (_cache != null && strategy == IndexingStrategy.HighQuality)
            {
                await UpdateCacheAsync(document, cancellationToken);
            }

            _logger.LogInformation("Successfully indexed document {Id} with strategy {Strategy}",
                document.Id, strategy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error indexing document {Id} with strategy {Strategy}",
                document.Id, strategy);
            throw;
        }
    }

    public async Task BatchIndexAsync(
        IEnumerable<Document> documents,
        IndexingStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        var docList = documents.ToList();
        _logger.LogInformation("Batch indexing {Count} documents with strategy {Strategy}",
            docList.Count, strategy);

        // Group documents by characteristics for optimized batch processing
        var groups = GroupDocumentsByCharacteristics(docList, strategy);

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Apply group-level optimizations
            await ApplyGroupOptimizationsAsync(group.ToList(), strategy, cancellationToken);

            // Index group in parallel if suitable
            if (ShouldParallelIndex(strategy, group.Count()))
            {
                await ParallelIndexAsync(group, strategy, cancellationToken);
            }
            else
            {
                await SequentialIndexAsync(group, strategy, cancellationToken);
            }
        }

        _logger.LogInformation("Batch indexing completed for {Count} documents", docList.Count);
    }

    public async Task UpdateIndexingConfigAsync(
        ChunkingMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (_optimizer == null)
        {
            _logger.LogDebug("No optimizer available, skipping config update");
            return;
        }

        try
        {
            // Determine quality target based on metadata
            var qualityTarget = DetermineQualityTarget(metadata);

            // Get dataset characteristics
            // Note: GetStatisticsAsync is not available in IVectorStore, using defaults
            var datasetSize = 1000;  // Default estimate
            var dimensions = 1536;   // Default embedding dimensions

            // Optimize HNSW parameters
            var optimizedParams = await _optimizer.OptimizeParametersAsync(
                datasetSize,
                dimensions,
                qualityTarget,
                cancellationToken);

            // Apply optimized parameters
            await ApplyOptimizedParametersAsync(optimizedParams, cancellationToken);

            _logger.LogInformation("Updated indexing config with optimized parameters: M={M}, EfConstruction={Ef}",
                optimizedParams.M, optimizedParams.EfConstruction);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update indexing configuration");
        }
    }

    private async Task ApplyStrategyOptimizationsAsync(
        Document document,
        IndexingStrategy strategy,
        CancellationToken cancellationToken)
    {
        switch (strategy)
        {
            case IndexingStrategy.HighQuality:
                // Ensure high-quality indexing
                document.Metadata.Properties["index_priority"] = "high";
                document.Metadata.Properties["require_exact_match"] = "false";
                break;

            case IndexingStrategy.Semantic:
                // Optimize for semantic search
                document.Metadata.Properties["search_type"] = "semantic";
                document.Metadata.Properties["use_synonyms"] = "true";
                break;

            case IndexingStrategy.Hybrid:
                // Balance between semantic and keyword
                document.Metadata.Properties["search_type"] = "hybrid";
                document.Metadata.Properties["hybrid_weight"] = "0.7";
                break;

            case IndexingStrategy.Keyword:
                // Optimize for keyword search
                document.Metadata.Properties["search_type"] = "keyword";
                document.Metadata.Properties["tokenize_aggressive"] = "true";
                break;

            case IndexingStrategy.Compressed:
                // Apply compression if possible
                if (document.Content.Length > 1000)
                {
                    document.Content = await CompressContentAsync(document.Content, cancellationToken);
                    document.Metadata["compressed"] = "true";
                }
                break;
        }

        await Task.CompletedTask;
    }

    private async Task<bool> CheckCacheAsync(Document document, CancellationToken cancellationToken)
    {
        if (_cache == null) return false;

        try
        {
            var cacheResult = await _cache.GetAsync(
                document.Content,
                similarityThreshold: 0.95f,
                maxResults: 1,
                cancellationToken);

            return cacheResult != null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache check failed for document {Id}", document.Id);
            return false;
        }
    }

    private async Task UpdateCacheAsync(Document document, CancellationToken cancellationToken)
    {
        if (_cache == null) return;

        try
        {
            await _cache.SetAsync(
                document.Content,
                new CacheEntry
                {
                    DocumentId = document.Id,
                    Content = document.Content,
                    Metadata = document.Metadata,
                    Timestamp = DateTime.UtcNow
                },
                TimeSpan.FromHours(24),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update cache for document {Id}", document.Id);
        }
    }

    private IEnumerable<IGrouping<string, Document>> GroupDocumentsByCharacteristics(
        IEnumerable<Document> documents,
        IndexingStrategy strategy)
    {
        // Group by similar characteristics for batch optimization
        return documents.GroupBy(doc =>
        {
            var size = doc.Content.Length;
            var hasCode = doc.Metadata.GetValueOrDefault("has_code_blocks", "false");
            var language = doc.Metadata.GetValueOrDefault("has_non_ascii", "false");

            // Create group key based on characteristics
            var sizeCategory = size switch
            {
                < 500 => "small",
                < 2000 => "medium",
                _ => "large"
            };

            return $"{sizeCategory}_{hasCode}_{language}_{strategy}";
        });
    }

    private async Task ApplyGroupOptimizationsAsync(
        List<Document> documents,
        IndexingStrategy strategy,
        CancellationToken cancellationToken)
    {
        if (!documents.Any()) return;

        // Calculate group statistics
        var avgSize = documents.Average(d => d.Content.Length);
        var hasNonAscii = documents.Any(d => 
            d.Metadata.GetValueOrDefault("has_non_ascii", "false") == "true");

        // Apply group-level optimizations
        if (_optimizer != null && documents.Count > 10)
        {
            var qualityTarget = strategy switch
            {
                IndexingStrategy.HighQuality => QualityTarget.Quality,
                IndexingStrategy.Compressed => QualityTarget.Speed,
                _ => QualityTarget.Balanced
            };

            var parameters = await _optimizer.OptimizeParametersAsync(
                documents.Count,
                1536, // Default embedding dimension
                qualityTarget,
                cancellationToken);

            // Store optimization hint for the group
            foreach (var doc in documents)
            {
                doc.Metadata["hnsw_m"] = parameters.M.ToString();
                doc.Metadata["hnsw_ef"] = parameters.EfSearch.ToString();
            }
        }

        await Task.CompletedTask;
    }

    private bool ShouldParallelIndex(IndexingStrategy strategy, int documentCount)
    {
        // Parallel indexing for large batches with suitable strategies
        return documentCount > 5 && 
               strategy != IndexingStrategy.Compressed && // Compression is CPU intensive
               strategy != IndexingStrategy.HighQuality;   // High quality needs sequential processing
    }

    private async Task ParallelIndexAsync(
        IEnumerable<Document> documents,
        IndexingStrategy strategy,
        CancellationToken cancellationToken)
    {
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Environment.ProcessorCount / 2 // Conservative parallelism
        };

        await Parallel.ForEachAsync(documents, parallelOptions, async (doc, ct) =>
        {
            await IndexWithStrategyAsync(doc, strategy, ct);
        });
    }

    private async Task SequentialIndexAsync(
        IEnumerable<Document> documents,
        IndexingStrategy strategy,
        CancellationToken cancellationToken)
    {
        foreach (var doc in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await IndexWithStrategyAsync(doc, strategy, cancellationToken);
        }
    }

    private QualityTarget DetermineQualityTarget(ChunkingMetadata metadata)
    {
        if (metadata.QualityScore.HasValue && metadata.QualityScore > 0.8)
            return QualityTarget.Quality;

        if (metadata.ChunkingStrategy?.ToLowerInvariant() == "memoryoptimized")
            return QualityTarget.Speed;

        return QualityTarget.Balanced;
    }

    private async Task ApplyOptimizedParametersAsync(
        HNSWParameters parameters,
        CancellationToken cancellationToken)
    {
        // Apply parameters to vector store if supported
        if (_vectorStore is IConfigurableVectorStore configurableStore)
        {
            await configurableStore.ConfigureAsync(new VectorStoreConfiguration
            {
                HnswM = parameters.M,
                HnswEfConstruction = parameters.EfConstruction,
                HnswEfSearch = parameters.EfSearch
            }, cancellationToken);
        }

        await Task.CompletedTask;
    }

    private async Task<string> CompressContentAsync(string content, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        // Simple compression: remove extra whitespace
        var compressed = System.Text.RegularExpressions.Regex.Replace(content, @"\s+", " ");
        
        // Remove redundant punctuation
        compressed = System.Text.RegularExpressions.Regex.Replace(compressed, @"\.{2,}", ".");
        
        return compressed.Trim();
    }

    private class CacheEntry
    {
        public string DocumentId { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public Dictionary<string, string> Metadata { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }
}

/// <summary>
/// Interface for configurable vector stores
/// </summary>
public interface IConfigurableVectorStore
{
    Task ConfigureAsync(VectorStoreConfiguration configuration, CancellationToken cancellationToken = default);
}

/// <summary>
/// Vector store configuration
/// </summary>
public class VectorStoreConfiguration
{
    public int HnswM { get; set; }
    public int HnswEfConstruction { get; set; }
    public int HnswEfSearch { get; set; }
}