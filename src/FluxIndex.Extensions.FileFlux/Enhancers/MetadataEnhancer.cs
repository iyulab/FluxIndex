using FluxIndex.Application.Interfaces;
using FluxIndex.Extensions.FileFlux.Interfaces;
using FluxIndex.Extensions.FileFlux.Strategies;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Extensions.FileFlux.Enhancers;

/// <summary>
/// Enhances metadata for optimized storage and retrieval
/// </summary>
public class MetadataEnhancer
{
    private readonly ILogger<MetadataEnhancer> _logger;
    private readonly IEmbeddingService? _embeddingService;

    public MetadataEnhancer(
        ILogger<MetadataEnhancer> logger,
        IEmbeddingService? embeddingService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _embeddingService = embeddingService;
    }

    /// <summary>
    /// Enhance metadata with additional information for optimization
    /// </summary>
    public async Task<Dictionary<string, string>> EnhanceAsync(
        Dictionary<string, string> originalMetadata,
        string content,
        ChunkingStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        var enhanced = new Dictionary<string, string>(originalMetadata);

        try
        {
            // Add content analysis
            AddContentAnalysis(enhanced, content);

            // Add strategy-specific metadata
            AddStrategyMetadata(enhanced, strategy);

            // Add quality metrics
            await AddQualityMetricsAsync(enhanced, content, strategy, cancellationToken);

            // Add search optimization hints
            AddSearchHints(enhanced, content, strategy);

            // Add indexing optimization hints
            AddIndexingHints(enhanced, content, strategy);

            _logger.LogDebug("Enhanced metadata with {Count} additional fields", 
                enhanced.Count - originalMetadata.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error enhancing metadata");
        }

        return enhanced;
    }

    private void AddContentAnalysis(Dictionary<string, string> metadata, string content)
    {
        // Basic content analysis
        metadata["content_length"] = content.Length.ToString();
        metadata["estimated_tokens"] = EstimateTokens(content).ToString();
        metadata["word_count"] = CountWords(content).ToString();
        metadata["sentence_count"] = CountSentences(content).ToString();
        metadata["avg_sentence_length"] = CalculateAverageSentenceLength(content).ToString("F1");
        
        // Language detection hint
        metadata["has_non_ascii"] = ContainsNonAscii(content).ToString().ToLower();
        
        // Structure detection
        metadata["has_code_blocks"] = ContainsCodeBlocks(content).ToString().ToLower();
        metadata["has_lists"] = ContainsLists(content).ToString().ToLower();
        metadata["has_tables"] = ContainsTables(content).ToString().ToLower();
        metadata["has_urls"] = ContainsUrls(content).ToString().ToLower();
    }

    private void AddStrategyMetadata(Dictionary<string, string> metadata, ChunkingStrategy strategy)
    {
        metadata["chunking_strategy"] = strategy.ToString();
        metadata["strategy_confidence"] = GetStrategyConfidence(strategy).ToString("F2");
        
        // Strategy-specific flags
        switch (strategy)
        {
            case ChunkingStrategy.Intelligent:
                metadata["requires_semantic_search"] = "true";
                metadata["requires_reranking"] = "true";
                break;
            case ChunkingStrategy.Smart:
                metadata["requires_hybrid_search"] = "true";
                metadata["requires_reranking"] = "true";
                break;
            case ChunkingStrategy.Semantic:
                metadata["requires_semantic_search"] = "true";
                metadata["use_overlap_expansion"] = "true";
                break;
            case ChunkingStrategy.FixedSize:
                metadata["requires_keyword_search"] = "true";
                metadata["requires_reranking"] = "false";
                break;
            case ChunkingStrategy.Paragraph:
                metadata["preserve_structure"] = "true";
                metadata["requires_hybrid_search"] = "true";
                break;
        }
    }

    private async Task AddQualityMetricsAsync(
        Dictionary<string, string> metadata,
        string content,
        ChunkingStrategy strategy,
        CancellationToken cancellationToken)
    {
        // Calculate quality metrics
        var quality = CalculateQualityMetrics(content, strategy);
        
        metadata["quality_overall"] = quality.OverallScore.ToString("F2");
        metadata["quality_coherence"] = quality.Coherence.ToString("F2");
        metadata["quality_completeness"] = quality.Completeness.ToString("F2");
        metadata["quality_density"] = quality.InformationDensity.ToString("F2");
        metadata["quality_readability"] = quality.Readability.ToString("F2");

        // Add embedding dimension hint if service available
        if (_embeddingService != null)
        {
            try
            {
                var optimalDim = await GetOptimalEmbeddingDimensionAsync(
                    content.Length, 
                    cancellationToken);
                metadata["optimal_embedding_dim"] = optimalDim.ToString();
            }
            catch
            {
                // Ignore embedding service errors
            }
        }
    }

    private void AddSearchHints(
        Dictionary<string, string> metadata,
        string content,
        ChunkingStrategy strategy)
    {
        // Search type hints
        metadata["search_hint"] = GenerateSearchHint(strategy);
        metadata["preferred_search_type"] = GetPreferredSearchType(strategy, content);
        
        // Reranker selection
        metadata["preferred_reranker"] = SelectOptimalReranker(strategy, content);
        
        // Search parameters
        metadata["suggested_top_k"] = GetSuggestedTopK(strategy).ToString();
        metadata["min_score_threshold"] = GetMinScoreThreshold(strategy).ToString("F2");
        
        // Expansion hints
        metadata["expand_with_overlap"] = ShouldExpandWithOverlap(strategy).ToString().ToLower();
        metadata["context_window_size"] = GetContextWindowSize(strategy).ToString();
    }

    private void AddIndexingHints(
        Dictionary<string, string> metadata,
        string content,
        ChunkingStrategy strategy)
    {
        // Indexing strategy
        metadata["indexing_strategy"] = DetermineIndexingStrategy(strategy, content).ToString();
        
        // Storage optimization
        metadata["compression_suitable"] = IsCompressionSuitable(content).ToString().ToLower();
        metadata["cache_priority"] = GetCachePriority(strategy).ToString();
        
        // Vector index hints
        metadata["hnsw_m_param"] = GetOptimalHnswM(strategy).ToString();
        metadata["hnsw_ef_construction"] = GetOptimalHnswEfConstruction(strategy).ToString();
    }

    private int EstimateTokens(string content)
    {
        // Rough estimation: 1 token ≈ 4 characters for English
        // For non-ASCII (Korean, etc): 1 token ≈ 2 characters
        bool hasNonAscii = ContainsNonAscii(content);
        return hasNonAscii ? content.Length / 2 : content.Length / 4;
    }

    private int CountWords(string content)
    {
        return content.Split(new[] { ' ', '\t', '\n', '\r' }, 
            StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private int CountSentences(string content)
    {
        return content.Split(new[] { '.', '!', '?' }, 
            StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private double CalculateAverageSentenceLength(string content)
    {
        var sentences = CountSentences(content);
        var words = CountWords(content);
        return sentences > 0 ? (double)words / sentences : 0;
    }

    private bool ContainsNonAscii(string content)
    {
        return content.Any(c => c > 127);
    }

    private bool ContainsCodeBlocks(string content)
    {
        return content.Contains("```") || content.Contains("    ") || content.Contains("\t");
    }

    private bool ContainsLists(string content)
    {
        return content.Contains("\n- ") || content.Contains("\n* ") || 
               content.Contains("\n1. ") || content.Contains("\n• ");
    }

    private bool ContainsTables(string content)
    {
        return content.Contains(" | ") && content.Contains("\n|");
    }

    private bool ContainsUrls(string content)
    {
        return content.Contains("http://") || content.Contains("https://") || 
               content.Contains("www.");
    }

    private double GetStrategyConfidence(ChunkingStrategy strategy)
    {
        return strategy switch
        {
            ChunkingStrategy.Intelligent => 0.95,
            ChunkingStrategy.Smart => 0.90,
            ChunkingStrategy.Semantic => 0.85,
            ChunkingStrategy.Paragraph => 0.80,
            ChunkingStrategy.FixedSize => 0.75,
            ChunkingStrategy.MemoryOptimized => 0.85,
            _ => 0.70
        };
    }

    private QualityMetrics CalculateQualityMetrics(string content, ChunkingStrategy strategy)
    {
        var metrics = new QualityMetrics();
        
        // Basic metrics
        var wordCount = CountWords(content);
        var sentenceCount = CountSentences(content);
        var avgSentenceLength = CalculateAverageSentenceLength(content);
        
        // Coherence: based on sentence structure
        metrics.Coherence = Math.Min(1.0, sentenceCount > 0 ? 0.5 + (0.5 / sentenceCount) : 0.5);
        
        // Completeness: based on content length and structure
        metrics.Completeness = CalculateCompleteness(content, wordCount, sentenceCount);
        
        // Information density: words per character ratio
        metrics.InformationDensity = Math.Min(1.0, wordCount / (double)Math.Max(1, content.Length) * 10);
        
        // Readability: based on average sentence length
        metrics.Readability = CalculateReadability(avgSentenceLength);
        
        // Overall score
        metrics.OverallScore = (metrics.Coherence + metrics.Completeness + 
                                metrics.InformationDensity + metrics.Readability) / 4;
        
        // Adjust for strategy
        if (strategy == ChunkingStrategy.Intelligent || strategy == ChunkingStrategy.Smart)
        {
            metrics.OverallScore = Math.Min(1.0, metrics.OverallScore * 1.1);
        }
        
        return metrics;
    }

    private double CalculateCompleteness(string content, int wordCount, int sentenceCount)
    {
        // Check for incomplete sentences
        bool endsWithPunctuation = content.TrimEnd().LastOrDefault() is '.' or '!' or '?';
        double punctuationScore = endsWithPunctuation ? 1.0 : 0.7;
        
        // Check for minimum content
        double contentScore = Math.Min(1.0, wordCount / 50.0);
        
        // Check for sentence completion
        double sentenceScore = sentenceCount > 0 ? 0.8 : 0.5;
        
        return (punctuationScore + contentScore + sentenceScore) / 3;
    }

    private double CalculateReadability(double avgSentenceLength)
    {
        // Optimal sentence length is 15-20 words
        if (avgSentenceLength >= 15 && avgSentenceLength <= 20)
            return 1.0;
        if (avgSentenceLength >= 10 && avgSentenceLength <= 25)
            return 0.9;
        if (avgSentenceLength >= 5 && avgSentenceLength <= 30)
            return 0.8;
        return 0.7;
    }

    private string GenerateSearchHint(ChunkingStrategy strategy)
    {
        return strategy switch
        {
            ChunkingStrategy.Intelligent => "semantic_priority",
            ChunkingStrategy.Smart => "hybrid_balanced",
            ChunkingStrategy.Semantic => "semantic_overlap",
            ChunkingStrategy.FixedSize => "keyword_focus",
            ChunkingStrategy.Paragraph => "structure_aware",
            ChunkingStrategy.MemoryOptimized => "efficient_retrieval",
            _ => "auto_detect"
        };
    }

    private string GetPreferredSearchType(ChunkingStrategy strategy, string content)
    {
        if (ContainsNonAscii(content))
            return "multilingual_hybrid";
        
        return strategy switch
        {
            ChunkingStrategy.Intelligent => "semantic",
            ChunkingStrategy.Smart => "hybrid",
            ChunkingStrategy.Semantic => "semantic",
            ChunkingStrategy.FixedSize => "keyword",
            ChunkingStrategy.Paragraph => "hybrid",
            _ => "hybrid"
        };
    }

    private string SelectOptimalReranker(ChunkingStrategy strategy, string content)
    {
        if (ContainsNonAscii(content))
            return "CohereReranker";
        
        return strategy switch
        {
            ChunkingStrategy.Intelligent => "Local",
            ChunkingStrategy.Smart => "CompositeReranker",
            ChunkingStrategy.Semantic => "Local",
            ChunkingStrategy.FixedSize => "LocalReranker",
            ChunkingStrategy.Paragraph => "LocalReranker",
            _ => "CompositeReranker"
        };
    }

    private int GetSuggestedTopK(ChunkingStrategy strategy)
    {
        return strategy switch
        {
            ChunkingStrategy.Intelligent => 20,
            ChunkingStrategy.Smart => 15,
            ChunkingStrategy.Semantic => 20,
            ChunkingStrategy.FixedSize => 30,
            ChunkingStrategy.Paragraph => 15,
            ChunkingStrategy.MemoryOptimized => 10,
            _ => 20
        };
    }

    private double GetMinScoreThreshold(ChunkingStrategy strategy)
    {
        return strategy switch
        {
            ChunkingStrategy.Intelligent => 0.7,
            ChunkingStrategy.Smart => 0.6,
            ChunkingStrategy.Semantic => 0.65,
            ChunkingStrategy.FixedSize => 0.4,
            ChunkingStrategy.Paragraph => 0.5,
            ChunkingStrategy.MemoryOptimized => 0.6,
            _ => 0.5
        };
    }

    private bool ShouldExpandWithOverlap(ChunkingStrategy strategy)
    {
        return strategy is ChunkingStrategy.Semantic or 
               ChunkingStrategy.Intelligent or 
               ChunkingStrategy.Smart;
    }

    private int GetContextWindowSize(ChunkingStrategy strategy)
    {
        return strategy switch
        {
            ChunkingStrategy.Semantic => 2,
            ChunkingStrategy.Intelligent => 1,
            ChunkingStrategy.Smart => 1,
            _ => 0
        };
    }

    private IndexingStrategy DetermineIndexingStrategy(ChunkingStrategy strategy, string content)
    {
        if (strategy == ChunkingStrategy.Intelligent && !ContainsNonAscii(content))
            return IndexingStrategy.HighQuality;
        
        if (strategy == ChunkingStrategy.Semantic)
            return IndexingStrategy.Semantic;
        
        if (strategy == ChunkingStrategy.FixedSize)
            return IndexingStrategy.Keyword;
        
        if (strategy == ChunkingStrategy.MemoryOptimized)
            return IndexingStrategy.Compressed;
        
        return IndexingStrategy.Hybrid;
    }

    private bool IsCompressionSuitable(string content)
    {
        // Don't compress if content has code or tables
        return !ContainsCodeBlocks(content) && !ContainsTables(content) && content.Length > 1000;
    }

    private int GetCachePriority(ChunkingStrategy strategy)
    {
        return strategy switch
        {
            ChunkingStrategy.Intelligent => 3,
            ChunkingStrategy.Smart => 2,
            ChunkingStrategy.Semantic => 2,
            _ => 1
        };
    }

    private int GetOptimalHnswM(ChunkingStrategy strategy)
    {
        return strategy switch
        {
            ChunkingStrategy.Intelligent => 48,
            ChunkingStrategy.Smart => 32,
            ChunkingStrategy.Semantic => 32,
            ChunkingStrategy.MemoryOptimized => 16,
            _ => 24
        };
    }

    private int GetOptimalHnswEfConstruction(ChunkingStrategy strategy)
    {
        return strategy switch
        {
            ChunkingStrategy.Intelligent => 400,
            ChunkingStrategy.Smart => 200,
            ChunkingStrategy.Semantic => 200,
            ChunkingStrategy.MemoryOptimized => 100,
            _ => 150
        };
    }

    private async Task<int> GetOptimalEmbeddingDimensionAsync(
        int contentLength,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Placeholder for async operations
        
        // Based on content length, suggest embedding dimension
        if (contentLength < 500)
            return 384;  // Small dimension for short content
        if (contentLength < 2000)
            return 768;  // Medium dimension
        return 1536;     // Full dimension for large content
    }

    private class QualityMetrics
    {
        public double OverallScore { get; set; }
        public double Coherence { get; set; }
        public double Completeness { get; set; }
        public double InformationDensity { get; set; }
        public double Readability { get; set; }
    }
}