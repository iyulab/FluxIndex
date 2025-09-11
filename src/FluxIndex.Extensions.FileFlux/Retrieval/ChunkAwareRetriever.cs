using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Extensions.FileFlux.Interfaces;
using FluxIndex.Extensions.FileFlux.Strategies;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Extensions.FileFlux.Retrieval;

/// <summary>
/// Chunk-aware retriever that optimizes search based on chunk characteristics
/// </summary>
public class ChunkAwareRetriever : IChunkAwareRetriever
{
    private readonly ISearchService _searchService;
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly ISemanticCache? _cache;
    private readonly ILogger<ChunkAwareRetriever> _logger;

    public ChunkAwareRetriever(
        ISearchService searchService,
        IVectorStore vectorStore,
        IEmbeddingService embeddingService,
        ILogger<ChunkAwareRetriever> logger,
        ISemanticCache? cache = null)
    {
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cache = cache;
    }

    public async Task<IEnumerable<Document>> RetrieveAsync(
        string query,
        RetrievalOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= RetrievalOptions.Default;
        
        _logger.LogDebug("Retrieving documents for query: {Query}", query);

        try
        {
            // Check cache first
            if (_cache != null && options.UseCache)
            {
                var cached = await GetCachedResultsAsync(query, options, cancellationToken);
                if (cached != null && cached.Any())
                {
                    _logger.LogDebug("Returning {Count} cached results", cached.Count());
                    return cached;
                }
            }

            // Determine optimal search strategy
            var strategy = DetermineSearchStrategy(query, options);
            _logger.LogDebug("Using search strategy: {Strategy}", strategy);

            // Execute search with strategy
            var results = await ExecuteStrategySearchAsync(query, strategy, options, cancellationToken);

            // Apply chunk-aware post-processing
            results = await PostProcessResultsAsync(results, query, strategy, options, cancellationToken);

            // Update cache
            if (_cache != null && options.UseCache && results.Any())
            {
                await UpdateCacheAsync(query, results, options, cancellationToken);
            }

            _logger.LogInformation("Retrieved {Count} documents for query", results.Count());
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving documents for query: {Query}", query);
            throw;
        }
    }

    public async Task<IEnumerable<Document>> RetrieveWithExpansionAsync(
        string query,
        ExpansionOptions expansionOptions,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Retrieving with expansion for query: {Query}", query);

        // Get initial results
        var coreResults = await RetrieveAsync(
            query, 
            new RetrievalOptions { TopK = expansionOptions.InitialTopK },
            cancellationToken);

        if (!coreResults.Any())
        {
            return Enumerable.Empty<Document>();
        }

        // Analyze chunk metadata to determine expansion strategy
        var expansionStrategy = DetermineExpansionStrategy(coreResults);
        
        // Expand based on strategy
        var expandedResults = await ExpandResultsAsync(
            coreResults, 
            expansionStrategy, 
            expansionOptions,
            cancellationToken);

        // Merge and deduplicate
        var allResults = MergeResults(coreResults, expandedResults, expansionOptions);

        _logger.LogInformation("Retrieved {Core} core + {Expanded} expanded = {Total} total documents",
            coreResults.Count(), expandedResults.Count(), allResults.Count());

        return allResults;
    }

    public async Task<IEnumerable<ChunkContext>> RetrieveChunkContextAsync(
        string documentId,
        int chunkIndex,
        int contextWindow,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Retrieving chunk context for document {Id}, chunk {Index}", 
            documentId, chunkIndex);

        var contexts = new List<ChunkContext>();

