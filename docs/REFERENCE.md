# FluxIndex Technical Reference

Architecture, retrieval mechanisms, and advanced topics for FluxIndex.

---

## Architecture

### Clean Architecture Overview

```
┌─────────────────────────────────────────────────────┐
│               SDK Layer (FluxIndex.SDK)             │
│            FluxIndexContext, Builder Pattern        │
│         FileFlux, WebFlux, FluxCurator              │
├─────────────────────────────────────────────────────┤
│              Storage Providers                      │
│  SQLite | PostgreSQL | Qdrant | Neo4j | Redis      │
├─────────────────────────────────────────────────────┤
│              Core (FluxIndex.Core)                  │
│   Domain + Application (AI Agnostic)               │
│   BM25, Graph Traversal, Quantization              │
└─────────────────────────────────────────────────────┘
```

### Storage Modes

FluxIndex supports three storage configurations:

| Mode | Setup | Storage Distribution |
|------|-------|---------------------|
| **Local** | `UseLocalStorage()` | SQLite → Vector + Graph + RDB + Cache |
| **Full** | `UseBestInClass()` | Qdrant(Vector) + Neo4j(Graph) + PostgreSQL(RDB+Cache) |
| **Custom** | Mix providers | User-defined distribution |

**Auto-Maximize Principle**: No feature toggles. Each provider contributes its capabilities automatically.

```
Provider Priority (same capability):
  Specialized (Qdrant/Neo4j) > General-purpose (PostgreSQL) > SQLite
```

### Core Interfaces

```csharp
// Vector storage (excerpt: also get, update, delete, count and document reassignment)
public interface IVectorStore
{
    Task<string> StoreAsync(DocumentChunk chunk, CancellationToken cancellationToken = default);
    Task<IEnumerable<DocumentChunk>> SearchAsync(
        float[] queryEmbedding, int topK = 10, float minScore = 0.0f,
        Dictionary<string, object>? filters = null, CancellationToken cancellationToken = default);
}

// Embedding generation (excerpt: also the query role, dimension, model name, identity)
public interface IEmbeddingService
{
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
    Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(
        IEnumerable<string> texts, CancellationToken cancellationToken = default);
}

// Reranking
public interface IReranker
{
    Task<IEnumerable<RerankResult>> RerankAsync(
        string query, IEnumerable<RetrievalCandidate> candidates,
        RerankOptions? options = null, CancellationToken cancellationToken = default);
    RerankModelInfo GetModelInfo();
}
```

#### Chunk identity

`DocumentChunk.Id` is a free string and every store honours it: what you store under is what you
read back, and what you pass to `GetAsync`/`DeleteAsync`/`ExistsAsync`. Leave it empty and the store
generates one, returns it, and writes it onto the chunk you passed — the instance you keep carries the
id the store answers to, in every backend alike.

Backends that cannot key on a string absorb that themselves rather than pushing it onto you. Qdrant
accepts only UUIDs or integers as point ids, and the PostgreSQL schema keys on `uuid`; both map a
non-UUID id to a deterministic UUID (`ChunkStorageId.ToStorageGuid`) and keep your original id
beside the row, so re-storing the same id is an update rather than a duplicate. An id that already
is a UUID is used verbatim, which is what keeps collections and tables written before this readable.
The SQLite stores key on the string itself (`vector_chunks.Id` and the vec0 `chunk_id` are TEXT), so no
mapping is involved there; re-storing an id updates the row, its vector and its FTS5 entry.

Practical consequence: you can move a corpus between SQLite, Qdrant and PostgreSQL without changing
how your application addresses chunks. Do not derive ids yourself to "help" a backend — that
defeats the round trip, since the store would then return the derived id rather than yours.

#### Replacing a document's rows (generation swap)

A pipeline that re-indexes a changed document replaces its rows as a *swap*: capture the ids the
document currently has, write the new generation, then delete `previous - attempted`. Each leg of a
hybrid index answers for its own rows — `IVectorStore.GetChunkIdsByDocumentIdAsync` for the vectors,
`IKeywordSearchService.GetChunkIdsByDocumentIdAsync` (since 0.39.0) for the keyword index. Drop the
superseded keyword rows with one `IKeywordSearchService.DeleteChunksAsync(ids)` call (since 0.40.0) rather
than `DeleteChunkAsync` per chunk — the relational indexes rewrite each shared term row once per call. Do not
read one leg's ids and delete on the other with them: nothing guarantees the two legs key their rows
identically (rows written before a store honoured caller ids never do), and a delete by an id the
other leg never held is a silent no-op that leaves the previous keyword generation searchable.

