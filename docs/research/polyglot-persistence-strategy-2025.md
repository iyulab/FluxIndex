# FluxIndex Polyglot Persistence Strategy

## Research Document (December 2025)

A two-tier architecture strategy for polyglot persistence in FluxIndex ecosystem.

---

## Executive Summary

FluxIndex adopts a **Two-Tier Architecture** separating interface responsibility from full-stack implementations:

| Layer | Package | Databases | Purpose |
|-------|---------|-----------|---------|
| **FluxIndex SDK** | `FluxIndex.Core`, `FluxIndex.SDK` | PostgreSQL + Redis | Interface-first design, Hybrid Tier (single DB emulates all) |
| **FluxIndex Stack** | `FluxIndex.Stack.*` | PostgreSQL + Qdrant + Neo4j + Redis | Full-Stack production deployment |

**Core Philosophy**: FluxIndex SDK provides **interfaces and AI-powered enrichment pipelines**. The SDK's built-in implementation uses PostgreSQL to emulate Vector (pgvector) and Graph (recursive CTEs) capabilities - a practical "Hybrid Tier" for simpler deployments. FluxIndex.Stack implements the full polyglot stack with dedicated databases.

---

## 1. Architecture Overview

### 1.1 Two-Tier Design

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        FluxIndex SDK (Core)                              │
│                                                                          │
│   Responsibility: INTERFACES + AI ENRICHMENT PIPELINES                  │
│                                                                          │
│   ┌────────────────┐ ┌────────────────┐ ┌────────────────┐             │
│   │  IVectorStore  │ │  IGraphStore   │ │  ICacheStore   │             │
│   │                │ │                │ │                │             │
│   │ • StoreChunk   │ │ • StoreEntity  │ │ • GetSemantic  │             │
│   │ • SearchVector │ │ • TraverseFrom │ │ • SetSemantic  │             │
│   │ • BatchStore   │ │ • GetCommunity │ │ • CacheHot     │             │
│   └───────┬────────┘ └───────┬────────┘ └───────┬────────┘             │
│           │                  │                  │                       │
│   ┌───────┴──────────────────┴──────────────────┴───────┐              │
│   │              AI-Powered Enrichment Pipeline          │              │
│   │                                                      │              │
│   │  Document → Extract → Enhance → Transform → Store   │              │
│   │            Entities   Context   Multi-rep            │              │
│   └──────────────────────────────────────────────────────┘              │
│                                                                          │
│   Built-in Implementations (Hybrid Tier):                               │
│   ┌──────────────────────────────────────────────────────────────┐     │
│   │  PostgreSQL (Single DB)                                       │     │
│   │  ├─ RDB: Metadata, Documents, Collections                    │     │
│   │  ├─ Vector: pgvector extension                               │     │
│   │  └─ Graph: Adjacency list + Recursive CTEs                   │     │
│   └──────────────────────────────────────────────────────────────┘     │
│   ┌──────────────────────────────────────────────────────────────┐     │
│   │  Redis: Semantic cache, Hot data                              │     │
│   └──────────────────────────────────────────────────────────────┘     │
└─────────────────────────────────────────────────────────────────────────┘

                              ▼ Implements Interfaces

┌─────────────────────────────────────────────────────────────────────────┐
│                       FluxIndex Stack (Full-Stack)                       │
│                                                                          │
│   Responsibility: PRODUCTION-GRADE POLYGLOT IMPLEMENTATION              │
│                                                                          │
│   ┌────────────────┐ ┌────────────────┐ ┌────────────────┐             │
│   │    Qdrant      │ │     Neo4j      │ │     Redis      │             │
│   │   (Vector)     │ │    (Graph)     │ │    (Cache)     │             │
│   │                │ │                │ │                │             │
│   │ • HNSW index   │ │ • Cypher query │ │ • Cluster mode │             │
│   │ • Sparse vec   │ │ • Community    │ │ • Persistence  │             │
│   │ • Payload filt │ │ • Multi-hop    │ │ • Pub/Sub      │             │
│   └────────────────┘ └────────────────┘ └────────────────┘             │
│           +                                                              │
│   ┌────────────────┐                                                    │
│   │   PostgreSQL   │  Metadata + ACID transactions                      │
│   │     (RDB)      │                                                    │
│   └────────────────┘                                                    │
└─────────────────────────────────────────────────────────────────────────┘
```

### 1.2 Design Rationale

| Concern | FluxIndex SDK Approach | FluxIndex Stack Approach |
|---------|------------------------|--------------------------|
| **Deployment** | Single PostgreSQL + Redis | Multiple dedicated DBs |
| **Complexity** | Low (2 services) | High (4+ services) |
| **Performance** | Good (pgvector ~50ms @1M) | Excellent (Qdrant <10ms) |
| **Graph Queries** | Emulated (CTEs) | Native (Cypher) |
| **Scalability** | Vertical | Horizontal |
| **Use Case** | Development, Small-Medium | Enterprise, Large Scale |

---

## 2. FluxIndex SDK: Interface Definitions

### 2.1 IVectorStore (Enhanced)

```csharp
namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Vector storage abstraction supporting multiple embedding types.
/// SDK provides pgvector implementation; Stack uses Qdrant.
/// </summary>
public interface IVectorStore
{
    #region Storage Operations

    Task<string> StoreChunkAsync(
        ChunkWithEmbeddings chunk,
        CancellationToken ct = default);

    Task<IReadOnlyList<string>> StoreBatchAsync(
        IEnumerable<ChunkWithEmbeddings> chunks,
        StoreBatchOptions? options = null,
        CancellationToken ct = default);

    Task UpdateChunkAsync(
        string chunkId,
        ChunkWithEmbeddings chunk,
        CancellationToken ct = default);

    Task DeleteChunkAsync(
        string chunkId,
        CancellationToken ct = default);

    #endregion

    #region Search Operations

    /// <summary>
    /// Multi-vector search supporting different embedding types.
    /// </summary>
    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        VectorSearchRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Hybrid search combining vector similarity with metadata filters.
    /// </summary>
    Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(
        HybridSearchRequest request,
        CancellationToken ct = default);

    #endregion

    #region Management

