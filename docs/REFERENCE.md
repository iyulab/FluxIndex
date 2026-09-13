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
// Vector storage
public interface IVectorStore
{
    Task StoreAsync(DocumentChunk chunk, CancellationToken ct);
    Task<IEnumerable<DocumentChunk>> SearchAsync(
        EmbeddingVector queryVector, int topK, CancellationToken ct);
}

// Embedding generation
public interface IEmbeddingService
{
    Task<EmbeddingVector> GenerateEmbeddingAsync(string text, CancellationToken ct);
    Task<IEnumerable<EmbeddingVector>> GenerateBatchEmbeddingsAsync(
        IEnumerable<string> texts, CancellationToken ct);
}

// Reranking
public interface IReranker
{
    Task<IEnumerable<RerankResult>> RerankAsync(
        string query, IEnumerable<RetrievalCandidate> candidates,
        RerankOptions? options = null, CancellationToken ct = default);
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
// Hybrid search with custom weights
var options = new HybridSearchOptions
{
    FusionMethod = FusionMethod.WeightedSum,
    VectorWeight = 0.7,
    SparseWeight = 0.3
};
```

**SDK options that are read, and where.** `Indexer.IndexDocumentAsync(string content, …)` is the only
path on which the SDK splits text: it uses the builder's `IndexerOptions.ChunkSize`/`ChunkOverlap`
(`WithChunking(...)` or `WithIndexerOptions(...)`; characters at the nearest sentence/paragraph/word
boundary, overlap must be smaller than the size). The `Document` overloads index the chunks you pass. The
per-call `IndexingOptions` is read for `EnableGraphRAG`, `GraphRAGOptions` and `CustomOptions` (AI metadata
extraction via `WithAIMetadataExtraction(...)`, overlaid on `IndexerOptions.CustomOptions`); its
`ChunkingStrategy`/`MaxChunkSize`/`OverlapSize`/`GenerateEmbeddings`/`ExtractMetadata`/`EnableOCR` are not
read. On the search side `SearchOptions.UseHybridSearch` auto-detects, `UseGraphRAG = true` throws (see
*Full GraphRAG*), and `IncludeVectors` is not read — `SearchResult` has no vector field. When a search
method's `maxResults`/`minScore` argument is omitted, `RetrieverOptions.DefaultMaxResults`/`DefaultMinScore`
(set by `WithSearchOptions(...)`; 10 / 0.2 by default) apply — `FindSimilarAsync` (0.5) and the quantized
searches (0.0) keep their own threshold defaults.

---

## Local reranking (LMSupply)

Cross-encoder reranking runs in-process through the `FluxIndex.Providers.LMSupply` package, which wraps
`LMSupply.Reranker`. The model is loaded lazily — downloaded first when it is not cached — on first use,
or at host start when `WarmUpOnStart` is set; building the container never blocks on it.

### Setup

```csharp
var context = FluxIndexContext.CreateBuilder()
    .UseSQLite("fluxindex.db")
    .AddSQLiteStorage()
    .ConfigureServices(s => s.AddLMSupplyReranker("quality"))   // alias or model id
    .Build();
```

### Model aliases

`default`, `fast`, `quality`, `multilingual` — the presets `LMSupply.Reranker` resolves; a model id or a
local path works as well. Sizes and speed depend on the LMSupply catalog version you have pinned.

### Configuration

```csharp
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
// BFS traversal
var neighbors = await graphService.TraverseBfsAsync(
    startChunkId: "chunk-123",
    maxDepth: 3,
    maxNodes: 100);

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
var extractor = serviceProvider.GetRequiredService<IEntityExtractionService>();

var entities = await extractor.ExtractEntitiesAsync(content);
var relations = await extractor.ExtractRelationsAsync(content, entities);
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
in several inputs appears in each input's graph and is merged by normalized name and type. Anything the
extractor puts in `ExtractedEntity.Metadata` (and `Subtype`, as `"subtype"`) is kept on the node's
`Properties` and persisted.

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

// Generate community summaries
var summaries = await summarizer.GenerateSummariesAsync(communities, level: 1);

// Global search using community summaries
var answer = await summarizer.GlobalSearchAsync(query, new GlobalSearchOptions
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
var index = await graphRag.BuildIndexAsync(chunks, new GraphRAGBuildOptions { GenerateEntityEmbeddings = true });
// var index = await graphRag.LoadIndexAsync(chunks);

var result = await graphRag.QueryAsync(query, index, new GraphRAGQueryOptions
{
    ForceScope = QueryScope.Hybrid,
    IncludeRelationships = true
});
```

---

## Query Enhancement

Dynamic fusion and query transformation.

### Dynamic Fusion

Query-type specific weight optimization.

```csharp
var dynamicFusion = serviceProvider.GetRequiredService<IDynamicFusionService>();

var fusion = await dynamicFusion.CalculateDynamicWeightsAsync(query);
// DynamicFusionConfiguration: VectorWeight and SparseWeight chosen from the query's type
```

**Default Weights by Query Type**:
| Query Type | Vector Weight | Sparse Weight |
|------------|---------------|---------------|
| Factual | 0.3 | 0.7 |
| Analytical | 0.7 | 0.3 |
| Exploratory | 0.8 | 0.2 |
| Procedural | 0.5 | 0.5 |

### Query Transformation

```csharp
var transformer = serviceProvider.GetRequiredService<IQueryTransformationService>();

// HyDE (Hypothetical Document Embedding)
var hyde = await transformer.GenerateHypotheticalDocumentAsync(query);

// Query decomposition for complex questions
var subQueries = await transformer.DecomposeQueryAsync(complexQuery);

// Intent analysis (drives the dynamic fusion weights above)
var intent = await transformer.AnalyzeQueryIntentAsync(query);
```

---

## Custom Implementations

### Custom Embedding Service

```csharp
public class CustomEmbeddingService : IEmbeddingService
{
    public async Task<EmbeddingVector> GenerateEmbeddingAsync(
        string text, CancellationToken ct)
    {
        var values = await YourModel.EmbedAsync(text);
        return new EmbeddingVector(values);
    }

    public async Task<IEnumerable<EmbeddingVector>> GenerateBatchEmbeddingsAsync(
        IEnumerable<string> texts, CancellationToken ct)
    {
        // Batch embedding for better performance
    }
}
```

### Custom Vector Store

```csharp
public class CustomVectorStore : IVectorStore
{
    public async Task StoreAsync(DocumentChunk chunk, CancellationToken ct)
    {
        // Pinecone, Qdrant, etc.
    }

    public async Task<IEnumerable<DocumentChunk>> SearchAsync(
        EmbeddingVector queryVector, int topK, CancellationToken ct)
    {
        // Your vector search
    }
}
```

### Custom Reranker

```csharp
public class CustomRerankerService : IReranker
{
    public async Task<IEnumerable<RerankResult>> RerankAsync(
        string query, IEnumerable<RetrievalCandidate> candidates,
        RerankOptions? options = null, CancellationToken ct = default)
    {
        // Your reranking logic
    }
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
[Fact]
public async Task SearchAsync_ValidQuery_ReturnsResults()
{
    var context = FluxIndexContext.CreateBuilder()
        .UseSQLiteInMemory()
        .AddSQLiteStorage()
        .Build();

    await context.Indexer.IndexDocumentAsync("test content", "doc-1");
    var results = await context.Retriever.SearchAsync("test");

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
- [Benchmarks](../benchmarks/FluxIndex.Benchmarks/BENCHMARK_RESULTS.md) - Full metrics
- [GitHub](https://github.com/iyulab/FluxIndex) - Issues & contributions
