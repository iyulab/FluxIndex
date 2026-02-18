using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Advanced entity extraction service interface for GraphRAG support.
/// Extracts named entities with type classification, confidence scoring, and position tracking.
/// </summary>
public interface IAdvancedEntityExtractionService
{
    /// <summary>
    /// Extracts named entities from content with detailed metadata.
    /// </summary>
    /// <param name="content">Text content to analyze</param>
    /// <param name="options">Extraction options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of extracted entities with metadata</returns>
    Task<IReadOnlyList<ExtractedEntity>> ExtractEntitiesAsync(
        string content,
        EntityExtractionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts relations between entities in content.
    /// </summary>
    /// <param name="content">Text content to analyze</param>
    /// <param name="entities">Pre-extracted entities (optional, will extract if not provided)</param>
    /// <param name="options">Extraction options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of entity relations</returns>
    Task<IReadOnlyList<EntityRelation>> ExtractRelationsAsync(
        string content,
        IReadOnlyList<ExtractedEntity>? entities = null,
        EntityExtractionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts both entities and relations in a single pass.
    /// More efficient when both are needed.
    /// </summary>
    /// <param name="content">Text content to analyze</param>
    /// <param name="options">Extraction options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Entity graph containing entities and relations</returns>
    Task<EntityGraph> ExtractEntityGraphAsync(
        string content,
        EntityExtractionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch extraction for multiple content pieces.
    /// </summary>
    /// <param name="contents">List of content to analyze</param>
    /// <param name="options">Extraction options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of entity graphs</returns>
    Task<IReadOnlyList<EntityGraph>> ExtractBatchAsync(
        IEnumerable<string> contents,
        EntityExtractionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Links entities across multiple documents to identify same entities.
    /// </summary>
    /// <param name="entityGraphs">Entity graphs from multiple documents</param>
    /// <param name="options">Linking options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Linked entity graph with merged entities</returns>
    Task<LinkedEntityGraph> LinkEntitiesAsync(
        IEnumerable<EntityGraph> entityGraphs,
        EntityLinkingOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Entity extraction options
/// </summary>
public class EntityExtractionOptions
{
    /// <summary>
    /// Minimum confidence threshold for entity inclusion (0.0-1.0)
    /// </summary>
    public double MinConfidence { get; set; } = 0.5;

    /// <summary>
    /// Entity types to extract. If null, extracts all types.
    /// </summary>
    public IReadOnlyList<NamedEntityType>? EntityTypes { get; set; }

    /// <summary>
    /// Whether to use LLM for complex entity extraction
    /// </summary>
    public bool UseLlm { get; set; } = true;

    /// <summary>
    /// Maximum number of entities to extract
    /// </summary>
    public int MaxEntities { get; set; } = 100;

    /// <summary>
    /// Whether to extract relations between entities
    /// </summary>
    public bool ExtractRelations { get; set; } = true;

    /// <summary>
    /// Language hint for entity extraction
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Custom entity patterns (regex) for domain-specific entities
    /// </summary>
    public Dictionary<string, string>? CustomPatterns { get; set; }

    /// <summary>
    /// Whether to include entity context (surrounding text)
    /// </summary>
    public bool IncludeContext { get; set; } = true;

    /// <summary>
    /// Context window size in characters
    /// </summary>
    public int ContextWindowSize { get; set; } = 100;
}

/// <summary>
/// Entity linking options for cross-document entity resolution
/// </summary>
public class EntityLinkingOptions
{
    /// <summary>
    /// Similarity threshold for entity matching (0.0-1.0)
    /// </summary>
    public double SimilarityThreshold { get; set; } = 0.8;

    /// <summary>
    /// Whether to use fuzzy matching for entity names
    /// </summary>
    public bool UseFuzzyMatching { get; set; } = true;

    /// <summary>
    /// Whether to consider entity type in matching
    /// </summary>
    public bool RequireSameType { get; set; } = true;

    /// <summary>
    /// Whether to use embeddings for entity similarity
    /// </summary>
    public bool UseEmbeddings { get; set; }
}

/// <summary>
/// Extracted entity with detailed metadata
/// </summary>
public class ExtractedEntity
{
    /// <summary>
    /// Unique identifier for the entity
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The extracted entity text
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Normalized/canonical form of the entity
    /// </summary>
    public string NormalizedText { get; init; } = string.Empty;

    /// <summary>
    /// Entity type classification
    /// </summary>
    public NamedEntityType Type { get; init; }

    /// <summary>
    /// Confidence score (0.0-1.0)
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Start position in source text (character offset)
    /// </summary>
    public int StartPosition { get; init; }

    /// <summary>
    /// End position in source text (character offset)
    /// </summary>
    public int EndPosition { get; init; }

    /// <summary>
    /// Surrounding context text
    /// </summary>
    public string? Context { get; init; }

    /// <summary>
    /// Source document or chunk ID
    /// </summary>
    public string? SourceId { get; init; }

    /// <summary>
    /// Occurrence count in the document
    /// </summary>
    public int OccurrenceCount { get; init; } = 1;

    /// <summary>
    /// All positions where this entity appears
    /// </summary>
    public IReadOnlyList<EntityOccurrence> Occurrences { get; init; } = Array.Empty<EntityOccurrence>();

    /// <summary>
    /// Additional metadata
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();

    /// <summary>
    /// External knowledge base link (e.g., Wikipedia, Wikidata ID)
    /// </summary>
    public string? ExternalLink { get; init; }

    /// <summary>
    /// Entity subtype for more specific classification
    /// </summary>
    public string? Subtype { get; init; }
}

/// <summary>
/// Entity occurrence position
/// </summary>
public class EntityOccurrence
{
    /// <summary>
    /// Start position in source text
    /// </summary>
    public int StartPosition { get; init; }

    /// <summary>
    /// End position in source text
    /// </summary>
    public int EndPosition { get; init; }

    /// <summary>
    /// Sentence index where entity appears
    /// </summary>
    public int SentenceIndex { get; init; }
}

/// <summary>
/// Relation between two entities
/// </summary>
public class EntityRelation
{
    /// <summary>
    /// Unique identifier for the relation
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
    public RelationType Type { get; init; }

    /// <summary>
    /// Relation label/description
    /// </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// Confidence score (0.0-1.0)
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Whether the relation is directional
    /// </summary>
    public bool IsDirectional { get; init; } = true;

    /// <summary>
    /// Evidence text supporting the relation
    /// </summary>
    public string? Evidence { get; init; }

    /// <summary>
    /// Source document or chunk ID
    /// </summary>
    public string? SourceId { get; init; }

    /// <summary>
    /// Additional metadata
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// Entity graph containing entities and their relations
/// </summary>
public class EntityGraph
{
    /// <summary>
    /// Source document or chunk ID
    /// </summary>
    public string SourceId { get; init; } = string.Empty;

    /// <summary>
    /// Extracted entities
    /// </summary>
    public IReadOnlyList<ExtractedEntity> Entities { get; init; } = Array.Empty<ExtractedEntity>();

    /// <summary>
    /// Relations between entities
    /// </summary>
    public IReadOnlyList<EntityRelation> Relations { get; init; } = Array.Empty<EntityRelation>();

    /// <summary>
    /// Extraction timestamp
    /// </summary>
    public DateTime ExtractedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Processing statistics
    /// </summary>
    public EntityExtractionStats Stats { get; init; } = new();
}

/// <summary>
/// Linked entity graph across multiple documents
/// </summary>
public class LinkedEntityGraph
{
    /// <summary>
    /// Merged entities with cross-document linking
    /// </summary>
    public IReadOnlyList<LinkedEntity> Entities { get; init; } = Array.Empty<LinkedEntity>();

    /// <summary>
    /// All relations across documents
    /// </summary>
    public IReadOnlyList<EntityRelation> Relations { get; init; } = Array.Empty<EntityRelation>();

    /// <summary>
    /// Source entity graphs that were linked
    /// </summary>
    public IReadOnlyList<string> SourceIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Linking statistics
    /// </summary>
    public EntityLinkingStats Stats { get; init; } = new();
}

/// <summary>
/// Entity that has been linked across multiple documents
/// </summary>
public class LinkedEntity
{
    /// <summary>
    /// Canonical ID for the linked entity
    /// </summary>
    public string CanonicalId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Canonical/normalized text
    /// </summary>
    public string CanonicalText { get; init; } = string.Empty;

    /// <summary>
    /// Entity type
    /// </summary>
    public NamedEntityType Type { get; init; }

    /// <summary>
    /// All surface forms (different text representations)
    /// </summary>
    public IReadOnlyList<string> SurfaceForms { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Source entity IDs that were merged
    /// </summary>
    public IReadOnlyList<string> MergedEntityIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Documents/chunks where this entity appears
    /// </summary>
    public IReadOnlyList<string> SourceIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Total occurrence count across all documents
    /// </summary>
    public int TotalOccurrences { get; init; }

    /// <summary>
    /// Importance score based on frequency and relations
    /// </summary>
    public double ImportanceScore { get; init; }
}

/// <summary>
/// Entity extraction statistics
/// </summary>
public class EntityExtractionStats
{
    /// <summary>
    /// Total entities extracted
    /// </summary>
    public int TotalEntities { get; init; }

    /// <summary>
    /// Entities by type count
    /// </summary>
    public Dictionary<NamedEntityType, int> EntitiesByType { get; init; } = new();

    /// <summary>
    /// Total relations extracted
    /// </summary>
    public int TotalRelations { get; init; }

    /// <summary>
    /// Processing time in milliseconds
    /// </summary>
    public double ProcessingTimeMs { get; init; }

    /// <summary>
    /// Whether LLM was used
    /// </summary>
    public bool UsedLlm { get; init; }
}

/// <summary>
/// Entity linking statistics
/// </summary>
public class EntityLinkingStats
{
    /// <summary>
    /// Number of original entities
    /// </summary>
    public int OriginalEntityCount { get; init; }

    /// <summary>
    /// Number of merged/linked entities
    /// </summary>
    public int LinkedEntityCount { get; init; }

    /// <summary>
    /// Number of entity merges performed
    /// </summary>
    public int MergeCount { get; init; }

    /// <summary>
    /// Processing time in milliseconds
    /// </summary>
    public double ProcessingTimeMs { get; init; }
}

/// <summary>
/// Named entity type classification for advanced entity extraction.
/// More comprehensive than the basic EntityType in Models.
/// </summary>
public enum NamedEntityType
{
    /// <summary>Unknown or unclassified entity</summary>
    Unknown = 0,

    /// <summary>Person name</summary>
    Person,

    /// <summary>Organization name</summary>
    Organization,

    /// <summary>Location/Place</summary>
    Location,

    /// <summary>Date or time expression</summary>
    DateTime,

    /// <summary>Monetary value</summary>
    Money,

    /// <summary>Percentage</summary>
    Percentage,

    /// <summary>Product name</summary>
    Product,

    /// <summary>Event name</summary>
    Event,

    /// <summary>Work of art (book, movie, etc.)</summary>
    WorkOfArt,

    /// <summary>Law or regulation</summary>
    Law,

    /// <summary>Language</summary>
    Language,

    /// <summary>Nationality or religious/political group</summary>
    NationalityGroup,

    /// <summary>Facility (building, airport, etc.)</summary>
    Facility,

    /// <summary>Geopolitical entity (country, city, state)</summary>
    GeopoliticalEntity,

    /// <summary>Technical concept or term</summary>
    TechnicalConcept,

    /// <summary>Programming language or framework</summary>
    Technology,

    /// <summary>API, library, or package name</summary>
    Software,

    /// <summary>Abstract concept or idea</summary>
    Concept,

    /// <summary>Quantity or measurement</summary>
    Quantity,

    /// <summary>Email address</summary>
    Email,

    /// <summary>URL or web address</summary>
    Url,

    /// <summary>Phone number</summary>
    PhoneNumber,

    /// <summary>Custom entity type defined by user</summary>
    Custom
}

/// <summary>
/// Relation type classification
/// </summary>
public enum RelationType
{
    /// <summary>Unknown or unclassified relation</summary>
    Unknown = 0,

    /// <summary>Part-whole relationship (is part of)</summary>
    PartOf,

    /// <summary>Located in relationship</summary>
    LocatedIn,

    /// <summary>Works for/employed by relationship</summary>
    WorksFor,

    /// <summary>Founded/created by relationship</summary>
    FoundedBy,

    /// <summary>Owns/possesses relationship</summary>
    Owns,

    /// <summary>Uses/utilizes relationship</summary>
    Uses,

    /// <summary>Related to (general association)</summary>
    RelatedTo,

    /// <summary>Causes/leads to relationship</summary>
    Causes,

    /// <summary>Enables/allows relationship</summary>
    Enables,

    /// <summary>Depends on relationship</summary>
    DependsOn,

    /// <summary>Inherits from/extends relationship</summary>
    InheritsFrom,

    /// <summary>Implements relationship</summary>
    Implements,

    /// <summary>Contains relationship</summary>
    Contains,

    /// <summary>Precedes/comes before relationship</summary>
    Precedes,

    /// <summary>Follows/comes after relationship</summary>
    Follows,

    /// <summary>Is a type of (taxonomy)</summary>
    IsTypeOf,

    /// <summary>Synonym/equivalent relationship</summary>
    SynonymOf,

    /// <summary>Opposite/antonym relationship</summary>
    OppositeOf,

    /// <summary>Compares to relationship</summary>
    ComparesTo,

    /// <summary>Custom relation type</summary>
    Custom
}
