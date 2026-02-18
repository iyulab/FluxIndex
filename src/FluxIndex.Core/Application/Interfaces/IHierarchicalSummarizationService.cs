using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.ValueObjects;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Hierarchical summarization service for GraphRAG global search.
/// Implements map-reduce summarization at community level with caching
/// and supports answer synthesis from multiple communities.
/// </summary>
public interface IHierarchicalSummarizationService
{
    /// <summary>
    /// Generates hierarchical summaries for a community hierarchy using map-reduce.
    /// </summary>
    /// <param name="hierarchy">Community hierarchy from Leiden detection</param>
    /// <param name="chunks">Original chunks for content access</param>
    /// <param name="options">Summarization options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Summarization result with cached summaries at each level</returns>
    Task<HierarchicalSummaryResult> GenerateHierarchicalSummariesAsync(
        CommunityHierarchy hierarchy,
        IEnumerable<DocumentChunk> chunks,
        HierarchicalSummarizationOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs global search using community summaries.
    /// Finds relevant communities and synthesizes an answer.
    /// </summary>
    /// <param name="query">User query</param>
    /// <param name="summaryResult">Precomputed hierarchical summaries</param>
    /// <param name="options">Global search options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Global search result with synthesized answer</returns>
    Task<GlobalSearchResult> GlobalSearchAsync(
        string query,
        HierarchicalSummaryResult summaryResult,
        GlobalSearchOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Incrementally updates summaries when new chunks are added.
    /// Only regenerates summaries for affected communities.
    /// </summary>
    /// <param name="existingResult">Existing summary result</param>
    /// <param name="newChunks">Newly added chunks</param>
    /// <param name="affectedCommunityIds">IDs of communities that need updates</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated summary result</returns>
    Task<HierarchicalSummaryResult> UpdateSummariesAsync(
        HierarchicalSummaryResult existingResult,
        IEnumerable<DocumentChunk> newChunks,
        IEnumerable<string> affectedCommunityIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Synthesizes an answer from multiple community summaries.
    /// </summary>
    /// <param name="query">User query</param>
    /// <param name="relevantSummaries">Summaries relevant to the query</param>
    /// <param name="options">Synthesis options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Synthesized answer</returns>
    Task<SynthesizedAnswer> SynthesizeAnswerAsync(
        string query,
        IEnumerable<CommunitySummary> relevantSummaries,
        AnswerSynthesisOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates cached summaries for specified communities.
    /// </summary>
    /// <param name="communityIds">Community IDs to invalidate</param>
    /// <param name="cascadeToParents">Whether to invalidate parent communities</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task InvalidateSummariesAsync(
        IEnumerable<string> communityIds,
        bool cascadeToParents = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets cached summary for a specific community if available.
    /// </summary>
    /// <param name="communityId">Community ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Cached summary or null if not found</returns>
    Task<CommunitySummary?> GetCachedSummaryAsync(
        string communityId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for hierarchical summarization
/// </summary>
public class HierarchicalSummarizationOptions
{
    /// <summary>
    /// Maximum tokens per chunk for map phase.
    /// Chunks exceeding this are truncated.
    /// Default: 1000
    /// </summary>
    public int MaxTokensPerChunk { get; set; } = 1000;

    /// <summary>
    /// Maximum chunks to include in map phase per community.
    /// If exceeded, representative chunks are selected.
    /// Default: 20
    /// </summary>
    public int MaxChunksPerCommunity { get; set; } = 20;

    /// <summary>
    /// Maximum tokens for reduce phase output.
    /// Default: 500
    /// </summary>
    public int MaxSummaryTokens { get; set; } = 500;

    /// <summary>
    /// Whether to generate summaries in parallel.
    /// Default: true
    /// </summary>
    public bool ParallelGeneration { get; set; } = true;

    /// <summary>
    /// Maximum degree of parallelism.
    /// Default: 4
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = 4;

    /// <summary>
    /// Temperature for LLM generation.
    /// Default: 0.3
    /// </summary>
    public float Temperature { get; set; } = 0.3f;

    /// <summary>
    /// Whether to enable summary caching.
    /// Default: true
    /// </summary>
    public bool EnableCaching { get; set; } = true;

    /// <summary>
    /// Cache expiration time.
    /// Default: 1 hour
    /// </summary>
    public TimeSpan CacheExpiration { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Levels to summarize (null = all levels).
    /// </summary>
    public int[]? LevelsToSummarize { get; set; }

    /// <summary>
    /// Whether to extract entities during summarization.
    /// Default: true
    /// </summary>
    public bool ExtractEntities { get; set; } = true;

    /// <summary>
    /// Whether to extract claims/facts during summarization.
    /// Default: true
    /// </summary>
    public bool ExtractClaims { get; set; } = true;

    /// <summary>
    /// Custom prompt template for map phase.
    /// Placeholders: {content}, {keywords}, {size}
    /// </summary>
    public string? MapPromptTemplate { get; set; }

    /// <summary>
    /// Custom prompt template for reduce phase.
    /// Placeholders: {summaries}, {level}, {count}
    /// </summary>
    public string? ReducePromptTemplate { get; set; }
}

/// <summary>
/// Options for global search
/// </summary>
public class GlobalSearchOptions
{
    /// <summary>
    /// Level to search at (0 = finest, higher = coarser).
    /// Default: 1 (intermediate level)
    /// </summary>
    public int SearchLevel { get; set; } = 1;

    /// <summary>
    /// Maximum communities to consider.
    /// Default: 5
    /// </summary>
    public int MaxCommunities { get; set; } = 5;

    /// <summary>
    /// Minimum similarity threshold for community matching.
    /// Default: 0.3
    /// </summary>
    public double MinSimilarityThreshold { get; set; } = 0.3;

    /// <summary>
    /// Whether to include child communities from matched parents.
    /// Default: true
    /// </summary>
    public bool IncludeChildCommunities { get; set; } = true;

    /// <summary>
    /// Maximum tokens for synthesized answer.
    /// Default: 1000
    /// </summary>
    public int MaxAnswerTokens { get; set; } = 1000;

    /// <summary>
    /// Whether to include sources in the answer.
    /// Default: true
    /// </summary>
    public bool IncludeSources { get; set; } = true;

    /// <summary>
    /// Whether to score answer confidence.
    /// Default: true
    /// </summary>
    public bool ScoreConfidence { get; set; } = true;

    /// <summary>
    /// Temperature for answer synthesis.
    /// Default: 0.3
    /// </summary>
    public float Temperature { get; set; } = 0.3f;

    /// <summary>
    /// Whether to use query expansion.
    /// Default: false
    /// </summary>
    public bool UseQueryExpansion { get; set; }
}

/// <summary>
/// Options for answer synthesis
/// </summary>
public class AnswerSynthesisOptions
{
    /// <summary>
    /// Maximum tokens for the answer.
    /// Default: 1000
    /// </summary>
    public int MaxTokens { get; set; } = 1000;

    /// <summary>
    /// Temperature for generation.
    /// Default: 0.3
    /// </summary>
    public float Temperature { get; set; } = 0.3f;

    /// <summary>
    /// Whether to include citations.
    /// Default: true
    /// </summary>
    public bool IncludeCitations { get; set; } = true;

    /// <summary>
    /// Whether to structure the answer with sections.
    /// Default: false
    /// </summary>
    public bool StructuredAnswer { get; set; }

    /// <summary>
    /// Custom synthesis prompt template.
    /// Placeholders: {query}, {summaries}, {count}
    /// </summary>
    public string? PromptTemplate { get; set; }

    /// <summary>
    /// Minimum confidence to include a summary in synthesis.
    /// Default: 0.5
    /// </summary>
    public double MinSummaryConfidence { get; set; } = 0.5;
}

/// <summary>
/// Result of hierarchical summarization
/// </summary>
public class HierarchicalSummaryResult
{
    /// <summary>
    /// Unique identifier
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Source hierarchy ID
    /// </summary>
    public string HierarchyId { get; init; } = string.Empty;

    /// <summary>
    /// Summaries organized by level
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyList<CommunitySummary>> SummariesByLevel { get; init; }
        = new Dictionary<int, IReadOnlyList<CommunitySummary>>();

    /// <summary>
    /// Total communities summarized
    /// </summary>
    public int TotalCommunitiesSummarized { get; init; }

    /// <summary>
    /// When summaries were generated
    /// </summary>
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Options used for generation
    /// </summary>
    public HierarchicalSummarizationOptions Options { get; init; } = new();

    /// <summary>
    /// Generation statistics
    /// </summary>
    public SummarizationStatistics Statistics { get; init; } = new();

    /// <summary>
    /// Community hierarchy reference
    /// </summary>
    public CommunityHierarchy? Hierarchy { get; init; }

    /// <summary>
    /// Chunk lookup for content access
    /// </summary>
    public IReadOnlyDictionary<string, DocumentChunk> ChunkLookup { get; init; }
        = new Dictionary<string, DocumentChunk>();
}

/// <summary>
/// Summary for a single community
/// </summary>
public class CommunitySummary
{
    /// <summary>
    /// Community ID
    /// </summary>
    public string CommunityId { get; init; } = string.Empty;

    /// <summary>
    /// Hierarchy level
    /// </summary>
    public int Level { get; init; }

    /// <summary>
    /// Summary text
    /// </summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// Title/topic of the community
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Key themes identified
    /// </summary>
    public IReadOnlyList<string> Themes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Extracted entities
    /// </summary>
    public IReadOnlyList<ExtractedSummaryEntity> Entities { get; init; } = Array.Empty<ExtractedSummaryEntity>();

    /// <summary>
    /// Extracted claims/facts
    /// </summary>
    public IReadOnlyList<ExtractedClaim> Claims { get; init; } = Array.Empty<ExtractedClaim>();

    /// <summary>
    /// Summary embedding for similarity search
    /// </summary>
    public EmbeddingVector? Embedding { get; init; }

    /// <summary>
    /// Confidence score (0-1)
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Number of source chunks
    /// </summary>
    public int SourceChunkCount { get; init; }

    /// <summary>
    /// IDs of source chunks
    /// </summary>
    public IReadOnlyList<string> SourceChunkIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Child summary IDs (for hierarchical navigation)
    /// </summary>
    public IReadOnlyList<string> ChildSummaryIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Parent summary ID
    /// </summary>
    public string? ParentSummaryId { get; init; }

    /// <summary>
    /// When the summary was generated
    /// </summary>
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Cache expiration time
    /// </summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>
    /// Whether the summary is from cache
    /// </summary>
    public bool IsCached { get; init; }
}

/// <summary>
/// Entity extracted from summary
/// </summary>
public class ExtractedSummaryEntity
{
    /// <summary>
    /// Entity text
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Entity type
    /// </summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Mention count in community
    /// </summary>
    public int MentionCount { get; init; }

    /// <summary>
    /// Importance score
    /// </summary>
    public double Importance { get; init; }
}

/// <summary>
/// Claim/fact extracted from summary
/// </summary>
public class ExtractedClaim
{
    /// <summary>
    /// Claim text
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Confidence in the claim
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Supporting evidence (chunk IDs)
    /// </summary>
    public IReadOnlyList<string> SupportingChunkIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Claim type (fact, opinion, definition, etc.)
    /// </summary>
    public string Type { get; init; } = "fact";
}

/// <summary>
/// Result of global search
/// </summary>
public class GlobalSearchResult
{
    /// <summary>
    /// User query
    /// </summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// Synthesized answer
    /// </summary>
    public SynthesizedAnswer Answer { get; init; } = new();

    /// <summary>
    /// Matched communities
    /// </summary>
    public IReadOnlyList<MatchedCommunity> MatchedCommunities { get; init; } = Array.Empty<MatchedCommunity>();

    /// <summary>
    /// Search level used
    /// </summary>
    public int SearchLevel { get; init; }

    /// <summary>
    /// Total communities searched
    /// </summary>
    public int TotalCommunitiesSearched { get; init; }

    /// <summary>
    /// Processing time in milliseconds
    /// </summary>
    public double ProcessingTimeMs { get; init; }

    /// <summary>
    /// Whether query expansion was used
    /// </summary>
    public bool UsedQueryExpansion { get; init; }

    /// <summary>
    /// Expanded queries if used
    /// </summary>
    public IReadOnlyList<string> ExpandedQueries { get; init; } = Array.Empty<string>();
}

/// <summary>
/// A matched community in global search
/// </summary>
public class MatchedCommunity
{
    /// <summary>
    /// Community ID
    /// </summary>
    public string CommunityId { get; init; } = string.Empty;

    /// <summary>
    /// Community summary
    /// </summary>
    public CommunitySummary Summary { get; init; } = null!;

    /// <summary>
    /// Similarity to query
    /// </summary>
    public double Similarity { get; init; }

    /// <summary>
    /// Relevance score (may include other factors)
    /// </summary>
    public double RelevanceScore { get; init; }

    /// <summary>
    /// Rank in results
    /// </summary>
    public int Rank { get; init; }
}

/// <summary>
/// Synthesized answer from communities
/// </summary>
public class SynthesizedAnswer
{
    /// <summary>
    /// Answer text
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Confidence score (0-1)
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Number of communities used
    /// </summary>
    public int SourceCommunityCount { get; init; }

    /// <summary>
    /// Citations included in answer
    /// </summary>
    public IReadOnlyList<AnswerCitation> Citations { get; init; } = Array.Empty<AnswerCitation>();

    /// <summary>
    /// Key points in the answer
    /// </summary>
    public IReadOnlyList<string> KeyPoints { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Whether the answer is complete
    /// </summary>
    public bool IsComplete { get; init; }

    /// <summary>
    /// Suggested follow-up questions
    /// </summary>
    public IReadOnlyList<string> SuggestedFollowUps { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Citation in a synthesized answer
/// </summary>
public class AnswerCitation
{
    /// <summary>
    /// Citation index (e.g., [1], [2])
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// Community ID
    /// </summary>
    public string CommunityId { get; init; } = string.Empty;

    /// <summary>
    /// Community title
    /// </summary>
    public string? CommunityTitle { get; init; }

    /// <summary>
    /// Excerpt from community summary
    /// </summary>
    public string Excerpt { get; init; } = string.Empty;

    /// <summary>
    /// Relevance to the citation point
    /// </summary>
    public double Relevance { get; init; }
}

/// <summary>
/// Statistics from summarization process
/// </summary>
public class SummarizationStatistics
{
    /// <summary>
    /// Total processing time in milliseconds
    /// </summary>
    public double TotalProcessingTimeMs { get; init; }

    /// <summary>
    /// Map phase time
    /// </summary>
    public double MapPhaseTimeMs { get; init; }

    /// <summary>
    /// Reduce phase time
    /// </summary>
    public double ReducePhaseTimeMs { get; init; }

    /// <summary>
    /// Total LLM calls made
    /// </summary>
    public int TotalLLMCalls { get; init; }

    /// <summary>
    /// Total tokens processed
    /// </summary>
    public int TotalTokensProcessed { get; init; }

    /// <summary>
    /// Cache hits
    /// </summary>
    public int CacheHits { get; init; }

    /// <summary>
    /// Cache misses
    /// </summary>
    public int CacheMisses { get; init; }

    /// <summary>
    /// Summaries by level
    /// </summary>
    public IReadOnlyDictionary<int, int> SummariesByLevel { get; init; } = new Dictionary<int, int>();

    /// <summary>
    /// Average confidence by level
    /// </summary>
    public IReadOnlyDictionary<int, double> AverageConfidenceByLevel { get; init; } = new Dictionary<int, double>();

    /// <summary>
    /// Failed summarizations
    /// </summary>
    public int FailedSummarizations { get; init; }

    /// <summary>
    /// Errors encountered
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}
