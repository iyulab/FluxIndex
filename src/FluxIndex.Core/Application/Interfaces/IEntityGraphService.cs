using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Domain.Entities;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Entity Graph Service interface for entity-centric indexing and retrieval.
/// Builds and queries entity graphs for GraphRAG capabilities.
/// </summary>
public interface IEntityGraphService
{
    /// <summary>
    /// Builds an entity graph from document chunks.
    /// Extracts entities and relations, creates entity-to-chunk mappings.
    /// </summary>
    /// <param name="chunks">Document chunks to process</param>
    /// <param name="options">Graph building options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Built entity graph with mappings</returns>
    Task<EntityGraphResult> BuildEntityGraphAsync(
        IEnumerable<DocumentChunk> chunks,
        EntityGraphBuildOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs entity-centric search using Personalized PageRank.
    /// Finds relevant chunks based on entity relationships.
    /// </summary>
    /// <param name="query">Search query</param>
    /// <param name="entityGraph">Entity graph to search</param>
    /// <param name="options">Search options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Ranked search results with entity context</returns>
    Task<EntitySearchResult> SearchByEntitiesAsync(
        string query,
        EntityGraphResult entityGraph,
        EntitySearchOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs multi-hop entity traversal to answer relational queries.
    /// </summary>
    /// <param name="startEntities">Starting entities for traversal</param>
    /// <param name="entityGraph">Entity graph to traverse</param>
    /// <param name="options">Traversal options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Traversal result with paths and related entities</returns>
    Task<EntityTraversalResult> TraverseEntityRelationsAsync(
        IEnumerable<string> startEntities,
        EntityGraphResult entityGraph,
        EntityTraversalOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes entity importance scores using Personalized PageRank.
    /// </summary>
    /// <param name="entityGraph">Entity graph to analyze</param>
    /// <param name="seedEntities">Optional seed entities for personalization</param>
    /// <param name="options">PPR options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Entity importance scores</returns>
    Task<IReadOnlyDictionary<string, double>> ComputeEntityImportanceAsync(
        EntityGraphResult entityGraph,
        IEnumerable<string>? seedEntities = null,
        PersonalizedPageRankOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges multiple entity graphs into a unified graph.
    /// Handles entity deduplication and relation merging.
    /// </summary>
    /// <param name="graphs">Entity graphs to merge</param>
    /// <param name="options">Merge options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Merged entity graph</returns>
    Task<EntityGraphResult> MergeEntityGraphsAsync(
        IEnumerable<EntityGraphResult> graphs,
        EntityGraphMergeOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets chunks associated with specific entities.
    /// </summary>
    /// <param name="entityIds">Entity IDs to look up</param>
    /// <param name="entityGraph">Entity graph with mappings</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Chunks associated with the entities</returns>
    Task<IReadOnlyList<EntityChunkMapping>> GetChunksForEntitiesAsync(
        IEnumerable<string> entityIds,
        EntityGraphResult entityGraph,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds entities that bridge multiple topic clusters.
    /// These are key connector entities for understanding relationships.
    /// </summary>
    /// <param name="entityGraph">Entity graph to analyze</param>
    /// <param name="options">Analysis options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of bridge entities with scores</returns>
    Task<IReadOnlyList<BridgeEntity>> FindBridgeEntitiesAsync(
        EntityGraphResult entityGraph,
        BridgeEntityOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for building entity graph
/// </summary>
public class EntityGraphBuildOptions
{
    /// <summary>
    /// Minimum entity confidence for inclusion
    /// </summary>
    public double MinEntityConfidence { get; set; } = 0.5;

    /// <summary>
    /// Minimum relation confidence for inclusion
    /// </summary>
    public double MinRelationConfidence { get; set; } = 0.4;

    /// <summary>
    /// Maximum entities per chunk
    /// </summary>
    public int MaxEntitiesPerChunk { get; set; } = 50;

    /// <summary>
    /// Whether to extract relations between entities
    /// </summary>
    public bool ExtractRelations { get; set; } = true;

    /// <summary>
    /// Whether to perform cross-chunk entity linking
    /// </summary>
    public bool LinkEntitiesAcrossChunks { get; set; } = true;

    /// <summary>
    /// Entity types to extract. Null means all types.
    /// </summary>
    public IReadOnlyList<NamedEntityType>? EntityTypes { get; set; }

    /// <summary>
    /// Whether to compute entity embeddings
    /// </summary>
    public bool ComputeEntityEmbeddings { get; set; } = false;

    /// <summary>
    /// Batch size for processing chunks
    /// </summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>
    /// Whether to persist the graph to the configured graph store (Neo4j, etc.)
    /// </summary>
    public bool PersistToGraphStore { get; set; } = true;
}

/// <summary>
/// Options for entity-based search
/// </summary>
public class EntitySearchOptions
{
    /// <summary>
    /// Maximum number of results to return
    /// </summary>
    public int TopK { get; set; } = 10;

    /// <summary>
    /// Whether to use PPR for ranking
    /// </summary>
    public bool UsePersonalizedPageRank { get; set; } = true;

    /// <summary>
    /// PPR damping factor
    /// </summary>
    public double DampingFactor { get; set; } = 0.85;

    /// <summary>
    /// Number of PPR iterations
    /// </summary>
    public int MaxIterations { get; set; } = 100;

    /// <summary>
    /// Minimum score threshold for results
    /// </summary>
    public double MinScore { get; set; } = 0.0;

    /// <summary>
    /// Whether to include entity explanations
    /// </summary>
    public bool IncludeExplanation { get; set; } = true;

    /// <summary>
    /// Entity types to prioritize in search
    /// </summary>
    public IReadOnlyList<NamedEntityType>? PriorityEntityTypes { get; set; }
}

/// <summary>
/// Options for entity traversal
/// </summary>
public class EntityTraversalOptions
{
    /// <summary>
    /// Maximum number of hops
    /// </summary>
    public int MaxHops { get; set; } = 3;

    /// <summary>
    /// Maximum entities per hop
    /// </summary>
    public int MaxEntitiesPerHop { get; set; } = 10;

    /// <summary>
    /// Relation types to follow. Null means all types.
    /// </summary>
    public IReadOnlyList<RelationType>? RelationTypes { get; set; }

    /// <summary>
    /// Whether to follow bidirectional relations
    /// </summary>
    public bool BidirectionalTraversal { get; set; } = true;

    /// <summary>
    /// Minimum relation strength to follow
    /// </summary>
    public double MinRelationStrength { get; set; } = 0.3;

    /// <summary>
    /// Whether to include chunks at each hop
    /// </summary>
    public bool IncludeChunks { get; set; } = true;
}

/// <summary>
/// Options for Personalized PageRank computation
/// </summary>
public class PersonalizedPageRankOptions
{
    /// <summary>
    /// Damping factor (typically 0.85)
    /// </summary>
    public double DampingFactor { get; set; } = 0.85;

    /// <summary>
    /// Maximum iterations
    /// </summary>
    public int MaxIterations { get; set; } = 100;

    /// <summary>
    /// Convergence threshold
    /// </summary>
    public double ConvergenceThreshold { get; set; } = 1e-6;

    /// <summary>
    /// Whether to use edge weights
    /// </summary>
    public bool UseEdgeWeights { get; set; } = true;

    /// <summary>
    /// Personalization weight for seed entities (0-1)
    /// </summary>
    public double PersonalizationWeight { get; set; } = 0.5;
}

/// <summary>
/// Options for merging entity graphs
/// </summary>
public class EntityGraphMergeOptions
{
    /// <summary>
    /// Similarity threshold for entity matching
    /// </summary>
    public double EntitySimilarityThreshold { get; set; } = 0.8;

    /// <summary>
    /// Whether to use fuzzy matching
    /// </summary>
    public bool UseFuzzyMatching { get; set; } = true;

    /// <summary>
    /// Whether to merge relation evidence
    /// </summary>
    public bool MergeRelationEvidence { get; set; } = true;

    /// <summary>
    /// Whether to use embeddings for entity matching
    /// </summary>
    public bool UseEmbeddingsForMatching { get; set; } = false;
}

/// <summary>
/// Options for finding bridge entities
/// </summary>
public class BridgeEntityOptions
{
    /// <summary>
    /// Minimum number of connections to be considered a bridge
    /// </summary>
    public int MinConnections { get; set; } = 3;

    /// <summary>
    /// Top N bridge entities to return
    /// </summary>
    public int TopN { get; set; } = 10;

    /// <summary>
    /// Whether to compute betweenness centrality
    /// </summary>
    public bool ComputeBetweennessCentrality { get; set; } = true;
}

/// <summary>
/// Result of entity graph building
/// </summary>
public class EntityGraphResult
{
    /// <summary>
    /// Unique identifier for this graph
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// All entities in the graph
    /// </summary>
    public IReadOnlyList<EntityNode> Entities { get; init; } = Array.Empty<EntityNode>();

    /// <summary>
    /// All relations in the graph
    /// </summary>
    public IReadOnlyList<EntityEdge> Relations { get; init; } = Array.Empty<EntityEdge>();

    /// <summary>
    /// Entity to chunk mappings
    /// </summary>
    public IReadOnlyList<EntityChunkMapping> ChunkMappings { get; init; } = Array.Empty<EntityChunkMapping>();

    /// <summary>
    /// Source chunk IDs that were processed
    /// </summary>
    public IReadOnlyList<string> SourceChunkIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Graph statistics
    /// </summary>
    public EntityGraphStats Stats { get; init; } = new();

    /// <summary>
    /// Creation timestamp
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Entity node in the graph
/// </summary>
public class EntityNode
{
    /// <summary>
    /// Unique identifier
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Canonical entity name
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Normalized name for matching
    /// </summary>
    public string NormalizedName { get; init; } = string.Empty;

    /// <summary>
    /// Entity type
    /// </summary>
    public NamedEntityType Type { get; init; }

    /// <summary>
    /// All surface forms (aliases)
    /// </summary>
    public IReadOnlyList<string> SurfaceForms { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Aggregate confidence score
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Importance score (computed via PPR)
    /// </summary>
    public double ImportanceScore { get; set; }

    /// <summary>
    /// Number of mentions across chunks
    /// </summary>
    public int MentionCount { get; init; }

    /// <summary>
    /// Entity embedding (optional)
    /// </summary>
    public float[]? Embedding { get; init; }

    /// <summary>
    /// External knowledge base links
    /// </summary>
    public IReadOnlyDictionary<string, string> ExternalLinks { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Additional properties
    /// </summary>
    public IReadOnlyDictionary<string, object> Properties { get; init; } = new Dictionary<string, object>();
}

/// <summary>
/// Entity edge (relation) in the graph
/// </summary>
public class EntityEdge
{
    /// <summary>
    /// Unique identifier
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Source entity ID
    /// </summary>
    public string SourceEntityId { get; init; } = string.Empty;

    /// <summary>
    /// Target entity ID
    /// </summary>
    public string TargetEntityId { get; init; } = string.Empty;

    /// <summary>
    /// Relation type
    /// </summary>
    public RelationType RelationType { get; init; }

    /// <summary>
    /// Relation label
    /// </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// Confidence score
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Relation weight/strength
    /// </summary>
    public double Weight { get; init; } = 1.0;

    /// <summary>
    /// Whether the relation is directional
    /// </summary>
    public bool IsDirectional { get; init; } = true;

    /// <summary>
    /// Evidence chunks supporting this relation
    /// </summary>
    public IReadOnlyList<string> EvidenceChunkIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Evidence text excerpts
    /// </summary>
    public IReadOnlyList<string> EvidenceTexts { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Additional properties
    /// </summary>
    public IReadOnlyDictionary<string, object> Properties { get; init; } = new Dictionary<string, object>();
}

/// <summary>
/// Mapping between entities and chunks
/// </summary>
public class EntityChunkMapping
{
    /// <summary>
    /// Entity ID
    /// </summary>
    public string EntityId { get; init; } = string.Empty;

    /// <summary>
    /// Chunk ID
    /// </summary>
    public string ChunkId { get; init; } = string.Empty;

    /// <summary>
    /// Mention count in this chunk
    /// </summary>
    public int MentionCount { get; init; }

    /// <summary>
    /// Positions in the chunk
    /// </summary>
    public IReadOnlyList<(int Start, int End)> Positions { get; init; } = Array.Empty<(int, int)>();

    /// <summary>
    /// Relevance score for this entity-chunk pair
    /// </summary>
    public double RelevanceScore { get; init; }
}

/// <summary>
/// Entity graph statistics
/// </summary>
public class EntityGraphStats
{
    /// <summary>
    /// Total number of entities
    /// </summary>
    public int TotalEntities { get; init; }

    /// <summary>
    /// Total number of relations
    /// </summary>
    public int TotalRelations { get; init; }

    /// <summary>
    /// Entities by type count
    /// </summary>
    public IReadOnlyDictionary<NamedEntityType, int> EntitiesByType { get; init; } = new Dictionary<NamedEntityType, int>();

    /// <summary>
    /// Relations by type count
    /// </summary>
    public IReadOnlyDictionary<RelationType, int> RelationsByType { get; init; } = new Dictionary<RelationType, int>();

    /// <summary>
    /// Number of connected components
    /// </summary>
    public int ConnectedComponents { get; init; }

    /// <summary>
    /// Graph density (edges / possible edges)
    /// </summary>
    public double Density { get; init; }

    /// <summary>
    /// Average degree per node
    /// </summary>
    public double AverageDegree { get; init; }

    /// <summary>
    /// Processing time in milliseconds
    /// </summary>
    public double ProcessingTimeMs { get; init; }
}

/// <summary>
/// Result of entity-based search
/// </summary>
public class EntitySearchResult
{
    /// <summary>
    /// Original query
    /// </summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// Query entities identified
    /// </summary>
    public IReadOnlyList<EntityNode> QueryEntities { get; init; } = Array.Empty<EntityNode>();

    /// <summary>
    /// Ranked chunk results
    /// </summary>
    public IReadOnlyList<EntitySearchHit> Hits { get; init; } = Array.Empty<EntitySearchHit>();

    /// <summary>
    /// Related entities discovered
    /// </summary>
    public IReadOnlyList<EntityNode> RelatedEntities { get; init; } = Array.Empty<EntityNode>();

    /// <summary>
    /// Search statistics
    /// </summary>
    public EntitySearchStats Stats { get; init; } = new();
}

/// <summary>
/// Single hit from entity search
/// </summary>
public class EntitySearchHit
{
    /// <summary>
    /// Chunk ID
    /// </summary>
    public string ChunkId { get; init; } = string.Empty;

    /// <summary>
    /// Chunk content
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Combined relevance score
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// PPR-based score
    /// </summary>
    public double PprScore { get; init; }

    /// <summary>
    /// Entity match score
    /// </summary>
    public double EntityMatchScore { get; init; }

    /// <summary>
    /// Entities in this chunk
    /// </summary>
    public IReadOnlyList<EntityNode> Entities { get; init; } = Array.Empty<EntityNode>();

    /// <summary>
    /// Explanation of why this chunk is relevant
    /// </summary>
    public string? Explanation { get; init; }
}

/// <summary>
/// Entity search statistics
/// </summary>
public class EntitySearchStats
{
    /// <summary>
    /// Total entities considered
    /// </summary>
    public int EntitiesConsidered { get; init; }

    /// <summary>
    /// Chunks evaluated
    /// </summary>
    public int ChunksEvaluated { get; init; }

    /// <summary>
    /// PPR iterations performed
    /// </summary>
    public int PprIterations { get; init; }

    /// <summary>
    /// Search time in milliseconds
    /// </summary>
    public double SearchTimeMs { get; init; }
}

/// <summary>
/// Result of entity traversal
/// </summary>
public class EntityTraversalResult
{
    /// <summary>
    /// Starting entities
    /// </summary>
    public IReadOnlyList<EntityNode> StartEntities { get; init; } = Array.Empty<EntityNode>();

    /// <summary>
    /// Entities by hop level
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyList<EntityNode>> EntitiesByHop { get; init; } = new Dictionary<int, IReadOnlyList<EntityNode>>();

    /// <summary>
    /// Traversal paths discovered
    /// </summary>
    public IReadOnlyList<EntityPath> Paths { get; init; } = Array.Empty<EntityPath>();

    /// <summary>
    /// Relevant chunks from traversal
    /// </summary>
    public IReadOnlyList<EntitySearchHit> RelevantChunks { get; init; } = Array.Empty<EntitySearchHit>();

    /// <summary>
    /// Traversal statistics
    /// </summary>
    public EntityTraversalStats Stats { get; init; } = new();
}

/// <summary>
/// Path through entity graph
/// </summary>
public class EntityPath
{
    /// <summary>
    /// Entities in path order
    /// </summary>
    public IReadOnlyList<EntityNode> Entities { get; init; } = Array.Empty<EntityNode>();

    /// <summary>
    /// Relations connecting entities
    /// </summary>
    public IReadOnlyList<EntityEdge> Relations { get; init; } = Array.Empty<EntityEdge>();

    /// <summary>
    /// Path length
    /// </summary>
    public int Length => Entities.Count - 1;

    /// <summary>
    /// Path strength (product of edge weights)
    /// </summary>
    public double Strength { get; init; }
}

/// <summary>
/// Entity traversal statistics
/// </summary>
public class EntityTraversalStats
{
    /// <summary>
    /// Total entities visited
    /// </summary>
    public int EntitiesVisited { get; init; }

    /// <summary>
    /// Maximum hop reached
    /// </summary>
    public int MaxHopReached { get; init; }

    /// <summary>
    /// Paths discovered
    /// </summary>
    public int PathsDiscovered { get; init; }

    /// <summary>
    /// Traversal time in milliseconds
    /// </summary>
    public double TraversalTimeMs { get; init; }
}

/// <summary>
/// Bridge entity that connects different clusters
/// </summary>
public class BridgeEntity
{
    /// <summary>
    /// Entity node
    /// </summary>
    public EntityNode Entity { get; init; } = null!;

    /// <summary>
    /// Bridge score (how important as a connector)
    /// </summary>
    public double BridgeScore { get; init; }

    /// <summary>
    /// Betweenness centrality
    /// </summary>
    public double BetweennessCentrality { get; init; }

    /// <summary>
    /// Number of distinct clusters connected
    /// </summary>
    public int ClustersConnected { get; init; }

    /// <summary>
    /// Connected entity IDs
    /// </summary>
    public IReadOnlyList<string> ConnectedEntityIds { get; init; } = Array.Empty<string>();
}