    Task<VectorStoreStats> GetStatsAsync(CancellationToken ct = default);
    Task<bool> HealthCheckAsync(CancellationToken ct = default);
    Task OptimizeIndexAsync(CancellationToken ct = default);

    #endregion
}

/// <summary>
/// Chunk with multiple embedding representations for storage optimization.
/// </summary>
public record ChunkWithEmbeddings(
    string Id,
    string DocumentId,
    string Content,
    int Position,

    // Multiple embedding types for different retrieval strategies
    float[] ContentEmbedding,           // Primary: chunk content
    float[]? ContextualEmbedding,       // Contextual Retrieval: with document context
    float[]? HypotheticalEmbedding,     // HyDE: hypothetical question/answer
    float[]? EntityEmbedding,           // Entity-focused: extracted entities
    float[]? SummaryEmbedding,          // Summary: chunk summary

    ChunkMetadata Metadata);

public record VectorSearchRequest(
    float[] QueryEmbedding,
    int TopK = 10,
    double MinScore = 0.0,
    EmbeddingType SearchType = EmbeddingType.Content,
    Dictionary<string, object>? Filters = null);

public enum EmbeddingType
{
    Content,          // Standard content embedding
    Contextual,       // With document context header
    Hypothetical,     // HyDE-generated
    Entity,           // Entity-focused
    Summary           // Summary embedding
}
```

### 2.2 IGraphStore (New)

```csharp
namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Graph storage for entities, relationships, and communities.
/// SDK uses PostgreSQL adjacency list; Stack uses Neo4j.
/// </summary>
public interface IGraphStore
{
    #region Entity Operations

    Task<string> StoreEntityAsync(
        GraphEntity entity,
        CancellationToken ct = default);

    Task<IReadOnlyList<string>> StoreEntitiesBatchAsync(
        IEnumerable<GraphEntity> entities,
        CancellationToken ct = default);

    Task<GraphEntity?> GetEntityByIdAsync(
        string id,
        CancellationToken ct = default);

    Task<IReadOnlyList<GraphEntity>> SearchEntitiesByNameAsync(
        string namePattern,
        int limit = 20,
        CancellationToken ct = default);

    #endregion

    #region Relationship Operations

    Task StoreRelationshipAsync(
        GraphRelationship relationship,
        CancellationToken ct = default);

    Task<IReadOnlyList<GraphRelationship>> GetRelationshipsAsync(
        string entityId,
        RelationshipDirection direction = RelationshipDirection.Both,
        CancellationToken ct = default);

    /// <summary>
    /// Multi-hop traversal from starting entity.
    /// SDK: Recursive CTE; Stack: Native Cypher.
    /// </summary>
    Task<GraphTraversalResult> TraverseAsync(
        string startEntityId,
        TraversalOptions options,
        CancellationToken ct = default);

    #endregion

    #region Community Operations

    Task<string> StoreCommunityAsync(
        GraphCommunity community,
        CancellationToken ct = default);

    Task<IReadOnlyList<GraphCommunity>> GetCommunitiesAtLevelAsync(
        int level,
        int limit = 100,
        CancellationToken ct = default);

    Task<GraphCommunity?> GetCommunityForEntityAsync(
        string entityId,
        int level = 0,
        CancellationToken ct = default);

    #endregion

    #region Bulk Operations

    Task<GraphStoreStats> GetStatsAsync(CancellationToken ct = default);
    Task ClearAsync(string? collectionId = null, CancellationToken ct = default);
    Task<bool> HealthCheckAsync(CancellationToken ct = default);

    #endregion
}

public record GraphEntity(
    string Id,
    string Name,
    string Type,                         // PERSON, ORGANIZATION, CONCEPT, LOCATION, etc.
    string? Description,
    float[]? Embedding,
    double Importance,
    IReadOnlyList<string> SourceChunkIds,
    IDictionary<string, object>? Properties);

public record GraphRelationship(
    string Id,
    string SourceEntityId,
    string TargetEntityId,
    string Type,                         // WORKS_AT, LOCATED_IN, RELATED_TO, etc.
    double Weight,
    string? Description,
    IReadOnlyList<string> SourceChunkIds);

public record GraphCommunity(
    string Id,
    int Level,                           // Hierarchy level (0 = leaf)
    string Title,
    string Summary,
    double Importance,
    int MemberCount,
    IReadOnlyList<string> EntityIds,
    string? ParentCommunityId,
    float[]? SummaryEmbedding);

public record TraversalOptions(
    int MaxHops = 2,
    int MaxNodes = 50,
    double MinRelationshipWeight = 0.0,
    IReadOnlyList<string>? RelationshipTypes = null,
    TraversalAlgorithm Algorithm = TraversalAlgorithm.BFS);

public enum TraversalAlgorithm { BFS, DFS, WeightedPath }
public enum RelationshipDirection { Outgoing, Incoming, Both }
```

### 2.3 ICacheStore (Enhanced)

```csharp
namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Cache abstraction with semantic caching support.
/// </summary>
public interface ICacheStore
{
    #region Basic Cache

    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    #endregion

    #region Semantic Cache

    /// <summary>
    /// Find cached result for semantically similar query.
    /// </summary>
    Task<SemanticCacheHit<T>?> GetSemanticAsync<T>(
        float[] queryEmbedding,
        double similarityThreshold = 0.95,
        CancellationToken ct = default);

    /// <summary>
    /// Cache result with query embedding for semantic lookup.
    /// </summary>
    Task SetSemanticAsync<T>(
        string queryText,
        float[] queryEmbedding,
        T result,
        TimeSpan? expiry = null,
        CancellationToken ct = default);

    #endregion

    #region Hot Data

    /// <summary>
    /// Pre-warm cache with frequently accessed entities.
    /// </summary>
    Task WarmupEntitiesAsync(
        IEnumerable<GraphEntity> entities,
        TimeSpan? expiry = null,
        CancellationToken ct = default);

    Task<GraphEntity?> GetCachedEntityAsync(
        string entityId,
        CancellationToken ct = default);

    #endregion
}

public record SemanticCacheHit<T>(
    T Value,
    double Similarity,
    string OriginalQuery,
    DateTime CachedAt,
    int HitCount);