#### Moving a document to a new id (reassignment, since 0.64.0)

When a document's id — and with it the ids of its chunks — derives from something that changed, such as a file path,
the rows can be re-keyed instead of deleted and indexed again. Each leg does it with its own call and one shared map
from old to new chunk id:

```csharp
var moved = await vectorStore.ReassignDocumentAsync(oldDocId, newDocId, chunkIdMap,
    new Dictionary<string, object?> { ["source_path"] = newPath, ["stale_key"] = null }, ct);
await keywordSearch.ReassignDocumentAsync(oldDocId, newDocId, chunkIdMap, sameUpdates, ct);
await graphRag.ReassignChunksAsync(chunkIdMap, oldDocId, newDocId, partition, ct);
```

- Content, token counts and vectors stay as stored: nothing is embedded. On Qdrant and PostgreSQL a new chunk id is a
  new row key, so the store copies the stored vector to it.
- Every check happens before any write, and a failed check leaves the store unchanged: a chunk the store holds for the
  old document with no map entry throws `ArgumentException`; a target document that already has chunks, or a new chunk
  id already stored, throws `InvalidOperationException`. Map entries for ids a leg does not hold are ignored, so one
  map serves every leg.
- A metadata update with a null value removes the key. Keys a store writes itself follow the move (`chunkId`, a
  `documentId` equal to the old id, serialized chunk relationships).
- The SQL stores and the relational keyword indexes write in one transaction. Qdrant has none: it upserts the new
  points before deleting the old, so an interrupted move leaves the document present twice rather than not at all.
  The graph call is composed of graph-store upserts and can be repeated with the same map.

### Package Structure

| Package | Purpose |
|---------|---------|
| **FluxIndex.Core** | Domain models, BM25, graph traversal, quantization, abstract base classes |
| **FluxIndex.SDK** | FluxIndexContext, Retriever, Indexer, Builder pattern, FileFlux/WebFlux integration |
| **FluxIndex.Storage.SQLite** | SQLite (Vector + Graph + RDB + Cache) - Local mode |
| **FluxIndex.Storage.PostgreSQL** | PostgreSQL with pgvector (Vector + Graph + RDB + Cache) |
| **FluxIndex.Storage.Qdrant** | Qdrant vector database (specialized Vector) |
| **FluxIndex.Storage.Neo4j** | Neo4j graph database (specialized Graph) |
| **FluxIndex.Cache.Redis** | Redis-based semantic caching |
| **FluxIndex.Extensions.FileVault** | Git-like file tracking for RAG indexing |

### Storage Capabilities by Provider

| Provider | Vector | Graph | RDB | Cache | Best For |
|----------|:------:|:-----:|:---:|:-----:|----------|
| **SQLite** | ✓ | ✓ | ✓ | ✓ | Development, edge deployment |
| **PostgreSQL** | ✓ | ✓ | ✓ | ✓ | Production (single DB) |
| **Qdrant** | ✓ | - | - | - | High-performance vector search |
| **Neo4j** | - | ✓ | - | - | Complex graph queries |
| **Redis** | - | - | - | ✓ | Distributed caching |

---

## Retrieval Mechanisms

### Search Pipeline

```
User Query
     │
     ▼
┌────────────────────┐
│  Query Analysis    │ ← Complexity Detection
└────────────────────┘
     │
     ├──────────────────────────┐
     ▼                          ▼
┌────────────┐          ┌────────────┐
│   Vector   │          │   Sparse   │
│   Search   │          │   (BM25)   │
└────────────┘          └────────────┘
     │                          │
     └──────────┬───────────────┘
                ▼
┌────────────────────┐
│   Rank Fusion      │ ← RRF / Weighted Sum
└────────────────────┘
                │
                ▼
┌────────────────────┐
│    Reranking       │ ← Cross-Encoder (Optional)
└────────────────────┘
                │
                ▼
         Final Results
```

### Search Strategies

| Strategy | Description | Use Case |
|----------|-------------|----------|
| **Vector** | Semantic similarity search | Natural language queries |
| **Keyword (BM25)** | Term frequency matching | Exact term matching, code search |
| **Hybrid** | Vector + BM25 combined | General purpose |
| **Adaptive** | Auto-select by query | When unsure which is best |

### Fusion Methods

**Reciprocal Rank Fusion (RRF)** - Default
```
FinalScore = Σ 1/(k + rank)  where k = 60
```

**Weighted Sum**
```
FinalScore = α × vectorScore + (1-α) × sparseScore
```

### Configuration