        try
        {
            // Get target chunk
            var targetChunk = await GetChunkAsync(documentId, chunkIndex, cancellationToken);
            if (targetChunk == null)
            {
                return contexts;
            }

            contexts.Add(new ChunkContext
            {
                Document = targetChunk,
                ChunkIndex = chunkIndex,
                IsTarget = true,
                RelativePosition = 0
            });

            // Get surrounding chunks
            for (int offset = 1; offset <= contextWindow; offset++)
            {
                // Previous chunks
                var prevChunk = await GetChunkAsync(documentId, chunkIndex - offset, cancellationToken);
                if (prevChunk != null)
                {
                    contexts.Insert(0, new ChunkContext
                    {
                        Document = prevChunk,
                        ChunkIndex = chunkIndex - offset,
                        IsTarget = false,
                        RelativePosition = -offset
                    });
                }

                // Next chunks
                var nextChunk = await GetChunkAsync(documentId, chunkIndex + offset, cancellationToken);
                if (nextChunk != null)
                {
                    contexts.Add(new ChunkContext
                    {
                        Document = nextChunk,
                        ChunkIndex = chunkIndex + offset,
                        IsTarget = false,
                        RelativePosition = offset
                    });
                }
            }

            return contexts;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving chunk context");
            throw;
        }
    }

    public async Task<IEnumerable<Document>> RetrieveAdaptiveAsync(
        string query,
        AdaptiveOptions adaptiveOptions,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Adaptive retrieval for query: {Query}", query);

        var allResults = new List<Document>();
        var currentTopK = adaptiveOptions.InitialTopK;
        var iteration = 0;
        var confidenceScore = 0.0;

        while (iteration < adaptiveOptions.MaxIterations && 
               confidenceScore < adaptiveOptions.ConfidenceThreshold)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Retrieve batch
            var batchResults = await RetrieveAsync(
                query,
                new RetrievalOptions { TopK = currentTopK },
                cancellationToken);

            // Calculate confidence
            confidenceScore = CalculateConfidence(batchResults, query);
            
            _logger.LogDebug("Iteration {Iteration}: Retrieved {Count} docs, confidence {Score}",
                iteration, batchResults.Count(), confidenceScore);

            // Add new results
            foreach (var result in batchResults)
            {
                if (!allResults.Any(r => r.Id == result.Id))
                {
                    allResults.Add(result);
                }
            }

            // Adapt parameters for next iteration
            if (confidenceScore < adaptiveOptions.ConfidenceThreshold)
            {
                currentTopK = Math.Min(currentTopK * 2, adaptiveOptions.MaxTopK);
                
                // Modify search strategy based on results
                if (iteration > 0 && !HasImproved(confidenceScore, iteration))
                {
                    // Switch strategy if not improving
                    break;
                }
            }

            iteration++;
        }

        // Final ranking
        var rankedResults = RankAdaptiveResults(allResults, query, adaptiveOptions);

        _logger.LogInformation("Adaptive retrieval completed in {Iterations} iterations, {Count} results",
            iteration, rankedResults.Count());

        return rankedResults;
    }

    private SearchStrategy DetermineSearchStrategy(string query, RetrievalOptions options)
    {
        // Analyze query characteristics
        var queryLength = query.Split(' ').Length;
        var hasKeywords = ContainsKeywords(query);
        var hasNaturalLanguage = queryLength > 5;

        // Check for explicit strategy in options
        if (options.Properties.TryGetValue("search_strategy", out var strategyValue))
        {
            if (Enum.TryParse<SearchStrategy>(strategyValue.ToString(), out var explicitStrategy))
            {
                return explicitStrategy;
            }
        }

        // Infer strategy from query
        if (hasNaturalLanguage && !hasKeywords)
        {
            return SearchStrategy.Semantic;
        }
        
        if (hasKeywords && !hasNaturalLanguage)
        {
            return SearchStrategy.Keyword;
        }

        return SearchStrategy.Hybrid;
    }

    private async Task<IEnumerable<Document>> ExecuteStrategySearchAsync(
        string query,
        SearchStrategy strategy,
        RetrievalOptions options,
        CancellationToken cancellationToken)
    {
        switch (strategy)
        {
            case SearchStrategy.Semantic:
                return await ExecuteSemanticSearchAsync(query, options, cancellationToken);
            
            case SearchStrategy.Keyword:
                return await ExecuteKeywordSearchAsync(query, options, cancellationToken);
            
            case SearchStrategy.Hybrid:
                return await ExecuteHybridSearchAsync(query, options, cancellationToken);
            
            default:
                return await _searchService.SearchAsync(query, options.TopK, cancellationToken);
        }
    }

    private async Task<IEnumerable<Document>> ExecuteSemanticSearchAsync(
        string query,
        RetrievalOptions options,
        CancellationToken cancellationToken)
    {
        // Generate embedding
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
        
        // Search with semantic focus
        var results = await _vectorStore.SearchAsync(
            queryEmbedding,
            options.TopK * 2, // Over-retrieve for reranking
            options.MinScore,
            cancellationToken);

        // Apply semantic-specific filtering
        return results.Where(r => 
        {
            var metadata = r.Metadata;
            return metadata.GetValueOrDefault("requires_semantic_search", "false") != "false" ||
                   metadata.GetValueOrDefault("search_type", "") == "semantic";
        }).Take(options.TopK);
    }

    private async Task<IEnumerable<Document>> ExecuteKeywordSearchAsync(
        string query,
        RetrievalOptions options,
        CancellationToken cancellationToken)
    {
        // This would integrate with a keyword search system
        // For now, use standard search with keyword-optimized parameters
        var results = await _searchService.SearchAsync(
            query,
            options.TopK * 3, // Over-retrieve for keyword matching
            cancellationToken);

        // Apply keyword-specific filtering
        return results.Where(r =>
        {
            var content = r.Content.ToLower();
            var queryTerms = query.ToLower().Split(' ');
            return queryTerms.Any(term => content.Contains(term));
        }).Take(options.TopK);
    }

    private async Task<IEnumerable<Document>> ExecuteHybridSearchAsync(
        string query,
        RetrievalOptions options,
        CancellationToken cancellationToken)
    {
        // Execute both searches in parallel
        var semanticTask = ExecuteSemanticSearchAsync(query, options, cancellationToken);
        var keywordTask = ExecuteKeywordSearchAsync(query, options, cancellationToken);

        await Task.WhenAll(semanticTask, keywordTask);

        var semanticResults = await semanticTask;
        var keywordResults = await keywordTask;

        // Merge with weighted ranking
        return MergeHybridResults(semanticResults, keywordResults, options);
    }

    private IEnumerable<Document> MergeHybridResults(
        IEnumerable<Document> semanticResults,
        IEnumerable<Document> keywordResults,
        RetrievalOptions options)
    {
        var hybridWeight = options.Properties.GetValueOrDefault("hybrid_weight", "0.7");
        var semanticWeight = double.Parse(hybridWeight.ToString() ?? "0.7");
        var keywordWeight = 1.0 - semanticWeight;

        var mergedScores = new Dictionary<string, (Document doc, double score)>();

        // Add semantic results with weight
        foreach (var doc in semanticResults)
        {
            mergedScores[doc.Id] = (doc, semanticWeight);
        }

        // Add/update keyword results with weight
        foreach (var doc in keywordResults)
        {
            if (mergedScores.ContainsKey(doc.Id))
            {
                var existing = mergedScores[doc.Id];
                mergedScores[doc.Id] = (doc, existing.score + keywordWeight);
            }
            else
            {
                mergedScores[doc.Id] = (doc, keywordWeight);
            }
        }

        // Sort by combined score
        return mergedScores
            .OrderByDescending(kvp => kvp.Value.score)
            .Select(kvp => kvp.Value.doc)
            .Take(options.TopK);
    }

    private async Task<IEnumerable<Document>> PostProcessResultsAsync(
        IEnumerable<Document> results,
        string query,
        SearchStrategy strategy,
        RetrievalOptions options,
        CancellationToken cancellationToken)
    {
        var processed = results.ToList();

        // Apply chunk-aware optimizations
        foreach (var doc in processed)
        {
            // Check if document needs expansion
            if (ShouldExpandChunk(doc))
            {
                var expanded = await ExpandChunkAsync(doc, cancellationToken);
                if (expanded != null)
                {
                    // Replace with expanded version
                    var index = processed.IndexOf(doc);
                    processed[index] = expanded;
                }
            }

            // Enhance metadata for better context
            EnhanceRetrievalMetadata(doc, strategy);
        }

        return processed;
    }

    private bool ShouldExpandChunk(Document document)
    {
        var metadata = document.Metadata;
        
        // Check expansion hints
        if (metadata.GetValueOrDefault("expand_with_overlap", "false") == "true")
            return true;
        
        // Check completeness
        if (metadata.TryGetValue("quality_completeness", out var completeness))
        {
            if (double.TryParse(completeness, out var score) && score < 0.7)
                return true;
        }

        return false;
    }

    private async Task<Document?> ExpandChunkAsync(
        Document document,
        CancellationToken cancellationToken)
    {
        // Get context window size from metadata
        var windowSize = 1;
        if (document.Metadata.TryGetValue("context_window_size", out var windowValue))
        {
            int.TryParse(windowValue, out windowSize);
        }

        // Get surrounding chunks
        var context = await RetrieveChunkContextAsync(
            document.Id,
            GetChunkIndex(document),
            windowSize,
            cancellationToken);

        if (!context.Any())
            return null;

        // Merge content
        var expandedContent = string.Join("\n\n", context.Select(c => c.Document.Content));
        
        // Create expanded document
        return Document.Create(
            expandedContent,
            document.Metadata,
            document.EmbeddingVector);
    }

    private void EnhanceRetrievalMetadata(Document document, SearchStrategy strategy)
    {
        document.Metadata["retrieval_strategy"] = strategy.ToString();
        document.Metadata["retrieval_timestamp"] = DateTime.UtcNow.ToString("O");
        
        // Add strategy-specific enhancements
        switch (strategy)
        {
            case SearchStrategy.Semantic:
                document.Metadata["retrieval_type"] = "semantic_enhanced";
                break;
            case SearchStrategy.Keyword:
                document.Metadata["retrieval_type"] = "keyword_matched";
                break;
            case SearchStrategy.Hybrid:
                document.Metadata["retrieval_type"] = "hybrid_optimized";
                break;
        }
    }

    private ExpansionStrategy DetermineExpansionStrategy(IEnumerable<Document> documents)
    {
        // Analyze metadata to determine best expansion strategy
        var hasOverlap = documents.Any(d => 
            d.Metadata.GetValueOrDefault("expand_with_overlap", "false") == "true");
        
        var avgCompleteness = documents
            .Select(d => d.Metadata.GetValueOrDefault("quality_completeness", "1.0"))
            .Select(v => double.TryParse(v, out var score) ? score : 1.0)
            .Average();

        if (hasOverlap && avgCompleteness < 0.8)
        {
            return ExpansionStrategy.Aggressive;
        }
        
        if (avgCompleteness < 0.6)
        {
            return ExpansionStrategy.Moderate;
        }

        return ExpansionStrategy.Conservative;
    }

    private async Task<IEnumerable<Document>> ExpandResultsAsync(
        IEnumerable<Document> coreResults,
        ExpansionStrategy strategy,
        ExpansionOptions options,
        CancellationToken cancellationToken)
    {
        var expanded = new List<Document>();

        foreach (var doc in coreResults)
        {
            var windowSize = strategy switch
            {
                ExpansionStrategy.Aggressive => 3,
                ExpansionStrategy.Moderate => 2,
                ExpansionStrategy.Conservative => 1,
                _ => 1
            };

            var context = await RetrieveChunkContextAsync(
                doc.Id,
                GetChunkIndex(doc),
                windowSize,
                cancellationToken);

            expanded.AddRange(context.Where(c => !c.IsTarget).Select(c => c.Document));
        }

        return expanded.Distinct();
    }

    private IEnumerable<Document> MergeResults(
        IEnumerable<Document> coreResults,
        IEnumerable<Document> expandedResults,
        ExpansionOptions options)
    {
        var merged = new List<Document>(coreResults);
        
        foreach (var expanded in expandedResults)
        {
            if (!merged.Any(d => d.Id == expanded.Id))
            {
                merged.Add(expanded);
            }
        }

        return merged.Take(options.MaxResults);
    }

    private double CalculateConfidence(IEnumerable<Document> results, string query)
    {
        if (!results.Any())
            return 0.0;

        // Simple confidence based on quality scores and relevance
        var scores = results.Select(r =>
        {
            var qualityScore = 0.5;
            if (r.Metadata.TryGetValue("quality_overall", out var quality))
            {
                double.TryParse(quality, out qualityScore);
            }

            // Simple keyword match score
            var queryTerms = query.ToLower().Split(' ');
            var content = r.Content.ToLower();
            var matchScore = queryTerms.Count(term => content.Contains(term)) / (double)queryTerms.Length;

            return (qualityScore + matchScore) / 2;
        });

        return scores.Average();
    }

    private bool HasImproved(double currentConfidence, int iteration)
    {
        // Simple improvement check - could track history
        return iteration == 0 || currentConfidence > 0.3;
    }

    private IEnumerable<Document> RankAdaptiveResults(
        List<Document> results,
        string query,
        AdaptiveOptions options)
    {
        // Final ranking considering multiple factors
        return results.OrderByDescending(r =>
        {
            var score = 0.0;
            
            // Quality score
            if (r.Metadata.TryGetValue("quality_overall", out var quality))
            {
                score += double.TryParse(quality, out var q) ? q : 0.5;
            }

            // Relevance (simple keyword match)
            var queryTerms = query.ToLower().Split(' ');
            var content = r.Content.ToLower();
            score += queryTerms.Count(term => content.Contains(term)) / (double)queryTerms.Length;

            return score;
        }).Take(options.MaxResults);
    }

    private bool ContainsKeywords(string query)
    {
        // Simple keyword detection
        var keywords = new[] { "AND", "OR", "NOT", "\"", "*", "?" };
        return keywords.Any(kw => query.Contains(kw));
    }

    private int GetChunkIndex(Document document)
    {
        if (document.Metadata.TryGetValue("chunk_index", out var index))
        {
            if (int.TryParse(index, out var chunkIndex))
                return chunkIndex;
        }
        return 0;
    }

    private async Task<Document?> GetChunkAsync(
        string documentId,
        int chunkIndex,
        CancellationToken cancellationToken)
    {
        // Query for specific chunk
        // This would need integration with the storage layer
        try
        {
            var results = await _vectorStore.SearchAsync(
                new float[0], // Empty vector for metadata-only search
                100,
                0,
                cancellationToken);

            return results.FirstOrDefault(r =>
                r.Id.StartsWith(documentId) &&
                r.Metadata.GetValueOrDefault("chunk_index", "-1") == chunkIndex.ToString());
        }
        catch
        {
            return null;
        }
    }

    private async Task<IEnumerable<Document>?> GetCachedResultsAsync(
        string query,
        RetrievalOptions options,
        CancellationToken cancellationToken)
    {
        if (_cache == null)
            return null;

        try
        {
            var cached = await _cache.GetAsync(
                query,
                similarityThreshold: 0.9f,
                maxResults: options.TopK,
                cancellationToken);

            if (cached != null && cached is IEnumerable<Document> documents)
            {
                return documents;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache retrieval failed");
        }

        return null;
    }

    private async Task UpdateCacheAsync(
        string query,
        IEnumerable<Document> results,
        RetrievalOptions options,
        CancellationToken cancellationToken)
    {
        if (_cache == null)
            return;

        try
        {
            await _cache.SetAsync(
                query,
                results,
                TimeSpan.FromMinutes(options.CacheDurationMinutes),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache update failed");
        }
    }

    private enum SearchStrategy
    {
        Semantic,
        Keyword,
        Hybrid
    }

    private enum ExpansionStrategy
    {
        Conservative,
        Moderate,
        Aggressive
    }
}

/// <summary>
/// Retrieval options
/// </summary>
public class RetrievalOptions
{
    public int TopK { get; set; } = 10;
    public double MinScore { get; set; } = 0.5;
    public bool UseCache { get; set; } = true;
    public int CacheDurationMinutes { get; set; } = 60;
    public Dictionary<string, object> Properties { get; set; } = new();

    public static RetrievalOptions Default => new();
}

/// <summary>
/// Expansion options for context retrieval
/// </summary>
public class ExpansionOptions
{
    public int InitialTopK { get; set; } = 5;
    public int MaxResults { get; set; } = 20;
    public int ContextWindow { get; set; } = 2;
}

/// <summary>
/// Adaptive retrieval options
/// </summary>
public class AdaptiveOptions
{
    public int InitialTopK { get; set; } = 5;
    public int MaxTopK { get; set; } = 50;
    public int MaxIterations { get; set; } = 3;
    public double ConfidenceThreshold { get; set; } = 0.8;
    public int MaxResults { get; set; } = 20;
}

/// <summary>
/// Chunk context information
/// </summary>
public class ChunkContext
{
    public Document Document { get; set; } = null!;
    public int ChunkIndex { get; set; }
    public bool IsTarget { get; set; }
    public int RelativePosition { get; set; }
}