```

---

## 3. AI-Powered Enrichment Pipeline

### 3.1 Core Focus: Maximize Storage Characteristics

FluxIndex SDK의 핵심 가치는 **문서 저장 시 AI를 활용한 강화**입니다. 각 저장소 타입의 특성을 최대한 활용하기 위한 enrichment pipeline을 제공합니다.

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    Document Enrichment Pipeline                          │
│                                                                          │
│  Input: Raw Document                                                    │
│    │                                                                     │
│    ▼                                                                     │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │  Stage 1: EXTRACTION (FileFlux)                                  │   │
│  │  • Parse document (PDF, DOCX, HTML, MD, etc.)                   │   │
│  │  • Extract text, images, tables                                 │   │
│  │  • Convert to Markdown                                          │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│    │                                                                     │
│    ▼                                                                     │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │  Stage 2: CHUNKING (Intelligent/Semantic)                        │   │
│  │  • Language-aware chunking (11 profiles)                        │   │
│  │  • Semantic boundary detection                                  │   │
│  │  • Overlap management                                           │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│    │                                                                     │
│    ▼                                                                     │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │  Stage 3: AI ENRICHMENT                                          │   │
│  │                                                                  │   │
│  │  ┌───────────────────┐  ┌───────────────────┐                  │   │
│  │  │ Entity Extraction │  │ Contextual Header │                  │   │
│  │  │ • Named entities  │  │ • Document context│                  │   │
│  │  │ • Relationships   │  │ • Chunk summary   │                  │   │
│  │  │ • Types & props   │  │ • Keywords        │                  │   │
│  │  └─────────┬─────────┘  └─────────┬─────────┘                  │   │
│  │            │                      │                             │   │
│  │  ┌─────────┴─────────┐  ┌─────────┴─────────┐                  │   │
│  │  │ HyDE Generation   │  │ QA Generation     │                  │   │
│  │  │ • Hypothetical Qs │  │ • Training pairs  │                  │   │
│  │  │ • Answer synthesis│  │ • Evaluation data │                  │   │
│  │  └───────────────────┘  └───────────────────┘                  │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│    │                                                                     │
│    ▼                                                                     │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │  Stage 4: MULTI-REPRESENTATION EMBEDDING                         │   │
│  │                                                                  │   │
│  │  For VECTOR STORE optimization:                                 │   │
│  │  ├─ Content Embedding: Standard chunk text                      │   │
│  │  ├─ Contextual Embedding: With document context header          │   │
│  │  ├─ Hypothetical Embedding: HyDE-generated Q&A                  │   │
│  │  ├─ Entity Embedding: Extracted entities text                   │   │
│  │  └─ Summary Embedding: Chunk summary                            │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│    │                                                                     │
│    ▼                                                                     │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │  Stage 5: STORAGE DISTRIBUTION                                   │   │
│  │                                                                  │   │
│  │  ┌─────────────────┐                                            │   │
│  │  │   VECTOR STORE  │ ← ChunkWithEmbeddings (5 types)            │   │
│  │  │   (Semantic)    │   Multi-representation for diverse queries │   │
│  │  └─────────────────┘                                            │   │
│  │                                                                  │   │
│  │  ┌─────────────────┐                                            │   │
│  │  │   GRAPH STORE   │ ← Entities, Relationships, Communities     │   │
│  │  │   (Knowledge)   │   Knowledge graph for reasoning            │   │
│  │  └─────────────────┘                                            │   │
│  │                                                                  │   │
│  │  ┌─────────────────┐                                            │   │
│  │  │    RDB STORE    │ ← Metadata, Processing status, Audit log  │   │
│  │  │   (Metadata)    │   ACID transactions, relational queries   │   │
│  │  └─────────────────┘                                            │   │
│  │                                                                  │   │
│  │  ┌─────────────────┐                                            │   │
│  │  │   CACHE STORE   │ ← Hot entities, Semantic query cache       │   │
│  │  │   (Latency)     │   Sub-millisecond access                   │   │
│  │  └─────────────────┘                                            │   │
│  └─────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
```

### 3.2 Storage-Specific Optimization Strategies

#### Vector Store: Multi-Representation Strategy

```csharp
/// <summary>
/// Generates multiple embedding representations for optimal retrieval.
/// </summary>
public interface IMultiRepresentationEmbedder
{
    /// <summary>
    /// Generate all embedding types for a chunk.
    /// </summary>
    Task<ChunkEmbeddings> GenerateAllEmbeddingsAsync(
        DocumentChunk chunk,
        DocumentContext context,
        CancellationToken ct = default);
}

public record ChunkEmbeddings(
    float[] Content,              // Direct chunk content
    float[]? Contextual,          // Chunk + document context header
    float[]? Hypothetical,        // HyDE: "What question would this answer?"
    float[]? Entity,              // Concatenated entity descriptions
    float[]? Summary);            // Abstractive summary

public record DocumentContext(
    string DocumentTitle,
    string? DocumentSummary,
    IReadOnlyList<string> Keywords,
    string? PreviousChunkSummary,
    string? NextChunkSummary);
```

**Contextual Retrieval Implementation**:
```csharp
/// <summary>
/// Generates context header for each chunk (Anthropic's Contextual Retrieval).
/// Reduces retrieval failure by ~67%.
/// </summary>
public class ContextualHeaderGenerator
{
    private readonly ITextCompletionService _llm;

    public async Task<string> GenerateContextHeaderAsync(
        DocumentChunk chunk,
        string documentContent,
        CancellationToken ct = default)
    {
        var prompt = $"""
            <document>
            {documentContent}
            </document>

            Here is the chunk we want to situate:
            <chunk>
            {chunk.Content}
            </chunk>

            Generate a short (2-3 sentences) context that explains:
            1. Where this chunk fits in the document
            2. Key entities or concepts it references
            3. What information it provides

            Context:
            """;

        return await _llm.CompleteAsync(prompt, ct);
    }
}
```

#### Graph Store: Entity-Relationship Extraction

