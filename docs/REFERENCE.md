# FluxIndex Technical Reference

Architecture, retrieval mechanisms, and advanced topics for FluxIndex.

---

## Architecture

### Clean Architecture Overview

```
┌─────────────────────────────────────────────────────┐
│               SDK Layer (FluxIndex.SDK)             │
│            FluxIndexContext, Builder Pattern        │
│         LMSupply, FileFlux, WebFlux, FluxCurator    │
├─────────────────────────────────────────────────────┤
│              Provider Packages (Optional)           │
│  FluxIndex.Storage.*    FluxIndex.Cache.Redis       │
│  FluxIndex.Extensions.FileVault                     │
├─────────────────────────────────────────────────────┤
│              Core (FluxIndex.Core)                  │
│         Domain + Application + Infrastructure       │
└─────────────────────────────────────────────────────┘
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

### Package Structure

| Package | Purpose |
|---------|---------|
| **FluxIndex.Core** | Domain models, BM25, local reranking, graph traversal, quantization |
| **FluxIndex.SDK** | FluxIndexContext, Retriever, Indexer, Builder pattern (includes LMSupply, FileFlux, WebFlux, FluxCurator, FluxImprover) |
| **FluxIndex.Storage.SQLite** | SQLite with vector extension |
| **FluxIndex.Storage.PostgreSQL** | PostgreSQL with pgvector |
| **FluxIndex.Storage.Neo4j** | Neo4j graph database |
| **FluxIndex.Storage.Qdrant** | Qdrant vector database |
| **FluxIndex.Cache.Redis** | Redis-based semantic caching |
| **FluxIndex.Extensions.FileVault** | Git-like file tracking for RAG indexing |

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

---

## LocalReranker

Cross-encoder neural reranking for improved relevance.

### Setup

```csharp
// Resilient adapter with fallback (recommended)
var context = FluxIndexContext.CreateBuilder()
    .UseResilientLocalReranker(options =>
    {
        options.ModelId = "quality";  // "fast", "quality", "multilingual"
    })
    .Build();
```

### Model Options

| Model | Size | Multilingual | Speed | Quality |
|-------|------|--------------|-------|---------|
| `fast` | ~25MB | No | ★★★★★ | ★★★ |
| `quality` | ~100MB | No | ★★★ | ★★★★★ |
| `multilingual` | ~280MB | Yes | ★★ | ★★★★ |

### Configuration

```csharp
var options = new LocalRerankerOptions
{
    ModelId = "quality",
    MaxSequenceLength = 512,
    UseGpu = false,
    BatchSize = 32,
    WarmupOnStartup = true
};
```

### Resilient Mode Behavior

```
Startup:
├─ Model load success → Semantic mode (high quality)
└─ Model load failure → Algorithmic mode (fallback)

Runtime:
├─ Inference success → Return results
└─ Inference failure → Algorithmic fallback → Return results
```

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

// PageRank importance
var importance = await graphService.CalculateChunkImportanceAsync(
    iterations: 20,
    dampingFactor: 0.85);
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
    MinQualityThreshold = 0.7f,
    EnableReflection = true
});

// Result contains FinalResults, FinalQualityScore, Iterations
```

### Corrective RAG

Document grading and knowledge refinement with web augmentation.

```csharp
var crag = serviceProvider.GetRequiredService<ICorrectiveRAGService>();

var result = await crag.RetrieveWithCorrectionAsync(query, new CorrectiveRAGOptions
{
    EnableWebAugmentation = true,
    GradingThreshold = 0.5f,
    MaxCorrections = 2
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

// Build graph from documents
await entityGraph.BuildGraphAsync(documents);

// Entity-centric search with Personalized PageRank
var results = await entityGraph.SearchByEntityAsync(entityId, new EntitySearchOptions
{
    MaxDepth = 3,
    DampingFactor = 0.85
});
```

### Hierarchical Summarization

```csharp
var summarizer = serviceProvider.GetRequiredService<IHierarchicalSummarizationService>();

// Generate community summaries
var summaries = await summarizer.GenerateSummariesAsync(communities, level: 1);

// Global search using community summaries
var answer = await summarizer.GlobalSearchAsync(query, new GlobalSearchOptions
{
    MaxCommunities = 5,
    SummaryLevel = 1
});
```

### Full GraphRAG

```csharp
var graphRag = serviceProvider.GetRequiredService<IGraphRAGService>();

var result = await graphRag.SearchAsync(query, new GraphRAGOptions
{
    EnableLocalSearch = true,
    EnableGlobalSearch = true,
    CommunityLevel = 1
});
```

---

## Query Enhancement

Dynamic fusion and query transformation.

### Dynamic Fusion

Query-type specific weight optimization.

```csharp
var dynamicFusion = serviceProvider.GetRequiredService<IDynamicFusionService>();

var weights = dynamicFusion.CalculateWeights(queryAnalysis);
// Returns optimized VectorWeight and SparseWeight based on query type
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
var hydeQuery = await transformer.TransformWithHyDEAsync(query);

// Multi-Query expansion
var multiQueries = await transformer.ExpandQueryAsync(query);

// Query decomposition for complex questions
var subQueries = await transformer.DecomposeQueryAsync(complexQuery);
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
# Mock mode (CI/CD)
pwsh scripts/mock-test.ps1

# Real API mode (local)
cp .env.local.example .env.local
# Edit .env.local with your API key
pwsh scripts/full-test.ps1

# With coverage
pwsh scripts/mock-test.ps1 -Coverage
```

### Test Fixture Pattern

```csharp
[Fact]
public async Task SearchAsync_ValidQuery_ReturnsResults()
{
    var context = FluxIndexContext.CreateBuilder()
        .UseSQLiteInMemory()
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
