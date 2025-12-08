using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.ValueObjects;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Full GraphRAG pipeline service that orchestrates entity graph, community detection,
/// and hierarchical summarization for comprehensive retrieval-augmented generation.
/// Supports both local (entity-centric) and global (community-level) search strategies.
/// </summary>
public interface IGraphRAGService
{
    /// <summary>
    /// Builds a complete GraphRAG index from document chunks.
    /// Creates entity graph, detects communities, and generates hierarchical summaries.
    /// </summary>
    /// <param name="chunks">Document chunks to index</param>
    /// <param name="options">Build options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>GraphRAG index ready for querying</returns>
    Task<GraphRAGIndex> BuildIndexAsync(
        IEnumerable<DocumentChunk> chunks,
        GraphRAGBuildOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries the GraphRAG index using automatic scope detection.
    /// Determines whether to use local (entity) or global (community) search.
    /// </summary>
    /// <param name="query">User query</param>
    /// <param name="index">Pre-built GraphRAG index</param>
    /// <param name="options">Query options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Query result with answer and supporting evidence</returns>
    Task<GraphRAGQueryResult> QueryAsync(
        string query,
        GraphRAGIndex index,
        GraphRAGQueryOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs local search using entity-centric retrieval.
    /// Best for specific, factual queries about entities and relationships.
    /// </summary>
    /// <param name="query">User query</param>
    /// <param name="index">GraphRAG index</param>
    /// <param name="options">Local search options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Local search result</returns>
    Task<LocalSearchResult> LocalSearchAsync(
        string query,
        GraphRAGIndex index,
        LocalSearchOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs global search using community summaries.
    /// Best for broad, thematic queries requiring holistic understanding.
    /// </summary>
    /// <param name="query">User query</param>
    /// <param name="index">GraphRAG index</param>
    /// <param name="options">Global search options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Global search result</returns>
    Task<GlobalSearchResult> GlobalSearchAsync(
        string query,
        GraphRAGIndex index,
        GlobalSearchOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs hybrid search combining both local and global strategies.
    /// Fuses results from entity and community paths.
    /// </summary>
    /// <param name="query">User query</param>
    /// <param name="index">GraphRAG index</param>
    /// <param name="options">Hybrid search options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Hybrid search result</returns>
    Task<HybridGraphSearchResult> HybridSearchAsync(
        string query,
        GraphRAGIndex index,
        HybridGraphSearchOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects the optimal search scope for a query.
    /// </summary>
    /// <param name="query">User query to analyze</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Detected query scope</returns>
    Task<QueryScopeResult> DetectQueryScopeAsync(
        string query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing GraphRAG index with new chunks.
    /// </summary>
    /// <param name="index">Existing index</param>
    /// <param name="newChunks">New chunks to add</param>
    /// <param name="options">Update options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated index</returns>
    Task<GraphRAGIndex> UpdateIndexAsync(
        GraphRAGIndex index,
        IEnumerable<DocumentChunk> newChunks,
        GraphRAGUpdateOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for building GraphRAG index
/// </summary>
public class GraphRAGBuildOptions
{
    /// <summary>
    /// Entity extraction options
    /// </summary>
    public EntityExtractionOptions? EntityOptions { get; set; }

    /// <summary>
    /// Community detection options
    /// </summary>
    public LeidenOptions? CommunityOptions { get; set; }

    /// <summary>
    /// Hierarchical summarization options
    /// </summary>
    public HierarchicalSummarizationOptions? SummarizationOptions { get; set; }

    /// <summary>
    /// Entity graph build options
    /// </summary>
    public EntityGraphBuildOptions? EntityGraphOptions { get; set; }

    /// <summary>
    /// Whether to enable parallel processing
    /// </summary>
    public bool ParallelProcessing { get; set; } = true;

    /// <summary>
    /// Whether to generate embeddings for entities
    /// </summary>
    public bool GenerateEntityEmbeddings { get; set; } = true;

    /// <summary>
    /// Whether to generate embeddings for summaries
    /// </summary>
    public bool GenerateSummaryEmbeddings { get; set; } = true;

    /// <summary>
    /// Maximum chunks to process. Null = no limit.
    /// </summary>
    public int? MaxChunks { get; set; }
}

/// <summary>
/// Options for GraphRAG query
/// </summary>
public class GraphRAGQueryOptions
{
    /// <summary>
    /// Force a specific search scope. Null = auto-detect.
    /// </summary>
    public QueryScope? ForceScope { get; set; }

    /// <summary>
    /// Maximum documents to retrieve
    /// </summary>
    public int MaxResults { get; set; } = 10;

    /// <summary>
    /// Minimum confidence threshold for results
    /// </summary>
    public double MinConfidence { get; set; } = 0.5;

    /// <summary>
    /// Whether to include full context in results
    /// </summary>
    public bool IncludeContext { get; set; } = true;

    /// <summary>
    /// Whether to include entity relationships
    /// </summary>
    public bool IncludeRelationships { get; set; } = true;

    /// <summary>
    /// Whether to include community context
    /// </summary>
    public bool IncludeCommunityContext { get; set; } = true;

    /// <summary>
    /// Maximum tokens for generated answer
    /// </summary>
    public int MaxAnswerTokens { get; set; } = 1000;

    /// <summary>
    /// Temperature for answer generation
    /// </summary>
    public float Temperature { get; set; } = 0.3f;
}

/// <summary>
/// Options for local (entity-centric) search
/// </summary>
public class LocalSearchOptions
{
    /// <summary>
    /// Maximum entities to consider
    /// </summary>
    public int MaxEntities { get; set; } = 10;

    /// <summary>
    /// Maximum hops for entity traversal
    /// </summary>
    public int MaxHops { get; set; } = 2;

    /// <summary>
    /// Whether to use entity embeddings for matching
    /// </summary>
    public bool UseEntityEmbeddings { get; set; } = true;

    /// <summary>
    /// Minimum entity match score
    /// </summary>
    public double MinEntityScore { get; set; } = 0.5;

    /// <summary>
    /// Maximum documents per entity
    /// </summary>
    public int MaxDocsPerEntity { get; set; } = 5;
}

/// <summary>
/// Options for hybrid graph search
/// </summary>
public class HybridGraphSearchOptions
{
    /// <summary>
    /// Weight for local (entity) results (0-1)
    /// </summary>
    public double LocalWeight { get; set; } = 0.6;

    /// <summary>
    /// Weight for global (community) results (0-1)
    /// </summary>
    public double GlobalWeight { get; set; } = 0.4;

    /// <summary>
    /// Local search options
    /// </summary>
    public LocalSearchOptions? LocalOptions { get; set; }

    /// <summary>
    /// Global search options
    /// </summary>
    public GlobalSearchOptions? GlobalOptions { get; set; }

    /// <summary>
    /// Maximum combined results
    /// </summary>
    public int MaxResults { get; set; } = 10;

    /// <summary>
    /// Fusion strategy for combining results
    /// </summary>
    public GraphFusionStrategy FusionStrategy { get; set; } = GraphFusionStrategy.WeightedSum;
}

/// <summary>
/// Options for updating GraphRAG index
/// </summary>
public class GraphRAGUpdateOptions
{
    /// <summary>
    /// Whether to rebuild affected communities
    /// </summary>
    public bool RebuildCommunities { get; set; } = true;

    /// <summary>
    /// Whether to update summaries
    /// </summary>
    public bool UpdateSummaries { get; set; } = true;

    /// <summary>
    /// Whether to merge similar entities
    /// </summary>
    public bool MergeEntities { get; set; } = true;
}

/// <summary>
/// Query scope types
/// </summary>
public enum QueryScope
{
    /// <summary>
    /// Local scope: entity-centric, specific queries
    /// </summary>
    Local,

    /// <summary>
    /// Global scope: community-level, broad queries
    /// </summary>
    Global,

    /// <summary>
    /// Hybrid scope: combine both strategies
    /// </summary>
    Hybrid
}

/// <summary>
/// Fusion strategies for combining graph results
/// </summary>
public enum GraphFusionStrategy
{
    /// <summary>
    /// Weighted sum of scores
    /// </summary>
    WeightedSum,

    /// <summary>
    /// Reciprocal rank fusion
    /// </summary>
    ReciprocalRankFusion,

    /// <summary>
    /// Take best from each source
    /// </summary>
    Interleaved,

    /// <summary>
    /// Let LLM decide best answer
    /// </summary>
    LLMFusion
}

/// <summary>
/// Complete GraphRAG index
/// </summary>
public class GraphRAGIndex
{
    /// <summary>
    /// Unique index identifier
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Entity graph
    /// </summary>
    public EntityGraphResult EntityGraph { get; init; } = null!;

    /// <summary>
    /// Community hierarchy
    /// </summary>
    public CommunityHierarchy CommunityHierarchy { get; init; } = null!;

    /// <summary>
    /// Hierarchical summaries
    /// </summary>
    public HierarchicalSummaryResult Summaries { get; init; } = null!;

    /// <summary>
    /// Source chunks
    /// </summary>
    public IReadOnlyDictionary<string, DocumentChunk> Chunks { get; init; }
        = new Dictionary<string, DocumentChunk>();

    /// <summary>
    /// When the index was created
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// When the index was last updated
    /// </summary>
    public DateTime? UpdatedAt { get; init; }

    /// <summary>
    /// Index statistics
    /// </summary>
    public GraphRAGIndexStats Stats { get; init; } = new();

    /// <summary>
    /// Build options used
    /// </summary>
    public GraphRAGBuildOptions Options { get; init; } = new();
}

/// <summary>
/// GraphRAG index statistics
/// </summary>
public class GraphRAGIndexStats
{
    /// <summary>
    /// Total chunks indexed
    /// </summary>
    public int TotalChunks { get; init; }

    /// <summary>
    /// Total entities extracted
    /// </summary>
    public int TotalEntities { get; init; }

    /// <summary>
    /// Total entity relationships
    /// </summary>
    public int TotalRelationships { get; init; }

    /// <summary>
    /// Total communities detected
    /// </summary>
    public int TotalCommunities { get; init; }

    /// <summary>
    /// Hierarchy levels
    /// </summary>
    public int HierarchyLevels { get; init; }

    /// <summary>
    /// Total summaries generated
    /// </summary>
    public int TotalSummaries { get; init; }

    /// <summary>
    /// Build time in milliseconds
    /// </summary>
    public double BuildTimeMs { get; init; }
}

/// <summary>
/// Result of query scope detection
/// </summary>
public class QueryScopeResult
{
    /// <summary>
    /// Detected scope
    /// </summary>
    public QueryScope Scope { get; init; }

    /// <summary>
    /// Confidence in the detection
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Reasoning for the detection
    /// </summary>
    public string? Reasoning { get; init; }

    /// <summary>
    /// Detected entity mentions in query
    /// </summary>
    public IReadOnlyList<string> DetectedEntities { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Detected themes/topics in query
    /// </summary>
    public IReadOnlyList<string> DetectedThemes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Query classification indicators
    /// </summary>
    public QueryIndicators Indicators { get; init; } = new();
}

/// <summary>
/// Query classification indicators
/// </summary>
public class QueryIndicators
{
    /// <summary>
    /// Whether query is specific (vs. broad)
    /// </summary>
    public double SpecificityScore { get; init; }

    /// <summary>
    /// Whether query mentions entities
    /// </summary>
    public double EntityMentionScore { get; init; }

    /// <summary>
    /// Whether query is thematic/conceptual
    /// </summary>
    public double ThematicScore { get; init; }

    /// <summary>
    /// Whether query requires aggregation
    /// </summary>
    public double AggregationScore { get; init; }

    /// <summary>
    /// Whether query is comparative
    /// </summary>
    public double ComparativeScore { get; init; }
}

/// <summary>
/// Result of GraphRAG query
/// </summary>
public class GraphRAGQueryResult
{
    /// <summary>
    /// Original query
    /// </summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// Generated answer
    /// </summary>
    public string Answer { get; init; } = string.Empty;

    /// <summary>
    /// Confidence in the answer
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Scope used for the query
    /// </summary>
    public QueryScope UsedScope { get; init; }

    /// <summary>
    /// Scope detection result
    /// </summary>
    public QueryScopeResult? ScopeDetection { get; init; }

    /// <summary>
    /// Retrieved documents with context
    /// </summary>
    public IReadOnlyList<GraphRAGDocument> Documents { get; init; } = Array.Empty<GraphRAGDocument>();

    /// <summary>
    /// Related entities
    /// </summary>
    public IReadOnlyList<GraphRAGEntity> RelatedEntities { get; init; } = Array.Empty<GraphRAGEntity>();

    /// <summary>
    /// Related communities
    /// </summary>
    public IReadOnlyList<GraphRAGCommunity> RelatedCommunities { get; init; } = Array.Empty<GraphRAGCommunity>();

    /// <summary>
    /// Citations in the answer
    /// </summary>
    public IReadOnlyList<AnswerCitation> Citations { get; init; } = Array.Empty<AnswerCitation>();

    /// <summary>
    /// Processing statistics
    /// </summary>
    public QueryStats Stats { get; init; } = new();
}

/// <summary>
/// Document in GraphRAG result
/// </summary>
public class GraphRAGDocument
{
    /// <summary>
    /// Chunk ID
    /// </summary>
    public string ChunkId { get; init; } = string.Empty;

    /// <summary>
    /// Document ID
    /// </summary>
    public string DocumentId { get; init; } = string.Empty;

    /// <summary>
    /// Content text
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Relevance score
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// Source (entity/community/hybrid)
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// Related entity IDs
    /// </summary>
    public IReadOnlyList<string> RelatedEntityIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Community ID (if applicable)
    /// </summary>
    public string? CommunityId { get; init; }
}

/// <summary>
/// Entity in GraphRAG result
/// </summary>
public class GraphRAGEntity
{
    /// <summary>
    /// Entity ID
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Entity text/name
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Entity type
    /// </summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Relevance to query
    /// </summary>
    public double Relevance { get; init; }

    /// <summary>
    /// Related entity IDs
    /// </summary>
    public IReadOnlyList<string> RelatedEntityIds { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Community in GraphRAG result
/// </summary>
public class GraphRAGCommunity
{
    /// <summary>
    /// Community ID
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Community title
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Community summary
    /// </summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// Relevance to query
    /// </summary>
    public double Relevance { get; init; }

    /// <summary>
    /// Hierarchy level
    /// </summary>
    public int Level { get; init; }
}

/// <summary>
/// Query processing statistics
/// </summary>
public class QueryStats
{
    /// <summary>
    /// Total processing time
    /// </summary>
    public double TotalTimeMs { get; init; }

    /// <summary>
    /// Scope detection time
    /// </summary>
    public double ScopeDetectionTimeMs { get; init; }

    /// <summary>
    /// Local search time
    /// </summary>
    public double LocalSearchTimeMs { get; init; }

    /// <summary>
    /// Global search time
    /// </summary>
    public double GlobalSearchTimeMs { get; init; }

    /// <summary>
    /// Answer generation time
    /// </summary>
    public double AnswerGenerationTimeMs { get; init; }

    /// <summary>
    /// Entities matched
    /// </summary>
    public int EntitiesMatched { get; init; }

    /// <summary>
    /// Communities matched
    /// </summary>
    public int CommunitiesMatched { get; init; }

    /// <summary>
    /// Documents retrieved
    /// </summary>
    public int DocumentsRetrieved { get; init; }
}

/// <summary>
/// Result of local (entity-centric) search
/// </summary>
public class LocalSearchResult
{
    /// <summary>
    /// Query
    /// </summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// Matched entities
    /// </summary>
    public IReadOnlyList<GraphRAGEntity> MatchedEntities { get; init; } = Array.Empty<GraphRAGEntity>();

    /// <summary>
    /// Retrieved documents
    /// </summary>
    public IReadOnlyList<GraphRAGDocument> Documents { get; init; } = Array.Empty<GraphRAGDocument>();

    /// <summary>
    /// Entity relationships used
    /// </summary>
    public IReadOnlyList<EntityRelationInfo> Relationships { get; init; } = Array.Empty<EntityRelationInfo>();

    /// <summary>
    /// Synthesized answer (if LLM available)
    /// </summary>
    public string? Answer { get; init; }

    /// <summary>
    /// Confidence
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Processing time
    /// </summary>
    public double ProcessingTimeMs { get; init; }
}

/// <summary>
/// Entity relationship information
/// </summary>
public class EntityRelationInfo
{
    /// <summary>
    /// Source entity ID
    /// </summary>
    public string SourceEntityId { get; init; } = string.Empty;

    /// <summary>
    /// Target entity ID
    /// </summary>
    public string TargetEntityId { get; init; } = string.Empty;

    /// <summary>
    /// Relationship type
    /// </summary>
    public string RelationType { get; init; } = string.Empty;

    /// <summary>
    /// Relationship strength
    /// </summary>
    public double Strength { get; init; }
}

/// <summary>
/// Result of hybrid graph search
/// </summary>
public class HybridGraphSearchResult
{
    /// <summary>
    /// Query
    /// </summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// Local search result
    /// </summary>
    public LocalSearchResult LocalResult { get; init; } = null!;

    /// <summary>
    /// Global search result
    /// </summary>
    public GlobalSearchResult GlobalResult { get; init; } = null!;

    /// <summary>
    /// Fused documents
    /// </summary>
    public IReadOnlyList<GraphRAGDocument> FusedDocuments { get; init; } = Array.Empty<GraphRAGDocument>();

    /// <summary>
    /// Synthesized answer
    /// </summary>
    public string Answer { get; init; } = string.Empty;

    /// <summary>
    /// Fusion strategy used
    /// </summary>
    public GraphFusionStrategy FusionStrategy { get; init; }

    /// <summary>
    /// Confidence
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Processing time
    /// </summary>
    public double ProcessingTimeMs { get; init; }
}