```csharp
/// <summary>
/// Extracts entities and relationships for knowledge graph construction.
/// </summary>
public interface IKnowledgeGraphExtractor
{
    /// <summary>
    /// Extract entities from document chunks.
    /// </summary>
    Task<IReadOnlyList<ExtractedEntity>> ExtractEntitiesAsync(
        IEnumerable<DocumentChunk> chunks,
        EntityExtractionOptions options,
        CancellationToken ct = default);

    /// <summary>
    /// Extract relationships between entities.
    /// </summary>
    Task<IReadOnlyList<ExtractedRelationship>> ExtractRelationshipsAsync(
        IReadOnlyList<ExtractedEntity> entities,
        IEnumerable<DocumentChunk> sourceChunks,
        CancellationToken ct = default);
}

public record ExtractedEntity(
    string Name,
    string Type,                    // PERSON, ORG, CONCEPT, LOCATION, EVENT, etc.
    string? Description,
    double Confidence,
    IReadOnlyList<string> SourceChunkIds,
    IDictionary<string, object>? Properties);

public record ExtractedRelationship(
    string SourceEntity,
    string TargetEntity,
    string Type,                    // WORKS_AT, LOCATED_IN, PART_OF, etc.
    string? Description,
    double Confidence,
    IReadOnlyList<string> SourceChunkIds);
```

#### RDB Store: Rich Metadata

```csharp
/// <summary>
/// Document metadata optimized for relational queries.
/// </summary>
public record EnrichedDocumentMetadata(
    // Core
    string DocumentId,
    string SourcePath,
    string Title,
    string MimeType,

    // Extracted
    string? Author,
    DateTime? CreatedDate,
    DateTime? ModifiedDate,
    string? Language,

    // AI-Generated
    string? Summary,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> Topics,
    string? DocumentType,           // REPORT, ARTICLE, MANUAL, etc.

    // Processing
    int ChunkCount,
    int EntityCount,
    ProcessingStatus Status,
    DateTime IndexedAt,

    // Quality
    double? ContentQualityScore,
    double? RelevanceScore);
```

### 3.3 Enrichment Pipeline Service

```csharp
namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Orchestrates document enrichment across all storage types.
/// </summary>
public interface IDocumentEnrichmentPipeline
{
    /// <summary>
    /// Process document through full enrichment pipeline.
    /// </summary>
    Task<EnrichmentResult> ProcessAsync(
        Stream documentStream,
        string fileName,
        EnrichmentOptions options,
        CancellationToken ct = default);

    /// <summary>
    /// Process with progress reporting.
    /// </summary>
    Task<EnrichmentResult> ProcessAsync(
        Stream documentStream,
        string fileName,
        EnrichmentOptions options,
        IProgress<EnrichmentProgress>? progress,
        CancellationToken ct = default);
}

public record EnrichmentOptions(
    // Chunking
    ChunkingStrategy ChunkingStrategy = ChunkingStrategy.Intelligent,
    int MaxChunkSize = 1024,
    int ChunkOverlap = 128,

    // Embeddings (which types to generate)
    bool GenerateContentEmbedding = true,
    bool GenerateContextualEmbedding = true,
    bool GenerateHypotheticalEmbedding = false,  // Expensive
    bool GenerateEntityEmbedding = true,
    bool GenerateSummaryEmbedding = false,

    // Knowledge Graph
    bool ExtractEntities = true,
    bool ExtractRelationships = true,
    bool DetectCommunities = false,  // Expensive, batch operation

    // Quality
    bool ValidateContent = true,
    bool MaskPII = false,

    // Metadata
    bool GenerateSummary = true,
    bool ExtractKeywords = true);

public record EnrichmentResult(
    string DocumentId,
    int ChunksCreated,
    int EntitiesExtracted,
    int RelationshipsExtracted,
    EnrichedDocumentMetadata Metadata,
    TimeSpan ProcessingTime,
    IReadOnlyList<string>? Warnings);

public record EnrichmentProgress(
    EnrichmentStage Stage,
    int Current,
    int Total,
    string? Message);

public enum EnrichmentStage
{
    Extracting,
    Chunking,
    ExtractingEntities,
    GeneratingEmbeddings,
    StoringVectors,
    StoringGraph,
    Finalizing
}
```

---

## 4. SDK Implementation: Hybrid Tier (PostgreSQL)

### 4.1 PostgreSQL Schema for Polyglot Emulation

