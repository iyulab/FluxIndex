# FluxIndex Guide

RAG infrastructure library for .NET - Complete setup and usage guide.

**Related Guides:**
- [AI Provider Integration](./AI_PROVIDER_INTEGRATION.md) - OpenAI, Azure, LMSupply, custom embedding/LLM/reranker
- [Advanced RAG Services](./ADVANCED_RAG.md) - GraphRAG, Self-RAG, Corrective RAG
- [API Reference](./REFERENCE.md) - Architecture and API documentation
- [FluxFeed](https://github.com/iyulab/FluxFeed) - Git-like file tracking / document ingestion pipeline (extracted from FluxIndex in 0.16.0; see [FILEVAULT_GUIDE.md](./FILEVAULT_GUIDE.md) for the migration note)

---

## Installation

```bash
# Core SDK (always required)
dotnet add package FluxIndex.SDK

# Storage (choose one or more)
dotnet add package FluxIndex.Storage.SQLite      # Local mode
dotnet add package FluxIndex.Storage.PostgreSQL  # Production RDB
dotnet add package FluxIndex.Storage.Qdrant      # Production vector DB
dotnet add package FluxIndex.Storage.Neo4j       # Production graph DB

# Optional
dotnet add package FluxIndex.Cache.Redis         # Distributed semantic cache

# File tracking / document ingestion moved to FluxFeed in 0.16.0:
#   dotnet add package FluxFeed                   # https://github.com/iyulab/FluxFeed
```

---

## Storage Modes

FluxIndex supports three storage modes. RDB, VectorDB, and GraphDB are not features to toggle - they **automatically activate based on your configuration**.

| Mode | Description | Use Case |
|------|-------------|----------|
| **Local** | SQLite handles all (Vector + Graph + RDB + Cache) | Development, testing, edge deployment |
| **Full** | PostgreSQL + Qdrant + Neo4j | Large-scale production |
| **Custom** | Mix-and-match or custom providers | Special requirements |

### Auto-Maximize Principle

FluxIndex automatically uses all available capabilities from your configured storage:
- **No feature toggles**: No `WithoutGraph()` or `VectorOnly()` methods
- **Specialized DB priority**: Qdrant (vector) > PostgreSQL's pgvector
- **Fallback to general**: If no specialized DB, uses the general-purpose one

---

## Local Mode (SQLite)

**SQLite handles everything**: Vector search, keyword search, graph relations, semantic cache.

Best for:
- Development and testing
- Edge AI deployment
- Single-machine applications
- Up to ~1M vectors

### Quick Start

```csharp
using FluxIndex.SDK;
using FluxIndex.Storage.SQLite;

// Minimal setup - all features enabled automatically
var context = FluxIndexContext.CreateBuilder()
    .UseLocalStorage("fluxindex.db")  // or .UseSQLite("fluxindex.db")
    .AddSQLiteStorage()
    .Build();

// Index documents
await context.Indexer.IndexDocumentAsync(
    content: "FluxIndex is a .NET RAG library.",
    documentId: "doc-001"
);

// Search (hybrid search enabled automatically)
var results = await context.Retriever.SearchAsync("RAG library", maxResults: 5);
```

> **Select the provider, then register it.** `Use*` only sets options — the matching `Add*Storage()`
> comes from the storage package (here `FluxIndex.Storage.SQLite`) and is what actually registers the
> store. `Build()` throws if you select a provider without registering it.

### Local Mode Storage Structure

`UseSQLite("fluxindex.db")` points the vector store, the chunk hierarchy and the semantic cache at
the **same** database file; only the entity graph gets a file of its own, derived from the path you
passed:

```
fluxindex.db              # Vector store (vectors) + metadata
                          #   + bm25_terms / bm25_postings / bm25_chunks (keyword index)
                          #   + chunk_hierarchies / chunk_relationships (Small-to-Big)
                          #   + semantic_cache (semantic cache)
fluxindex-entitygraph.db  # Entity graph (GraphRAG)
```

(Registering `AddSQLiteGraphStore` / `AddSQLiteSemanticCache` yourself lets you point them at
separate files — `fluxindex-graph.db`, `fluxindex-cache.db` — which is what their defaults do outside
the builder.)

### What Build() provisions

`Build()` creates the schema for **every component the builder enabled** — vector store, keyword
index, graph store, entity graph and semantic cache — before it returns. You do not call
`EnsureCreated` or run migrations yourself.

Provisioning creates only the tables each component owns and leaves everything else in the database
alone, so pointing FluxIndex at a database that already holds your application's tables is supported
(a dedicated database is still the tidier choice for derived data). If some — but not all — of a
component's tables already exist, `Build()` fails with an actionable error rather than half-repairing
the schema.

Opt out per component when you manage the schema externally:

```csharp
var builder = FluxIndexContext.CreateBuilder().UsePostgreSQL(conn);
builder.Options.VectorStore.EnableAutoMigration = false;  // vector store
builder.Options.GraphStore.AutoMigrate = false;           // graph store
builder.Options.SemanticCache.AutoMigrate = false;        // semantic cache
var context = builder.AddPostgreSQLStorage().Build();
```

> Before 0.21.2 (PostgreSQL) and 0.21.3 (SQLite) only the vector store was provisioned on this path;
> the other components' schemas were never created and their first operation failed on a missing
> table. Upgrade if you use graph, GraphRAG or the semantic cache. The SQLite **vector store** itself
> kept using `EnsureCreated()` here until 0.22.0, so before that version pointing it at a database
> that already held any table left `vectors` uncreated and the first write failed.

### In-Memory Testing

```csharp
// Perfect for unit tests - all data in memory
var context = FluxIndexContext.CreateBuilder()
    .UseSQLiteInMemory()
    .AddSQLiteStorage()
    .Build();
```

### With Production Embedding

```csharp
// Local storage + LMSupply (consumer app implements wrapper)
var context = FluxIndexContext.CreateBuilder()
    .UseLocalStorage("fluxindex.db")
    .AddSQLiteStorage()
    .ConfigureServices(s => s.AddLMSupplyEmbedding())  // Your extension method
    .Build();
```

---

## Full Mode (Best-in-Class)

**Specialized databases for each role**: PostgreSQL (RDB/Cache), Qdrant (Vector), Neo4j (Graph).

Best for:
- Large-scale production
- Millions of vectors
- Complex graph queries
- High availability requirements

### Quick Start

```csharp
var context = FluxIndexContext.CreateBuilder()
    .UseBestInClass(
        postgresConnectionString: "Host=localhost;Database=fluxindex;...",
        qdrantHost: "localhost",
        qdrantPort: 6334,
        qdrantCollection: "chunks",
        vectorSize: 1536,
        neo4jUri: "bolt://localhost:7687",
        neo4jUsername: "neo4j",
        neo4jPassword: "password")
    .AddQdrantStorage()      // Vector
    .AddPostgreSQLStorage()  // RDB + Cache
    .AddNeo4jStorage()       // Graph
    .ConfigureServices(s => s.AddOpenAIEmbedding(apiKey))  // Your extension
    .Build();
```

### Storage Role Distribution

| Role | Provider | Purpose |
|------|----------|---------|
| **Vector Search** | Qdrant | High-performance similarity search |
| **Keyword Search** | in-process BM25 | Populated by indexing; **not persisted** on this path — see [The keyword (BM25) index](#the-keyword-bm25-index) |
| **Graph Relations** | Neo4j | Entity graph, community detection |
| **Metadata** | PostgreSQL | Document and chunk metadata |
| **Semantic Cache** | PostgreSQL | Query result caching |

### Docker Compose for Full Mode

```yaml
# docker-compose.yml
services:
  postgres:
    image: pgvector/pgvector:pg16
    environment:
      POSTGRES_DB: fluxindex
      POSTGRES_PASSWORD: postgres
    ports:
      - "5432:5432"

  qdrant:
    image: qdrant/qdrant:latest
    ports:
      - "6333:6333"  # REST
      - "6334:6334"  # gRPC

  neo4j:
    image: neo4j:5-community
    environment:
      NEO4J_AUTH: neo4j/password
    ports:
      - "7474:7474"  # Browser
      - "7687:7687"  # Bolt
```

---

## Custom Mode

Mix and match providers or implement your own.

### PostgreSQL + Qdrant (No Graph)

```csharp
var context = FluxIndexContext.CreateBuilder()
    .UsePostgreSQL(connStr)       // RDB + Cache
    .UseQdrantFixed("localhost", 6334, "chunks", 1536)  // Vector (overrides PostgreSQL)
    .AddPostgreSQLStorage()
    .AddQdrantStorage()
    // No UseNeo4j() → PostgreSQL handles graph (basic support)
    .Build();
```

### PostgreSQL + Neo4j (No Qdrant)

```csharp
var context = FluxIndexContext.CreateBuilder()
    .UsePostgreSQL(connStr)       // RDB + Cache + Vector (pgvector)
    .UseNeo4j(uri, user, pass)    // Graph (overrides PostgreSQL)
    .AddPostgreSQLStorage()
    .AddNeo4jStorage()
    .Build();
```

### Custom Provider Implementation

```csharp
// 1. Implement IStorageProvider with capability interfaces
public class ChromaProvider : IStorageProvider, IVectorCapable
{
    public string ProviderName => "Chroma";
    public StorageCapabilities Capabilities => StorageCapabilities.Vector;
    public IVectorStore VectorStore { get; }

    public ChromaProvider(string endpoint)
    {
        VectorStore = new ChromaVectorStore(endpoint);
    }
}

// 2. Register via ConfigureServices
var context = FluxIndexContext.CreateBuilder()
    .UsePostgreSQL(connStr)  // RDB + Cache
    .ConfigureServices(s =>
        s.AddSingleton<IVectorStore>(new ChromaProvider("http://localhost:8000").VectorStore))
    .Build();
```

### Priority Rules

When multiple providers support the same capability:

1. **Specialized provider wins**: Qdrant (vector-only) > PostgreSQL (multi-purpose)
2. **Last registration wins**: Among same-tier providers
3. **Fallback fills gaps**: Unregistered capabilities use available multi-purpose provider

```csharp
// Example: Qdrant handles Vector, PostgreSQL handles everything else
.UsePostgreSQL(connStr)   // RDB + Cache + Vector + Graph
.UseQdrant(...)           // Takes over Vector
.AddPostgreSQLStorage()
.AddQdrantStorage()
// Result: Qdrant(Vector), PostgreSQL(RDB, Cache, Graph)
```

---

## Configuration Reference

### Builder Methods

| Method | Storage | Features |
|--------|---------|----------|
| `UseLocalStorage(path)` | SQLite | All (Vector, Graph, RDB, Cache) |
| `UseSQLite(path)` | SQLite | All (same as UseLocalStorage) |
| `UseSQLiteInMemory()` | SQLite (memory) | All (testing) |
| `UsePostgreSQL(conn)` | PostgreSQL | All (Vector via pgvector) |
| `UseQdrant(...)` | Qdrant | Vector only |
| `UseNeo4j(...)` | Neo4j | Graph only |
| `UseBestInClass(...)` | PG + Qdrant + Neo4j | Optimal distribution |

> **Advanced Qdrant options.** `UseQdrant(...)` only exposes host/port/collection. To configure
> the full `QdrantOptions` surface (`CreateCollectionOnStartup`, `DistanceMetric`, `HnswM`,
> `OnDiskPayload`, `TimeoutSeconds`, ...), use the `AddQdrantStorage` lambda overload instead:
>
> ```csharp
> builder.AddQdrantStorage(o =>
> {
>     o.Host = "localhost"; o.GrpcPort = 6334; o.BaseCollectionName = "chunks";
>     o.CreateCollectionOnStartup = true; // default is already true → auto-creates collections
> });
> ```

### AI Services

```csharp
// InMemory embedding (default, for testing)
.Build()  // Uses InMemoryEmbeddingService automatically

// Custom embedding (production)
.ConfigureServices(s => s.AddSingleton<IEmbeddingService>(myEmbedder))

// Direct instance
.UseEmbeddingService(myEmbeddingInstance)
```

### Caching

```csharp
// In-memory embedding cache (always enabled)
.Build()

// Redis for distributed cache
.UseRedisCache("localhost:6379")

// Memory cache with custom size
.UseMemoryCache(maxCacheSize: 5000)
```

### Search Options

```csharp
.WithSearchOptions(
    defaultMaxResults: 10,
    defaultMinScore: 0.5f)

.WithChunking(
    strategy: "Auto",      // Auto, Semantic, Sliding
    chunkSize: 512,
    chunkOverlap: 64)

.WithCacheDuration(TimeSpan.FromHours(1))
```

---

## Indexing

### Single Document

```csharp
await context.Indexer.IndexDocumentAsync(
    content: "Document content here...",
    documentId: "doc-001",
    metadata: new Dictionary<string, object>
    {
        ["category"] = "technical",
        ["author"] = "John Doe",
        ["date"] = DateTime.UtcNow
    }
);
```

### Batch Indexing

```csharp
var documents = files.Select(f => new IndexRequest
{
    DocumentId = Path.GetFileNameWithoutExtension(f),
    Content = File.ReadAllText(f),
    Metadata = new Dictionary<string, object> { ["file"] = f }
});

await context.Indexer.IndexBatchAsync(documents, parallelism: 8);
// Performance: ~50K chunks/second with optimal parallelism
```

### File Processing (PDF, DOCX)

```csharp
// Register FileFlux integration
services.AddFileFluxIntegration(options =>
{
    options.DefaultChunkingStrategy = ChunkingStrategies.Intelligent;
    options.DefaultMaxChunkSize = 1024;
    options.DefaultOverlapSize = 128;
    options.DefaultLanguage = "en";
});

// Process and index files
var fileFlux = provider.GetRequiredService<FileFluxIntegration>();
await fileFlux.ProcessAndIndexAsync("document.pdf");
```

---

## Search

### Basic Search

```csharp
// Adaptive search (auto-selects best strategy)
var results = await context.Retriever.SearchAsync(
    query: "How does machine learning work?",
    maxResults: 10,
    minScore: 0.5f
);

foreach (var result in results)
{
    Console.WriteLine($"[{result.Score:F2}] {result.DocumentChunk.Content}");
}
```

### Search Modes

```csharp
// Vector only
var vectorResults = await context.Retriever.SearchAsync(query, maxResults: 10);

// Keyword only (BM25). Reads the keyword index that indexing populates — on the SQLite path that
// index is persisted next to the vectors, so this keeps working after a restart.
var keywordResults = await context.Retriever.KeywordSearchAsync(keyword, maxResults: 10);

// Hybrid (Vector + BM25 with RRF)
var hybridResults = await context.Retriever.HybridSearchAsync(
    keyword: "machine learning",
    query: "How does machine learning work?",
    maxResults: 10,
    vectorWeight: 0.7  // 70% vector, 30% keyword
);
```

### Advanced Search Options

```csharp
var options = new SearchOptions
{
    TopK = 10,
    MinSimilarity = 0.5f,
    UseHybridSearch = true,
    MetadataFilter = new Dictionary<string, object>
    {
        ["category"] = "technical"
    }
};

var results = await context.Retriever.SearchAsync(query, options);

Console.WriteLine($"Total: {results.TotalResults}");
Console.WriteLine($"Time: {results.SearchTime.TotalMilliseconds}ms");
```

---

## GraphRAG

GraphRAG enables entity-aware retrieval for relational queries.

### Enable GraphRAG

```csharp
// Local mode - SQLite handles entity graph
var context = FluxIndexContext.CreateBuilder()
    .UseLocalStorage("fluxindex.db")  // Includes SQLiteEntityGraphStore
    .AddSQLiteStorage()
    .Build();

// Full mode - Neo4j for optimal graph performance
var context = FluxIndexContext.CreateBuilder()
    .UseBestInClass(pgConn, qdrantHost, 6334, "chunks", 1536, neo4jUri, neo4jUser, neo4jPassword)
    .AddQdrantStorage()
    .AddPostgreSQLStorage()
    .AddNeo4jStorage()
    .Build();
```

### GraphRAG Search

```csharp
var graphRag = serviceProvider.GetRequiredService<IGraphRAGService>();

var result = await graphRag.SearchAsync(
    "How are Machine Learning and Neural Networks related?",
    new GraphRAGOptions
    {
        EnableLocalSearch = true,   // Entity-centric search
        EnableGlobalSearch = true,  // Community-based search
        CommunityLevel = 1
    }
);
```

### Entity Graph Operations

```csharp
var entityGraph = serviceProvider.GetRequiredService<IEntityGraphService>();

// Build graph from documents
await entityGraph.BuildGraphAsync(chunks);

// Search by entity
var results = await entityGraph.SearchByEntityAsync(entityId, new EntitySearchOptions
{
    MaxDepth = 3,
    DampingFactor = 0.85
});
```

---

## Performance Tips

| Optimization | Benefit |
|-------------|---------|
| Embedding cache (built-in) | 100% improvement for repeated queries |
| Semantic cache (Redis/SQLite) | ~95% improvement for similar queries |
| Batch indexing (8 threads) | 50K chunks/second throughput |
| Vector quantization | 4-32x memory reduction |
| LocalReranker | +15-25% precision improvement |

### Enable Caching

```csharp
var context = FluxIndexContext.CreateBuilder()
    .UseLocalStorage("fluxindex.db")
    .AddSQLiteStorage()
    .ConfigureServices(s => s.AddLMSupplyEmbedding())
    .UseRedisCache("localhost:6379")  // Distributed semantic cache
    .AddRedisStorage()
    .Build();

// First query: ~50ms (embedding + search)
// Same query: <1ms (embedding cache hit)
// Similar query: <5ms (semantic cache hit)
```

### Enable Quantization

```csharp
// 4x compression with Int8 quantization
services.AddScalarQuantization(dimension: 1536);
services.AddQuantizedVectorStoreDecorator(autoQuantize: true);
```

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Slow search | Enable Redis caching, check embedding cache |
| Database locked (SQLite) | Use singleton pattern, check concurrent access |
| Poor relevance | Adjust chunk size (512-1024), try hybrid search |
| Out of memory | Use batch processing, enable quantization |
| Graph features not working | Ensure Graph provider is registered |

---

## Example: Complete RAG Pipeline

```csharp
public class RAGService
{
    private readonly IFluxIndexContext _context;
    private readonly ITextCompletionService _llm;

    public RAGService(IFluxIndexContext context, ITextCompletionService llm)
    {
        _context = context;
        _llm = llm;
    }

    public async Task<string> AnswerAsync(string question)
    {
        // 1. Retrieve relevant documents
        var results = await _context.Retriever.SearchAsync(question, maxResults: 5);

        // 2. Build context from retrieved chunks
        var context = string.Join("\n\n", results.Select(r =>
            $"[Source: {r.DocumentChunk.Metadata?["title"]}]\n{r.DocumentChunk.Content}"));

        // 3. Generate answer with LLM
        var prompt = $"""
            Based on the following context, answer the question.

            Context:
            {context}

            Question: {question}

            Answer:
            """;

        return await _llm.GenerateCompletionAsync(prompt);
    }
}
```

---

## Next Steps

- [AI Provider Integration](./AI_PROVIDER_INTEGRATION.md) - Implement custom embedding/LLM services
- [Technical Reference](./REFERENCE.md) - Architecture details
- [Samples](../samples/) - Working code examples