```csharp
using FluxIndex.Core.Domain.Models;  // HybridSearchOptions, FusionMethod (IHybridSearchService)

// Hybrid search with custom weights — values you set are used as given
var options = new HybridSearchOptions
{
    FusionMethod = FusionMethod.WeightedSum,
    VectorWeight = 0.7,
    SparseWeight = 0.3
};
var results = await hybridSearch.SearchAsync(query, options);
// results[0].Fusion == AppliedFusion(WeightedSum, 0.7, 0.3, RrfK: 60, SelectedBy: Caller)
```

Leave any of the three unset (null) and the service chooses it per query — Dynamic Alpha Tuning when
`EnableDynamicAlphaTuning` is on and an `IDynamicFusionService` is registered, otherwise a query-length/term
heuristic. `AppliedFusion.SelectedBy` says which: `Caller`, `DynamicAlphaTuning`, `QueryHeuristic`, or
`ServiceDefault` (Qdrant's native hybrid fills unset values with RRF 0.7 / 0.3).

`HybridSearchOptions.TimeoutMs` bounds the whole search (fusion selection, both legs, fusion): past it the call throws
`TimeoutException` instead of returning what an interrupted leg left behind. Zero or less (the default) sets no limit;
cancelling your own token still throws `OperationCanceledException`. The vector leg's similarity metric is the store's,
fixed when its table or collection is created; keyword knobs (term expansion, phrase search, BM25 `K1`/`B`) live on
`SparseOptions`.

**SDK options that are read, and where.** `Indexer.IndexDocumentAsync(string content, …)` is the only
path on which the SDK splits text: it uses the builder's `IndexerOptions.ChunkSize`/`ChunkOverlap`
(`WithChunking(...)` or `WithIndexerOptions(...)`; characters at the nearest sentence/paragraph/word
boundary, overlap must be smaller than the size). The `Document` overloads index the chunks you pass. The
per-call `IndexingOptions` is read for `EnableGraphRAG`, `GraphRAGOptions` and `CustomOptions` (AI metadata
extraction via `WithAIMetadataExtraction(...)`, overlaid on `IndexerOptions.CustomOptions`) — those are all
its fields (the unread `GenerateEmbeddings`/`ExtractMetadata` were removed in 0.66.0). On the search side `SearchOptions.UseHybridSearch` auto-detects, `UseGraphRAG = true` throws (see
*Full GraphRAG*; graph query options go to `IGraphRAGService.QueryAsync`), and `IncludeVectors` is not read —
`SearchResult` has no vector field. When a search
method's `maxResults`/`minScore` argument is omitted, `RetrieverOptions.DefaultMaxResults`/`DefaultMinScore`
(set by `WithSearchOptions(...)`; 10 / 0.2 by default) apply — `FindSimilarAsync` (0.5) and the quantized
searches (0.0) keep their own threshold defaults.

**Semantic cache (opt-in).** `FluxIndexContext.SearchAsync` consults the registered `ISemanticCacheService`, and none is
registered unless you opt in — `UseSQLite`/`UsePostgreSQL`/`UseLocalStorage`/`UseBestInClass` do not. Opt in with
`WithSemanticCacheOptions(o => o.Provider = "SQLite")` or `"PostgreSQL"` (the storage package's `Add*Storage()` then
registers its cache on the selected store's database); Redis (`AddRedisSemanticCache`) or your own implementation is
registered through `ConfigureServices`, with `Provider` `"Redis"` or `"None"`. `Build()` throws for an opted-in provider
nobody registered and for two registrations. Matching is approximate: a query at least `SimilarityThreshold` (0.95)
similar to an earlier one gets that query's results, so two different questions can share an answer. A search without a filter is answered from the results
stored for the most similar earlier query when that query asked for at least as many results with a minimum score no
higher (the hit is trimmed to the request); otherwise it searches and stores its results. Filtered searches neither read
nor fill the cache, every indexer write empties it (`ClearCacheAsync`), a failing cache is skipped rather than failing
the search, and a keyword-only context (no embedder) never caches. Cached results keep chunk metadata (as plain values)
but carry no vector/keyword sub-scores or highlights.

**Semantic cache threshold.** `SemanticCacheOptions.SimilarityThreshold` (`WithSemanticCacheOptions(...)`) is the minimum
query similarity for a cache hit: `FluxIndexContext.SearchAsync` passes it to the cache, and the builder copies it into the
SQLite/PostgreSQL cache options. Unset, the context passes none and the cache's own `SimilarityThreshold` decides — 0.95
for the SQLite, PostgreSQL and Redis caches.

---

## Local reranking (LMSupply)

Cross-encoder reranking runs in-process through the `FluxIndex.Providers.LMSupply` package, which wraps
`LMSupply.Reranker`. The model is loaded lazily — downloaded first when it is not cached — on first use,
or at host start when `WarmUpOnStart` is set; building the container never blocks on it.

### Setup

```csharp
using FluxIndex.Providers.LMSupply.Extensions;
using FluxIndex.SDK;
using FluxIndex.Storage.SQLite;

var context = FluxIndexContext.CreateBuilder()
    .UseSQLite("fluxindex.db")
    .AddSQLiteStorage()
    .ConfigureServices(s => s.AddLMSupplyReranker("quality"))   // alias or model id
    .Build();
```

### Model aliases

`auto` (the default), `default`, `fast`, `quality`, `large`, `multilingual` — the presets `LMSupply.Reranker`
resolves; a model id or a local path works as well. Sizes and speed depend on the LMSupply catalog version you have pinned.

`auto` picks by hardware tier: `default` on low-spec machines, `quality` on mid-range, `multilingual` above. `default`,
`fast` and `ms-marco-l12` are **English-only** — over a non-English query they rank an unrelated passage in the query's
language above one in another language that answers it. For a non-English or mixed-language corpus on a low-spec
machine, name `quality` explicitly.

### Configuration

```csharp
using FluxIndex.Providers.LMSupply.Extensions;
using LMSupply.Reranker;  // RerankerOptions

services.AddLMSupplyReranker(options =>
{
    options.ModelId = "quality";
    options.WarmUpOnStart = true;                 // load at host start instead of first use
    options.LoadTimeout = TimeSpan.FromMinutes(5); // null = no timeout
    options.Reranker = new RerankerOptions          // LMSupply loader options
    {
        MaxSequenceLength = 512,
        BatchSize = 32
    };
});
```

Failure to load or download the model surfaces on the first rerank call (or at start-up with
`WarmUpOnStart`) as an exception; there is no silent algorithmic fallback.

---

## Vector Quantization

Memory optimization through vector compression.

### Quantization Types

| Type | Compression | Recall | Speed | Use Case |
|------|-------------|--------|-------|----------|
| **Scalar Int8** | 4x | ~73% | 2x | General search |
| **Scalar Int4** | 8x | ~65% | 3x | Balance |
| **Binary** | 32x | ~54% | 25x | Candidate filtering |
| **Product (PQ)** | 16-64x | ~70-80% | 5-10x | Memory constrained |

### Setup

```csharp
using FluxIndex.Core.Application.Services;

// Scalar quantization (recommended start)
services.AddScalarQuantization(dimension: 1536);

// Binary (maximum compression)
services.AddBinaryQuantization(dimension: 1536);

// Product quantization
services.AddProductQuantization(
    dimension: 1536,
    numSubvectors: 8,
    codebookSize: 256);

services.AddQuantizedVectorStoreDecorator(autoQuantize: true);
```

### Two-Stage Search

```csharp
using FluxIndex.Core.Domain.Models;

var options = new HybridSearchOptions
{
    UseQuantizedSearch = true,
    QuantizedCandidateMultiplier = 3,  // TopK * 3 candidates
};
```

1. **Stage 1**: Fast approximate search on quantized vectors
2. **Stage 2**: Rerank candidates with original vectors

### Migration

```csharp
using FluxIndex.Core.Application.Services.Quantization;

var migrationService = serviceProvider.GetRequiredService<VectorQuantizationMigrationService>();

var result = await migrationService.MigrateAllAsync(
    new MigrationOptions { BatchSize = 100 },
    progress: new Progress<MigrationProgress>(p =>
        Console.WriteLine($"Progress: {p.ProcessedCount}")));
```

---

## Graph Traversal

Document relationship navigation for multi-hop reasoning.

### Algorithms

```csharp
var graphService = serviceProvider.GetRequiredService<IGraphTraversalService>();

// BFS traversal
var neighbors = await graphService.TraverseBfsAsync(
    startChunkId: "chunk-123",
    new GraphTraversalOptions { MaxDepth = 3, MaxNodes = 100 });

// Shortest path
var path = await graphService.FindShortestPathAsync(startId, endId);

// PageRank importance (chunk id → score)
var importance = await graphService.ComputeChunkImportanceAsync();
```

### Available Operations

- **BFS/DFS Traversal**: Navigate document relationships
- **Dijkstra Shortest Path**: Find minimum hops between chunks
- **Connected Components**: Identify document clusters
- **Cycle Detection**: Find circular references
- **PageRank**: Calculate chunk importance

---

## Advanced RAG Services

Self-correction and agentic retrieval capabilities.

### Self-RAG

Self-reflective RAG with iterative quality improvement.

```csharp
var selfRag = serviceProvider.GetRequiredService<ISelfRAGService>();

var result = await selfRag.SearchAsync(query, new SelfRAGOptions
{
    MaxIterations = 3,
    QualityThreshold = 0.7f,
    EnableAutoRefinement = true
});

// Result contains FinalResults, FinalQualityScore, Iterations
```

### Corrective RAG

Document grading and knowledge refinement with web augmentation.

```csharp
var crag = serviceProvider.GetRequiredService<ICorrectiveRAGService>();

var result = await crag.RetrieveWithCorrectionAsync(query, new CorrectiveRAGOptions
{
    EnableWebSearch = true,
    CorrectThreshold = 0.7f,
    AmbiguousThreshold = 0.4f,
    RetryCount = 2
});

// Documents are graded as Correct, Ambiguous, or Incorrect
```

### Agentic Retrieval Router

Intelligent strategy selection based on query analysis.

```csharp
var router = serviceProvider.GetRequiredService<IAgenticRetrievalRouter>();

// Automatic strategy selection
var result = await router.RouteAndRetrieveAsync(query, new RoutingContext
{
    MaxResults = 10,
    Domain = "technical"
});

// Or analyze query first
var decision = await router.AnalyzeQueryAsync(query);
// decision.PrimaryStrategy, decision.QueryAnalysis.Type
```

**Supported Strategies**:
- SemanticSearch, KeywordSearch, HybridSearch
- MultiHopRetrieval, SelfRAG, CorrectiveRAG
- SmallToBig, GraphTraversal, IterativeRetrieval
- QueryDecomposition, Ensemble

---

## GraphRAG Pipeline

Entity-centric retrieval and hierarchical summarization.

### Entity Extraction

```csharp
var extractor = serviceProvider.GetRequiredService<IAdvancedEntityExtractionService>();

var entities = await extractor.ExtractEntitiesAsync(content);
var relations = await extractor.ExtractRelationsAsync(content, entities);
// or both at once: await extractor.ExtractEntityGraphAsync(content)
```

### Entity Graph Service

```csharp
var entityGraph = serviceProvider.GetRequiredService<IEntityGraphService>();

// Build the graph from chunks; every query below takes the result explicitly
EntityGraphResult graph = await entityGraph.BuildEntityGraphAsync(chunks);

// Entity-centric search with Personalized PageRank (the query's entities are the seeds)
EntitySearchResult search = await entityGraph.SearchByEntitiesAsync(query, graph,
    new EntitySearchOptions { TopK = 10, DampingFactor = 0.85, IncludeExplanation = true });

// Multi-hop traversal from named entities
EntityTraversalResult traversal = await entityGraph.TraverseEntityRelationsAsync(
    ["Microsoft"], graph, new EntityTraversalOptions { MaxHops = 3 });

// Entity importance (PPR), optionally personalised to seed entities
IReadOnlyDictionary<string, double> importance =
    await entityGraph.ComputeEntityImportanceAsync(graph, seedEntities: ["Microsoft"]);
```

| Method | Options | Returns |
|---|---|---|
| `BuildEntityGraphAsync(chunks, EntityGraphBuildOptions?)` | extraction and mapping settings | `EntityGraphResult` — entities, relations, entity→chunk mappings |
| `SearchByEntitiesAsync(query, graph, EntitySearchOptions?)` | `TopK`, `DampingFactor`, `MaxIterations`, `MinScore`, `PriorityEntityTypes` | `EntitySearchResult` — `Hits` (chunk, `Score`, `PprScore`), `QueryEntities`, `RelatedEntities` |
| `TraverseEntityRelationsAsync(startEntities, graph, EntityTraversalOptions?)` | `MaxHops`, `MaxEntitiesPerHop`, `RelationTypes`, `MinRelationStrength` | `EntityTraversalResult` — `EntitiesByHop`, paths |
| `ComputeEntityImportanceAsync(graph, seedEntities?, PersonalizedPageRankOptions?)` | `DampingFactor`, `MaxIterations`, `ConvergenceThreshold` | entity id → score |

**Stored extractions are reused.** With a graph store registered, `BuildEntityGraphAsync` looks up
the entities already persisted for the chunks it is given (by chunk id) and reconstitutes them —
entities, provenance, relationships — instead of extracting those chunks again; only chunks the
store has nothing for go to the extractor, and an entity extracted again is joined to its stored
counterpart (same normalized name and type) so provenance accumulates on one entity. Building the
same chunks twice costs one round of extraction. `EntityGraphStats.ChunksReused` /
`ChunksExtracted` say what happened; `EntityGraphBuildOptions.ReuseStoredExtractions = false`
turns it off. This depends on chunk ids being stable across builds — a consumer that mints a fresh
id per run reuses nothing. A chunk that was extracted before but yielded no entity leaves no trace
and is extracted again.

**What the extractor is handed, and what it must return.** The build calls
`IAdvancedEntityExtractionService.ExtractBatchAsync` once per batch of `EntityGraphBuildOptions.BatchSize`
chunks (default 10, contiguous). The options it passes start from
`EntityGraphBuildOptions.ExtractionOptions` — the extractor-side configuration (`UseLlm`, `Language`,
`CustomPatterns`, `IncludeContext`, …); `GraphRAGBuildOptions.EntityOptions` lands there when you go
through `IGraphRAGService` — with the build's own knobs laid over: `MinEntityConfidence`,
`MaxEntitiesPerChunk` and `ExtractRelations` always, `EntityTypes` when set. The extractor must return
**exactly one `EntityGraph` per input, in input order** — provenance (`GraphEntity.ChunkIds`) is joined by
position, and a shorter result is rejected rather than leaving chunks silently without entities. Whether
a batch is extracted text by text or resolved as one set is the extractor's choice; an entity present
in several inputs appears in each input's graph and is merged by normalized name, type **and declared
`Subtype`** — two `Custom` subtypes sharing a name are two nodes, each keeping its label. Anything the
extractor puts in `ExtractedEntity.Metadata` (and `Subtype`, as `"subtype"`) is kept on the node's
`Properties` and persisted; values read back from a store are the primitives the build wrote, not JSON
elements. A query entity that declares no subtype matches every subtype of its name and type.

**What the default extractor does with its options.** `Language` (free-form, e.g. `"ko"`) is a hint
to the LLM: the prompt names the language and asks for entity text exactly as written, so names on a
Korean corpus come back Korean rather than transliterated — the parser matches returned text back into
the chunk, and a translated name matches nothing. Pattern extraction is language-independent.
`CustomPatterns` maps a subtype to a .NET regular expression; every match is emitted as
`NamedEntityType.Custom` with that subtype, at pattern confidence, and passes through the `EntityTypes`
filter (which must include `Custom`) and `MinConfidence` like the built-in patterns. An expression that
does not parse throws naming its key.

```csharp
var options = new EntityExtractionOptions
{
    Language = "ko",
    CustomPatterns = new() { ["ticket"] = @"\bT-\d{4}\b", ["desk"] = @"\bDESK-[A-Z]+\b" }
};
```

**Query-time switches.** `GraphRAGQueryOptions.IncludeContext = false` returns `Documents` without
their chunk text (ids, scores, sources and entity links only — the answer is still generated from the
full text). `IncludeRelationships` controls whether the relationships the local search traversed are
returned in `GraphRAGQueryResult.Relationships` (local and hybrid scope; global scope has none).
`IncludeCommunityContext = false` keeps community summaries out of the answer context and out of
`RelatedCommunities`; global scope still retrieves community-derived documents.

### Hierarchical Summarization

```csharp
var summarizer = serviceProvider.GetRequiredService<IHierarchicalSummarizationService>();

// Generate community summaries for a hierarchy from ILeidenCommunityService and the chunks it grouped
var summaries = await summarizer.GenerateHierarchicalSummariesAsync(hierarchy, chunks,
    new HierarchicalSummarizationOptions { LevelsToSummarize = [1] });

// Global search using community summaries
var answer = await summarizer.GlobalSearchAsync(query, summaries, new GlobalSearchOptions
{
    MaxCommunities = 5,
    SearchLevel = 1
});
```

### Full GraphRAG

Graph retrieval runs against an index built from a known set of chunks, so it is a two-step API on
`IGraphRAGService` — it is **not** performed by `Retriever.SearchAsync` (setting
`SearchOptions.UseGraphRAG = true` there throws rather than silently searching without the graph).

```csharp
var graphRag = serviceProvider.GetRequiredService<IGraphRAGService>();

// Build once (the SDK indexer does this for you when EnableGraphRAG is on) or load a persisted graph
var index = await graphRag.BuildIndexAsync(chunks, new GraphRAGBuildOptions());
// var index = await graphRag.LoadIndexAsync(chunks);
// Several tenants in one graph store: new GraphRAGBuildOptions { Partition = "tenant-a" } (and GraphRAGLoadOptions.Partition)
// — see ADVANCED_RAG.md «Partitions».

var result = await graphRag.QueryAsync(query, index, new GraphRAGQueryOptions
{
    ForceScope = QueryScope.Hybrid,
    IncludeRelationships = true
});
```

---

## Query Enhancement

Dynamic fusion.

### Dynamic Fusion

Query-type specific weight optimization.

```csharp
var dynamicFusion = serviceProvider.GetRequiredService<IDynamicFusionService>();

var fusion = await dynamicFusion.CalculateDynamicWeightsAsync(query);
// DynamicFusionConfiguration: VectorWeight and SparseWeight chosen from the query's type
```

**Base Weights by Query Type** (then shifted for detected technical domains and complexity, and normalized):
| Query Type | Vector Weight | Sparse Weight |
|------------|---------------|---------------|
| SimpleKeyword | 0.35 | 0.65 |
| NaturalQuestion | 0.70 | 0.30 |
| ComplexSearch | 0.45 | 0.55 |
| ReasoningQuery | 0.80 | 0.20 |
| ComparisonQuery | 0.55 | 0.45 |
| TemporalQuery | 0.60 | 0.40 |
| MultiHopQuery | 0.75 | 0.25 |

---

## Custom Implementations

### Custom Embedding Service

Derive from `EmbeddingServiceBase`: it handles empty input, the query path, batch fallback, token
estimates and `GetIdentity()`, leaving the embedding call and the three members that identify the vector
space ([AI Provider Integration](./AI_PROVIDER_INTEGRATION.md) has complete provider examples).

```csharp
using FluxIndex.Core.Application.Services.Base;

public class CustomEmbeddingService : EmbeddingServiceBase
{
    // Embedding of stored text (documents, chunks)
    protected override Task<float[]> EmbedCoreAsync(string text, CancellationToken cancellationToken)
        => YourEmbeddingProvider.EmbedAsync(text, cancellationToken);

    // Provider + model + dimension identify the vector space
    public override int GetEmbeddingDimension() => 1024;
    public override string GetModelName() => "your-model";
    protected override string GetProviderName() => "YourProvider";

    // Optional: EmbedQueryCoreAsync for an asymmetric model's query convention,
    // GenerateEmbeddingsBatchAsync for a native batch call
}
```

### Custom Vector Store

Derive from `VectorStoreBase`: it validates chunks, applies `minScore`, sorts results and re-checks
metadata filters, and implements the rest of `IVectorStore` on top of these core calls.

```csharp
using FluxIndex.Core.Application.Services.Base;
using FluxIndex.Core.Application.Utilities;  // ScoredChunk

public class CustomVectorStore : VectorStoreBase
{
    protected override async Task<string> StoreCoreAsync(DocumentChunk chunk, CancellationToken cancellationToken)
    {
        // Chunk identity: keep the caller's id; generate one only when it is empty, and write it back
        if (string.IsNullOrEmpty(chunk.Id))
            chunk.Id = Guid.NewGuid().ToString();
        await YourVectorDatabase.UpsertAsync(chunk, cancellationToken);  // Pinecone, Chroma, etc.
        return chunk.Id;
    }

    protected override async Task<IEnumerable<ScoredChunk>> SearchCoreAsync(
        float[] queryEmbedding, int topK, Dictionary<string, object>? filters, CancellationToken cancellationToken)
    {
        // Apply the filters in the database when it can, so matches are not crowded out of the top K
        var hits = await YourVectorDatabase.QueryAsync(queryEmbedding, topK, filters, cancellationToken);
        return hits.Select(h => new ScoredChunk(h.Chunk, h.Score));
    }

    protected override Task<DocumentChunk?> GetCoreAsync(string id, CancellationToken cancellationToken)
        => YourVectorDatabase.GetAsync(id, cancellationToken);

    protected override async Task<bool> UpdateCoreAsync(DocumentChunk chunk, CancellationToken cancellationToken)
    {
        await YourVectorDatabase.UpsertAsync(chunk, cancellationToken);
        return true;
    }

    protected override Task<bool> DeleteCoreAsync(string id, CancellationToken cancellationToken)
        => YourVectorDatabase.DeleteAsync(id, cancellationToken);

    protected override Task<IEnumerable<DocumentChunk>> GetByDocumentIdCoreAsync(string documentId, CancellationToken cancellationToken)
        => YourVectorDatabase.GetByDocumentIdAsync(documentId, cancellationToken);

    protected override Task<bool> DeleteByDocumentIdCoreAsync(string documentId, CancellationToken cancellationToken)
        => YourVectorDatabase.DeleteByDocumentIdAsync(documentId, cancellationToken);

    protected override Task<int> CountCoreAsync(CancellationToken cancellationToken)
        => YourVectorDatabase.CountAsync(cancellationToken);

    protected override Task ClearCoreAsync(CancellationToken cancellationToken)
        => YourVectorDatabase.ClearAsync(cancellationToken);
}
```

### Custom Reranker

Derive from `RerankerBase`: it trims candidate content, maps scores back to `RerankResult` and applies
`ScoreThreshold`, leaving the scoring call and the model description.

```csharp
using FluxIndex.Core.Application.Services.Base;

public class CustomRerankerService : RerankerBase
{
    // (original index, score) pairs, most relevant first
    protected override async Task<IEnumerable<(int Index, float Score)>> RerankCoreAsync(
        string query, IReadOnlyList<string> documents, int topN, CancellationToken cancellationToken)
    {
        var scores = await YourRerankerProvider.ScoreAsync(query, documents, cancellationToken);
        return scores
            .Select((score, index) => (Index: index, Score: score))
            .OrderByDescending(x => x.Score)
            .Take(topN);
    }

    public override RerankModelInfo GetModelInfo() => new()
    {
        Name = "your-reranker",
        Type = RerankModel.Custom,
        RequiresApiKey = false
    };
}
```

---

## Testing

### Test Modes

| Mode | File Required | API | Cost | Speed |
|------|---------------|-----|------|-------|
| **Mock** | No `.env.local` | Mock | Free | Fast |
| **Real API** | `.env.local` | OpenAI | Paid | Slow |

### Running Tests

```powershell
# Mock mode (also what CI runs)
pwsh scripts/test.ps1

# Real API mode (local)
cp .env.local.example .env.local
# Edit .env.local with your API key
pwsh scripts/full-test.ps1

# With coverage
pwsh scripts/test.ps1 -Coverage
```

Both scripts run the same thing — `full-test.ps1` reports whether `.env.local` is present and then
delegates — so the runner's behaviour is defined in one place. **Both CI workflows invoke
`test.ps1` too**: the pull-request run and the release gate share this one definition of "the tests
pass", which is also why a local run is a faithful preview of CI rather than an approximation.

The runner **discovers** every `tests/**/*.Tests.csproj` rather than reading a fixed list, and
requires all of them to pass. Two categories are excluded because their exclusion holds on any
machine:

| Category | Why it is excluded |
|---|---|
| `Integration` | Needs an external service (Testcontainers/Docker, a live database) |
| `Performance` | Asserts on wall-clock time, which a shared runner cannot make meaningful |

Mark a test with `[Trait("Category", "...")]` to place it in either. Excluding by category rather
than by project matters for the case a project list cannot express: a service-dependent test living
inside an otherwise self-contained project.

To run an excluded category deliberately:

```powershell
dotnet test --filter "Category=Performance"
```

### Test Fixture Pattern

```csharp
using FluxIndex.SDK;
using FluxIndex.Storage.SQLite;
using Xunit;

[Fact]
public async Task KeywordSearchAsync_ValidQuery_ReturnsResults()
{
    // No embedder: a keyword-only context, which is all this test needs
    await using var context = FluxIndexContext.CreateBuilder()
        .UseSQLiteInMemory()
        .AddSQLiteStorage()
        .Build();

    await context.Indexer.IndexDocumentAsync("test content", "doc-1");
    var results = await context.Retriever.KeywordSearchAsync("test");

    Assert.Single(results);
}
```

---

## Performance Benchmarks

Based on .NET 10.0, Intel i7-1360P:

| Operation | Size | Time |
|-----------|------|------|
| Batch Indexing | 1K chunks | 24ms |
| Batch Indexing | 10K chunks | 188ms |
| Vector Search | 1K chunks | 0.6-0.7ms |
| Hybrid Search | 100 chunks | 383ms avg |
| Embedding Cache Hit | Repeated | 0ms |
| Semantic Cache Hit | Similar | <5ms |

### Optimization Summary

- Embedding cache: 100% improvement for exact matches
- Semantic cache: ~95% improvement for similar queries
- Optimal parallelism: 8 threads for batch indexing
- Quantization: 4-32x memory reduction

---

## Next Steps

- [Guide](GUIDE.md) - Quick start and examples
- [Samples](../samples/) - Working code
- [GitHub](https://github.com/iyulab/FluxIndex) - Issues & contributions