```sql
-- ================================================================
-- FluxIndex SDK: PostgreSQL Hybrid Tier Schema
-- Single database emulating Vector + Graph + RDB
-- ================================================================

-- Enable extensions
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "vector";        -- pgvector for embeddings
CREATE EXTENSION IF NOT EXISTS "pg_trgm";       -- Trigram for text search

-- ================================================================
-- RDB Layer: Documents & Metadata
-- ================================================================

CREATE TABLE documents (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    source_id VARCHAR(255),
    source_path TEXT,
    title TEXT NOT NULL,
    mime_type VARCHAR(100),
    language VARCHAR(10),

    -- AI-enriched metadata
    summary TEXT,
    keywords TEXT[],
    topics TEXT[],
    document_type VARCHAR(50),
    author VARCHAR(255),

    -- Quality scores
    content_quality_score FLOAT,

    -- Processing
    status VARCHAR(20) DEFAULT 'pending',
    chunk_count INT DEFAULT 0,
    entity_count INT DEFAULT 0,

    -- Timestamps
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    indexed_at TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,

    -- Full document context for contextual retrieval
    full_text TEXT
);

CREATE INDEX idx_documents_status ON documents(status);
CREATE INDEX idx_documents_type ON documents(document_type);
CREATE INDEX idx_documents_keywords ON documents USING GIN(keywords);

-- ================================================================
-- Vector Layer: Chunks with Multi-Representation Embeddings
-- ================================================================

CREATE TABLE chunks (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    document_id UUID REFERENCES documents(id) ON DELETE CASCADE,
    position INT NOT NULL,
    content TEXT NOT NULL,

    -- Context (for contextual retrieval)
    context_header TEXT,              -- AI-generated context
    previous_chunk_id UUID,
    next_chunk_id UUID,

    -- Multi-representation embeddings (pgvector)
    content_embedding VECTOR(1536),        -- Standard content
    contextual_embedding VECTOR(1536),     -- With context header
    hypothetical_embedding VECTOR(1536),   -- HyDE Q&A
    entity_embedding VECTOR(1536),         -- Entity-focused
    summary_embedding VECTOR(1536),        -- Summary

    -- Metadata
    metadata JSONB,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,

    UNIQUE(document_id, position)
);

-- Vector indexes for each embedding type
CREATE INDEX idx_chunks_content_vec ON chunks
    USING ivfflat (content_embedding vector_cosine_ops) WITH (lists = 100);
CREATE INDEX idx_chunks_contextual_vec ON chunks
    USING ivfflat (contextual_embedding vector_cosine_ops) WITH (lists = 100);
CREATE INDEX idx_chunks_entity_vec ON chunks
    USING ivfflat (entity_embedding vector_cosine_ops) WITH (lists = 100);

-- ================================================================
-- Graph Layer: Entities & Relationships (Adjacency List)
-- ================================================================

CREATE TABLE entities (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    name TEXT NOT NULL,
    type VARCHAR(50) NOT NULL,        -- PERSON, ORG, CONCEPT, LOCATION, etc.
    description TEXT,

    -- For vector-enhanced graph search
    embedding VECTOR(1536),

    -- Importance (PageRank-style)
    importance FLOAT DEFAULT 0.0,

    -- Source tracking
    source_chunk_ids UUID[],

    -- Properties
    properties JSONB,

    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_entities_name ON entities USING GIN(name gin_trgm_ops);
CREATE INDEX idx_entities_type ON entities(type);
CREATE INDEX idx_entities_embedding ON entities
    USING ivfflat (embedding vector_cosine_ops) WITH (lists = 50);

CREATE TABLE relationships (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    source_entity_id UUID REFERENCES entities(id) ON DELETE CASCADE,
    target_entity_id UUID REFERENCES entities(id) ON DELETE CASCADE,
    type VARCHAR(50) NOT NULL,        -- WORKS_AT, LOCATED_IN, PART_OF, etc.
    weight FLOAT DEFAULT 1.0,
    description TEXT,
    source_chunk_ids UUID[],
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,

    UNIQUE(source_entity_id, target_entity_id, type)
);

CREATE INDEX idx_relationships_source ON relationships(source_entity_id);
CREATE INDEX idx_relationships_target ON relationships(target_entity_id);
CREATE INDEX idx_relationships_type ON relationships(type);

-- Communities (Leiden algorithm results)
CREATE TABLE communities (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    level INT NOT NULL,               -- Hierarchy level (0 = leaf)
    title TEXT,
    summary TEXT NOT NULL,
    importance FLOAT DEFAULT 0.0,
    member_count INT DEFAULT 0,
    entity_ids UUID[],
    parent_community_id UUID REFERENCES communities(id),
    summary_embedding VECTOR(1536),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_communities_level ON communities(level);

-- ================================================================
-- Graph Traversal Functions (Recursive CTEs)
-- ================================================================

-- Multi-hop traversal function
CREATE OR REPLACE FUNCTION traverse_from_entity(
    start_entity_id UUID,
    max_hops INT DEFAULT 2,
    max_nodes INT DEFAULT 50,
    min_weight FLOAT DEFAULT 0.0,
    relationship_types TEXT[] DEFAULT NULL
)
RETURNS TABLE (
    entity_id UUID,
    entity_name TEXT,
    entity_type VARCHAR(50),
    hop_distance INT,
    path UUID[]
) AS $$
BEGIN
    RETURN QUERY
    WITH RECURSIVE traversal AS (
        -- Base case: starting entity
        SELECT
            e.id,
            e.name,
            e.type,
            0 AS distance,
            ARRAY[e.id] AS path
        FROM entities e
        WHERE e.id = start_entity_id

        UNION ALL

        -- Recursive case: follow relationships
        SELECT
            e.id,
            e.name,
            e.type,
            t.distance + 1,
            t.path || e.id
        FROM traversal t
        JOIN relationships r ON (r.source_entity_id = t.id OR r.target_entity_id = t.id)
        JOIN entities e ON (
            e.id = CASE
                WHEN r.source_entity_id = t.id THEN r.target_entity_id
                ELSE r.source_entity_id
            END
        )
        WHERE t.distance < max_hops
          AND NOT (e.id = ANY(t.path))  -- Avoid cycles
          AND r.weight >= min_weight
          AND (relationship_types IS NULL OR r.type = ANY(relationship_types))
    )
    SELECT DISTINCT ON (traversal.id)
        traversal.id,
        traversal.name,
        traversal.type,
        traversal.distance,
        traversal.path
    FROM traversal
    ORDER BY traversal.id, traversal.distance
    LIMIT max_nodes;
END;
$$ LANGUAGE plpgsql;
```

### 4.2 PostgreSQL IGraphStore Implementation

```csharp
namespace FluxIndex.Storage.PostgreSQL;

/// <summary>
/// PostgreSQL implementation of IGraphStore using adjacency list pattern.
/// </summary>
public class PostgresGraphStore : IGraphStore
{
    private readonly FluxIndexDbContext _context;
    private readonly ILogger<PostgresGraphStore> _logger;

    public async Task<GraphTraversalResult> TraverseAsync(
        string startEntityId,
        TraversalOptions options,
        CancellationToken ct = default)
    {
        // Use PostgreSQL recursive CTE function
        var sql = @"
            SELECT * FROM traverse_from_entity(
                @startId::uuid,
                @maxHops,
                @maxNodes,
                @minWeight,
                @relationshipTypes::text[]
            )";

        var results = await _context.Database
            .SqlQueryRaw<TraversalRow>(sql,
                new NpgsqlParameter("startId", Guid.Parse(startEntityId)),
                new NpgsqlParameter("maxHops", options.MaxHops),
                new NpgsqlParameter("maxNodes", options.MaxNodes),
                new NpgsqlParameter("minWeight", options.MinRelationshipWeight),
                new NpgsqlParameter("relationshipTypes", options.RelationshipTypes?.ToArray()))
            .ToListAsync(ct);

        return new GraphTraversalResult(
            results.Select(r => new TraversedEntity(
                r.EntityId.ToString(),
                r.EntityName,
                r.EntityType,
                r.HopDistance,
                r.Path.Select(p => p.ToString()).ToList()
            )).ToList());
    }
}
```

---

## 5. Stack Implementation: Full Polyglot

### 5.1 FluxIndex.Stack Database Architecture

```
FluxIndex.Stack Production Architecture
═══════════════════════════════════════

┌─────────────────────────────────────────────────────────────────┐
│                        API Layer                                 │
│              FluxIndex.Stack.Api (ASP.NET Core)                 │
└───────────────────────────┬─────────────────────────────────────┘
                            │
┌───────────────────────────┴─────────────────────────────────────┐
│                     Service Layer                                │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐ │
│  │ SearchService   │  │ IndexingService │  │ GraphRAGService │ │
│  │ (IPolyglotQuery)│  │ (Pipeline)      │  │ (Community)     │ │
│  └────────┬────────┘  └────────┬────────┘  └────────┬────────┘ │
└───────────┼────────────────────┼────────────────────┼───────────┘
            │                    │                    │
┌───────────┴────────────────────┴────────────────────┴───────────┐
│                   Storage Abstraction Layer                      │
│                  (FluxIndex.Core Interfaces)                     │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐            │
│  │ IVectorStore │ │ IGraphStore  │ │ ICacheStore  │            │
│  └──────┬───────┘ └──────┬───────┘ └──────┬───────┘            │
└─────────┼────────────────┼────────────────┼─────────────────────┘
          │                │                │
          ▼                ▼                ▼
┌──────────────┐  ┌──────────────┐  ┌──────────────┐  ┌──────────┐
│   Qdrant     │  │    Neo4j     │  │    Redis     │  │PostgreSQL│
│              │  │              │  │              │  │  (RDB)   │
│ • HNSW index │  │ • Cypher     │  │ • Cluster    │  │ • ACID   │
│ • Multi-vec  │  │ • Community  │  │ • Semantic   │  │ • Meta   │
│ • Sparse     │  │ • Multi-hop  │  │ • Hot cache  │  │ • Audit  │
│ • Payload    │  │ • PageRank   │  │ • Pub/Sub    │  │          │
└──────────────┘  └──────────────┘  └──────────────┘  └──────────┘
```

### 5.2 Stack-Specific Implementations

#### Qdrant Vector Store

```csharp
namespace FluxIndex.Stack.Infrastructure.Storage;

public class QdrantVectorStore : IVectorStore
{
    private readonly QdrantClient _client;
    private readonly ILogger<QdrantVectorStore> _logger;

    public async Task<IReadOnlyList<string>> StoreBatchAsync(
        IEnumerable<ChunkWithEmbeddings> chunks,
        StoreBatchOptions? options = null,
        CancellationToken ct = default)
    {
        var points = chunks.Select(chunk => new PointStruct
        {
            Id = new PointId { Uuid = chunk.Id },
            Vectors = new Vectors
            {
                Vectors_ =
                {
                    ["content"] = new Vector { Data = { chunk.ContentEmbedding } },
                    ["contextual"] = chunk.ContextualEmbedding != null
                        ? new Vector { Data = { chunk.ContextualEmbedding } }
                        : null,
                    ["entity"] = chunk.EntityEmbedding != null
                        ? new Vector { Data = { chunk.EntityEmbedding } }
                        : null
                }
            },
            Payload =
            {
                ["document_id"] = chunk.DocumentId,
                ["content"] = chunk.Content,
                ["position"] = chunk.Position,
                ["metadata"] = JsonSerializer.Serialize(chunk.Metadata)
            }
        }).ToList();

        await _client.UpsertAsync(
            collectionName: "chunks",
            points: points,
            wait: true,
            cancellationToken: ct);

        return points.Select(p => p.Id.Uuid).ToList();
    }

    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        VectorSearchRequest request,
        CancellationToken ct = default)
    {
        var vectorName = request.SearchType switch
        {
            EmbeddingType.Content => "content",
            EmbeddingType.Contextual => "contextual",
            EmbeddingType.Entity => "entity",
            _ => "content"
        };

        var results = await _client.SearchAsync(
            collectionName: "chunks",
            vector: request.QueryEmbedding,
            vectorName: vectorName,
            limit: (ulong)request.TopK,
            scoreThreshold: (float)request.MinScore,
            filter: BuildFilter(request.Filters),
            cancellationToken: ct);

        return results.Select(r => new VectorSearchResult(
            ChunkId: r.Id.Uuid,
            Score: r.Score,
            Content: r.Payload["content"].StringValue,
            DocumentId: r.Payload["document_id"].StringValue,
            Position: (int)r.Payload["position"].IntegerValue,
            Metadata: JsonSerializer.Deserialize<ChunkMetadata>(
                r.Payload["metadata"].StringValue)
        )).ToList();
    }
}
```

#### Neo4j Graph Store

```csharp
namespace FluxIndex.Stack.Infrastructure.Storage;

public class Neo4jGraphStore : IGraphStore
{
    private readonly IDriver _driver;
    private readonly ILogger<Neo4jGraphStore> _logger;

    public async Task<GraphTraversalResult> TraverseAsync(
        string startEntityId,
        TraversalOptions options,
        CancellationToken ct = default)
    {
        await using var session = _driver.AsyncSession();

        var relationshipFilter = options.RelationshipTypes?.Any() == true
            ? $"[:{string.Join("|", options.RelationshipTypes)}]"
            : "";

        var query = $@"
            MATCH path = (start:Entity {{id: $startId}})-{relationshipFilter}*1..{options.MaxHops}-(related:Entity)
            WHERE related.importance >= $minWeight
            WITH related, length(path) as distance, nodes(path) as pathNodes
            RETURN DISTINCT related.id as entityId,
                   related.name as name,
                   related.type as type,
                   distance,
                   [n IN pathNodes | n.id] as path
            ORDER BY distance, related.importance DESC
            LIMIT $maxNodes";

        var result = await session.RunAsync(query, new
        {
            startId = startEntityId,
            minWeight = options.MinRelationshipWeight,
            maxNodes = options.MaxNodes
        });

        var entities = await result.ToListAsync(ct);

        return new GraphTraversalResult(
            entities.Select(r => new TraversedEntity(
                r["entityId"].As<string>(),
                r["name"].As<string>(),
                r["type"].As<string>(),
                r["distance"].As<int>(),
                r["path"].As<List<string>>()
            )).ToList());
    }

    public async Task<IReadOnlyList<GraphCommunity>> GetCommunitiesAtLevelAsync(
        int level,
        int limit = 100,
        CancellationToken ct = default)
    {
        await using var session = _driver.AsyncSession();

        var query = @"
            MATCH (c:Community {level: $level})
            OPTIONAL MATCH (c)<-[:BELONGS_TO]-(e:Entity)
            WITH c, collect(e.id) as entityIds
            RETURN c.id as id,
                   c.level as level,
                   c.title as title,
                   c.summary as summary,
                   c.importance as importance,
                   size(entityIds) as memberCount,
                   entityIds,
                   c.parentId as parentCommunityId
            ORDER BY c.importance DESC
            LIMIT $limit";

        var result = await session.RunAsync(query, new { level, limit });
        var communities = await result.ToListAsync(ct);

        return communities.Select(r => new GraphCommunity(
            r["id"].As<string>(),
            r["level"].As<int>(),
            r["title"].As<string>(),
            r["summary"].As<string>(),
            r["importance"].As<double>(),
            r["memberCount"].As<int>(),
            r["entityIds"].As<List<string>>(),
            r["parentCommunityId"].As<string?>(),
            null // Embedding stored in Vector DB
        )).ToList();
    }
}
```

---

## 6. Unified Search: Polyglot Query Coordination

### 6.1 IPolyglotSearchService

```csharp
namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Coordinates search across all storage types for optimal retrieval.
/// </summary>
public interface IPolyglotSearchService
{
    /// <summary>
    /// Execute unified search leveraging all storage characteristics.
    /// </summary>
    Task<PolyglotSearchResult> SearchAsync(
        string query,
        PolyglotSearchOptions options,
        CancellationToken ct = default);
}

public record PolyglotSearchOptions(
    int TopK = 10,
    double MinScore = 0.0,

    // Search strategy
    SearchStrategy Strategy = SearchStrategy.Auto,

    // Component weights (for Hybrid/Auto)
    double VectorWeight = 0.5,
    double GraphWeight = 0.3,
    double KeywordWeight = 0.2,

    // Embedding type preferences
    EmbeddingType[] PreferredEmbeddings = null,  // Default: Content, Contextual

    // Graph options
    bool UseGraphEnhancement = true,
    int GraphHops = 2,

    // Cache options
    bool UseCache = true,
    double CacheSimilarityThreshold = 0.95,

    // Filters
    Dictionary<string, object>? Filters = null);

public enum SearchStrategy
{
    Auto,           // Analyzer determines best path
    VectorOnly,     // Pure semantic search
    GraphOnly,      // Entity/relationship traversal
    KeywordOnly,    // BM25 keyword matching
    Hybrid,         // Vector + Keyword + Graph fusion
    GraphEnhanced   // Vector search with graph context expansion
}

public record PolyglotSearchResult(
    IReadOnlyList<SearchResultItem> Results,
    SearchExecutionInfo ExecutionInfo);

public record SearchResultItem(
    string ChunkId,
    string DocumentId,
    string Content,
    double Score,

    // Source attribution
    double VectorScore,
    double GraphScore,
    double KeywordScore,

    // Graph context (if graph-enhanced)
    IReadOnlyList<RelatedEntity>? RelatedEntities,
    GraphCommunity? RelevantCommunity);

public record SearchExecutionInfo(
    SearchStrategy StrategyUsed,
    bool CacheHit,
    TimeSpan TotalTime,
    TimeSpan? CacheLookupTime,
    TimeSpan? VectorSearchTime,
    TimeSpan? GraphSearchTime,
    TimeSpan? FusionTime,
    int VectorResultCount,
    int GraphResultCount);
```

### 6.2 Search Flow Implementation

```csharp
namespace FluxIndex.Core.Application.Services;

public class PolyglotSearchService : IPolyglotSearchService
{
    private readonly IVectorStore _vectorStore;
    private readonly IGraphStore _graphStore;
    private readonly ICacheStore _cacheStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly IQueryComplexityAnalyzer _queryAnalyzer;
    private readonly IRankFusionService _fusionService;

    public async Task<PolyglotSearchResult> SearchAsync(
        string query,
        PolyglotSearchOptions options,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var execInfo = new SearchExecutionInfoBuilder();

        // 1. Check semantic cache
        if (options.UseCache)
        {
            var cacheStart = stopwatch.Elapsed;
            var queryEmbedding = await _embeddingService.EmbedAsync(query, ct);
            var cached = await _cacheStore.GetSemanticAsync<PolyglotSearchResult>(
                queryEmbedding, options.CacheSimilarityThreshold, ct);

            execInfo.CacheLookupTime = stopwatch.Elapsed - cacheStart;

            if (cached != null)
            {
                execInfo.CacheHit = true;
                return cached.Value with { ExecutionInfo = execInfo.Build(stopwatch.Elapsed) };
            }
        }

        // 2. Determine search strategy
        var strategy = options.Strategy == SearchStrategy.Auto
            ? await DetermineStrategyAsync(query, ct)
            : options.Strategy;
        execInfo.StrategyUsed = strategy;

        // 3. Execute search based on strategy
        var results = strategy switch
        {
            SearchStrategy.VectorOnly => await VectorOnlySearchAsync(query, options, execInfo, ct),
            SearchStrategy.GraphOnly => await GraphOnlySearchAsync(query, options, execInfo, ct),
            SearchStrategy.GraphEnhanced => await GraphEnhancedSearchAsync(query, options, execInfo, ct),
            SearchStrategy.Hybrid => await HybridSearchAsync(query, options, execInfo, ct),
            _ => await HybridSearchAsync(query, options, execInfo, ct)
        };

        // 4. Cache results
        if (options.UseCache && results.Any())
        {
            var queryEmbedding = await _embeddingService.EmbedAsync(query, ct);
            await _cacheStore.SetSemanticAsync(
                query, queryEmbedding,
                new PolyglotSearchResult(results, execInfo.Build(stopwatch.Elapsed)),
                TimeSpan.FromMinutes(30), ct);
        }

        return new PolyglotSearchResult(results, execInfo.Build(stopwatch.Elapsed));
    }

    private async Task<IReadOnlyList<SearchResultItem>> GraphEnhancedSearchAsync(
        string query,
        PolyglotSearchOptions options,
        SearchExecutionInfoBuilder execInfo,
        CancellationToken ct)
    {
        // Step 1: Vector search with contextual embedding
        var vectorStart = Stopwatch.GetTimestamp();
        var queryEmbedding = await _embeddingService.EmbedAsync(query, ct);

        var vectorResults = await _vectorStore.SearchAsync(new VectorSearchRequest(
            queryEmbedding, options.TopK * 2, options.MinScore,
            EmbeddingType.Contextual, options.Filters), ct);

        execInfo.VectorSearchTime = Stopwatch.GetElapsedTime(vectorStart);
        execInfo.VectorResultCount = vectorResults.Count;

        // Step 2: Extract entities from query and expand via graph
        var graphStart = Stopwatch.GetTimestamp();
        var queryEntities = await ExtractEntitiesFromQueryAsync(query, ct);

        var graphExpansion = new List<GraphEntity>();
        foreach (var entity in queryEntities)
        {
            var traversal = await _graphStore.TraverseAsync(entity.Id,
                new TraversalOptions(options.GraphHops, 20), ct);
            graphExpansion.AddRange(traversal.Entities);
        }

        execInfo.GraphSearchTime = Stopwatch.GetElapsedTime(graphStart);
        execInfo.GraphResultCount = graphExpansion.Count;

        // Step 3: Enrich vector results with graph context
        var enrichedResults = await EnrichWithGraphContextAsync(
            vectorResults, graphExpansion, ct);

        // Step 4: Re-rank with graph boost
        return await _fusionService.FuseAndRerankAsync(
            enrichedResults, options.VectorWeight, options.GraphWeight, ct);
    }
}
```

---

## 7. Implementation Roadmap (Revised)

### Phase 1: Interface Foundation (v0.5.x)

| Task | Package | Priority |
|------|---------|----------|
| Define `IGraphStore` interface | FluxIndex.Core | High |
| Define `ICacheStore` enhancements | FluxIndex.Core | High |
| Define `IDocumentEnrichmentPipeline` | FluxIndex.Core | High |
| PostgreSQL `IGraphStore` (adjacency list + CTEs) | FluxIndex.Storage.PostgreSQL | High |
| Redis semantic cache implementation | FluxIndex.Cache.Redis | Medium |

**Deliverables:**
- Complete interface definitions in Core
- PostgreSQL hybrid tier implementation (graph emulation)
- Enhanced semantic caching

### Phase 2: AI Enrichment Pipeline (v0.6.x)

| Task | Package | Priority |
|------|---------|----------|
| `IMultiRepresentationEmbedder` implementation | FluxIndex.SDK | High |
| Contextual header generation | FluxIndex.SDK | High |
| Entity/relationship extraction | FluxIndex.SDK | High |
| `IDocumentEnrichmentPipeline` orchestrator | FluxIndex.SDK | High |
| FluxImprover integration for quality | FluxIndex.Extensions.FluxImprover | Medium |

**Deliverables:**
- Complete enrichment pipeline with multi-representation embeddings
- Contextual Retrieval implementation
- Entity extraction for graph construction

### Phase 3: Stack Full Implementation (v0.7.x)

| Task | Package | Priority |
|------|---------|----------|
| Qdrant `IVectorStore` implementation | FluxIndex.Stack.Infrastructure | High |
| Neo4j `IGraphStore` implementation | FluxIndex.Stack.Infrastructure | High |
| `IPolyglotSearchService` implementation | FluxIndex.Stack.Application | High |
| Community detection (Leiden) integration | FluxIndex.Stack.Application | Medium |

**Deliverables:**
- Production-grade Stack with Qdrant + Neo4j
- Unified polyglot search
- GraphRAG with persistent graph

### Phase 4: Optimization & Scale (v0.8.x)

| Task | Package | Priority |
|------|---------|----------|
| Adaptive strategy selection (ML) | FluxIndex.Core | Medium |
| Batch enrichment pipeline | FluxIndex.SDK | Medium |
| Graph index optimization | FluxIndex.Stack | Medium |
| Benchmark suite | FluxIndex.Tests | High |

---

## 8. Performance Targets

### SDK Hybrid Tier (PostgreSQL)

| Operation | Target P50 | Target P99 | Notes |
|-----------|-----------|-----------|-------|
| Vector search (100K chunks) | < 30ms | < 80ms | pgvector HNSW |
| Graph traversal (2-hop) | < 50ms | < 150ms | Recursive CTE |
| Semantic cache hit | < 5ms | < 15ms | Redis |
| Full enrichment (1 doc) | < 10s | < 30s | Depends on AI calls |

### Stack Full Polyglot

| Operation | Target P50 | Target P99 | Notes |
|-----------|-----------|-----------|-------|
| Vector search (1M chunks) | < 15ms | < 50ms | Qdrant HNSW |
| Graph traversal (3-hop) | < 30ms | < 100ms | Neo4j native |
| Hybrid search | < 80ms | < 200ms | All stores |
| GraphRAG query | < 300ms | < 800ms | Community + vector |

---

## 9. Key Design Decisions

### Why Interface-First in SDK?

1. **Flexibility**: Consumers can implement any storage backend
2. **Testability**: Easy mocking for unit tests
3. **Evolution**: Stack can adopt new DBs without Core changes
4. **Simplicity**: Hybrid tier works for most use cases

### Why PostgreSQL Hybrid Tier?

1. **Single Service**: Reduces operational complexity
2. **pgvector Performance**: Competitive for < 1M vectors
3. **ACID Transactions**: Cross-concern consistency
4. **Graph Emulation**: CTEs handle 90% of graph queries
5. **Cost**: No additional infrastructure

### Why Dedicated DBs in Stack?

1. **Scale**: Qdrant/Neo4j designed for billions of records
2. **Performance**: Native indexing (HNSW, graph indexes)
3. **Features**: Full Cypher, sparse vectors, etc.
4. **Production**: Battle-tested at enterprise scale

---

*Document Version: 2.0*
*Updated: 2025-12-17*
*Authors: FluxIndex Team*
