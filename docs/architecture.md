# FluxIndex Architecture

Clean Architecture-based RAG infrastructure for .NET 10.0 with modular design and provider flexibility.

## Architecture Overview

FluxIndex follows Clean Architecture principles with clear separation of concerns:

```
┌─────────────────────────────────────────────────────┐
│               SDK Layer                              │
│             (FluxIndex.SDK)                         │
│  FluxIndexContext, Builder Pattern                  │
├─────────────────────────────────────────────────────┤
│              Provider Packages (Optional)            │
│  FluxIndex.AI.OpenAI    FluxIndex.Storage.*         │
│  FluxIndex.Cache.Redis  FluxIndex.Extensions.*      │
├─────────────────────────────────────────────────────┤
│              Core Infrastructure                     │
│              (FluxIndex.Core)                       │
│   Domain + Application + Infrastructure             │
└─────────────────────────────────────────────────────┘
```

## Core Layers

### 1. Domain Layer

**Pure business logic with no dependencies**

```csharp
// Domain Entities
namespace FluxIndex.Domain.Entities
{
    public class Document
    {
        public string Id { get; set; }
        public ICollection<DocumentChunk> Chunks { get; }

        public static Document Create(string id);
        public void AddChunk(DocumentChunk chunk);
    }

    public class DocumentChunk
    {
        public string Content { get; set; }
        public int ChunkIndex { get; set; }
        public string DocumentId { get; set; }
        public EmbeddingVector? Embedding { get; set; }
        public ChunkMetadata? Metadata { get; set; }
    }
}

// Value Objects
namespace FluxIndex.Domain.ValueObjects
{
    public class EmbeddingVector
    {
        public float[] Values { get; }
        public int Dimensions { get; }

        public float CosineSimilarity(EmbeddingVector other);
    }
}
```

### 2. Application Layer

**Business logic orchestration and interfaces**

```csharp
// Core Interfaces
namespace FluxIndex.Core.Application.Interfaces
{
    public interface IVectorStore
    {
        Task StoreAsync(DocumentChunk chunk, CancellationToken ct);
        Task<IEnumerable<DocumentChunk>> SearchAsync(
            EmbeddingVector queryVector, int topK, CancellationToken ct);
    }

    public interface IEmbeddingService
    {
        Task<EmbeddingVector> GenerateEmbeddingAsync(
            string text, CancellationToken ct);
        Task<IEnumerable<EmbeddingVector>> GenerateBatchEmbeddingsAsync(
            IEnumerable<string> texts, CancellationToken ct);
    }

    public interface IDocumentRepository
    {
        Task<Document?> GetByIdAsync(string id, CancellationToken ct);
        Task AddAsync(Document document, CancellationToken ct);
    }
}

// Application Services
namespace FluxIndex.Core.Application.Services
{
    public class SearchService
    {
        // Vector search, keyword search, hybrid search
        Task<IEnumerable<SearchResult>> SearchAsync(
            string query, SearchStrategy strategy, CancellationToken ct);
    }

    public class IndexingService
    {
        // Document indexing with metadata enrichment
        Task IndexDocumentAsync(
            DocumentChunk chunk, CancellationToken ct);
        Task IndexBatchAsync(
            IEnumerable<DocumentChunk> chunks, int parallelism, CancellationToken ct);
    }

    public class BM25Service
    {
        // Keyword-based search using BM25 algorithm
        Task<IEnumerable<SearchResult>> SearchAsync(
            string query, int topK, CancellationToken ct);
    }
}
```

### 3. Infrastructure Layer

**External dependencies implementation**

Provider packages implement core interfaces:

```csharp
// FluxIndex.AI.OpenAI
public class OpenAIEmbeddingService : IEmbeddingService
{
    Task<EmbeddingVector> GenerateEmbeddingAsync(string text, CancellationToken ct);
}

// FluxIndex.Storage.SQLite
public class SQLiteVectorStore : IVectorStore
{
    // SQLite with vector search extension
}

// FluxIndex.Storage.PostgreSQL
public class PostgreSQLVectorStore : IVectorStore
{
    // PostgreSQL with pgvector extension
}

// FluxIndex.Cache.Redis
public class RedisSemanticCache : ISemanticCacheService
{
    // Redis-based semantic caching
}
```

### 4. SDK Layer

**User-facing API with fluent configuration**

```csharp
// FluxIndex.SDK.FluxIndexContext
public class FluxIndexContext : IFluxIndexContext
{
    public Retriever Retriever { get; }
    public Indexer Indexer { get; }

    public static FluxIndexContextBuilder CreateBuilder();
}

// Builder Pattern
public class FluxIndexContextBuilder
{
    // Storage configuration
    public FluxIndexContextBuilder UseSQLite(string path);
    public FluxIndexContextBuilder UseSQLiteInMemory();
    public FluxIndexContextBuilder UsePostgreSQL(string connectionString);

    // AI provider configuration
    public FluxIndexContextBuilder UseOpenAI(string apiKey, string model);
    public FluxIndexContextBuilder UseAzureOpenAI(string endpoint, string apiKey, string deployment);
    public FluxIndexContextBuilder UseCustomEmbedding<T>() where T : IEmbeddingService;

    // Caching configuration
    public FluxIndexContextBuilder UseRedisCache(string connectionString);
    public FluxIndexContextBuilder UseInMemoryCache();

    public FluxIndexContext Build();
}
```

## Design Patterns

### Repository Pattern

Abstracts data access with `IDocumentRepository` and `IVectorStore` interfaces.

```csharp
// Domain defines interfaces
public interface IDocumentRepository
{
    Task<Document?> GetByIdAsync(string id, CancellationToken ct);
    Task AddAsync(Document document, CancellationToken ct);
}

// Infrastructure implements
public class SQLiteDocumentRepository : IDocumentRepository
{
    // SQLite-specific implementation
}
```

### Factory Pattern

Domain entities use static factory methods for controlled creation:

```csharp
public class Document
{
    private Document(string id) { Id = id; }

    public static Document Create(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Document ID cannot be empty", nameof(id));

        return new Document(id);
    }
}
```

### Builder Pattern

Fluent API for SDK configuration:

```csharp
var context = FluxIndexContext.CreateBuilder()
    .UseSQLite("fluxindex.db")
    .UseOpenAI(apiKey, "text-embedding-3-small")
    .UseRedisCache("localhost:6379")
    .Build();
```

### Strategy Pattern

Multiple search strategies with runtime selection:

```csharp
public enum SearchStrategy
{
    Adaptive,    // Auto-select based on query
    Vector,      // Semantic search only
    Keyword,     // BM25 search only
    Hybrid       // Combined vector + keyword
}

public interface ISearchStrategy
{
    Task<IEnumerable<SearchResult>> ExecuteAsync(
        string query, int topK, CancellationToken ct);
}
```

## Dependency Injection

FluxIndex uses Microsoft.Extensions.DependencyInjection throughout:

```csharp
// Service registration in builder
services.AddSingleton<IVectorStore, SQLiteVectorStore>();
services.AddSingleton<IEmbeddingService, OpenAIEmbeddingService>();
services.AddSingleton<IDocumentRepository, SQLiteDocumentRepository>();

// Application services
services.AddScoped<SearchService>();
services.AddScoped<IndexingService>();
services.AddScoped<BM25Service>();

// SDK components
services.AddSingleton<Retriever>();
services.AddSingleton<Indexer>();
```

## Data Flow

### Indexing Pipeline

```
1. Document Input
   ↓
2. Chunking (via FileFlux extension)
   ↓
3. Metadata Enrichment (MetadataEnrichmentService)
   ↓
4. Embedding Generation (IEmbeddingService)
   ↓
5. Vector Storage (IVectorStore)
   ↓
6. Document Repository (IDocumentRepository)
```

### Search Pipeline

```
1. User Query
   ↓
2. Query Analysis (complexity detection)
   ↓
3. Strategy Selection (Adaptive/Vector/Keyword/Hybrid)
   ↓
4. Cache Check (ISemanticCacheService)
   ↓
5. Vector Search (IVectorStore)
   ↓
6. Keyword Search (BM25Service)
   ↓
7. Rank Fusion (RankFusionService)
   ↓
8. Reranking (IRerankerService)
   ↓
9. Results
```

## Package Structure

### Core Package (Required)

**FluxIndex.Core** - Minimal dependencies, local algorithms

- BM25 keyword search
- Local reranking
- Rank fusion
- Domain models

### SDK Package (Recommended)

**FluxIndex.SDK** - User-facing API

- FluxIndexContext
- Retriever and Indexer components
- Builder pattern

### Provider Packages (Optional)

**AI Services:**
- FluxIndex.AI.OpenAI - OpenAI/Azure OpenAI embeddings

**Storage:**
- FluxIndex.Storage.SQLite - SQLite with vector extension
- FluxIndex.Storage.PostgreSQL - PostgreSQL with pgvector

**Caching:**
- FluxIndex.Cache.Redis - Redis-based semantic caching

**Extensions:**
- FluxIndex.Extensions.FileFlux - PDF/DOCX processing
- FluxIndex.Extensions.WebFlux - Web crawling

## AI Provider Flexibility

FluxIndex is **completely AI provider-agnostic**:

```csharp
// Use OpenAI (optional)
var context = FluxIndexContext.CreateBuilder()
    .UseOpenAI(apiKey, "text-embedding-3-small")
    .Build();

// Use custom embedding service
public class CustomEmbeddingService : IEmbeddingService
{
    public Task<EmbeddingVector> GenerateEmbeddingAsync(
        string text, CancellationToken ct)
    {
        // Your implementation (Anthropic, Cohere, local models, etc.)
    }
}

var context = FluxIndexContext.CreateBuilder()
    .UseCustomEmbedding<CustomEmbeddingService>()
    .Build();

// Use without AI (local algorithms only)
var context = FluxIndexContext.CreateBuilder()
    .UseSQLite("fluxindex.db")
    .Build();  // BM25, local reranking still available
```

## Performance Optimizations

### 1. Embedding Cache

In-memory cache eliminates redundant API calls:

```csharp
// First query: API call (1000ms)
var results1 = await context.Retriever.SearchAsync("machine learning");

// Identical query: cached (0ms)
var results2 = await context.Retriever.SearchAsync("machine learning");

// Performance: 100% improvement for repeated queries
```

### 2. Semantic Cache (Redis)

Similar queries use cached results:

```csharp
// First query: "machine learning basics"
var results1 = await context.Retriever.SearchAsync("machine learning basics");

// Similar query: "ML fundamentals" (95% similarity)
// Uses cached results if similarity > threshold (default 0.95)
var results2 = await context.Retriever.SearchAsync("ML fundamentals");

// Performance: <5ms for similar queries
```

### 3. Batch Operations

Parallel processing for bulk indexing:

```csharp
// Optimal parallelism: 8 threads
await context.Indexer.IndexBatchAsync(
    documents,
    parallelism: 8,
    cancellationToken);

// Performance: 24ms per 1K chunks
```

### 4. Connection Pooling

Database connection optimization:

```csharp
// SQLite: WAL mode for concurrent reads
// PostgreSQL: Connection pooling enabled by default

// Configuration
builder.UsePostgreSQL(connectionString); // Auto-pooling
builder.UseSQLite(path); // Auto WAL mode
```

## Extension Points

### Custom Embedding Service

```csharp
public class CustomEmbeddingService : IEmbeddingService
{
    public async Task<EmbeddingVector> GenerateEmbeddingAsync(
        string text, CancellationToken ct)
    {
        // Your embedding logic
        var values = await YourModel.EmbedAsync(text);
        return new EmbeddingVector(values);
    }

    public async Task<IEnumerable<EmbeddingVector>> GenerateBatchEmbeddingsAsync(
        IEnumerable<string> texts, CancellationToken ct)
    {
        // Batch embedding logic for better performance
    }
}
```

### Custom Vector Store

```csharp
public class CustomVectorStore : IVectorStore
{
    public async Task StoreAsync(DocumentChunk chunk, CancellationToken ct)
    {
        // Your storage logic (Pinecone, Qdrant, etc.)
    }

    public async Task<IEnumerable<DocumentChunk>> SearchAsync(
        EmbeddingVector queryVector, int topK, CancellationToken ct)
    {
        // Your vector search logic
    }
}
```

### Custom Reranker

```csharp
public class CustomRerankerService : IRerankerService
{
    public async Task<IEnumerable<SearchResult>> RerankAsync(
        string query,
        IEnumerable<SearchResult> results,
        CancellationToken ct)
    {
        // Your reranking logic (cross-encoder, custom scoring, etc.)
    }
}
```

## Testing Architecture

### Unit Tests

Test individual components in isolation:

```csharp
public class SearchServiceTests
{
    [Fact]
    public async Task SearchAsync_ValidQuery_ReturnsResults()
    {
        // Arrange: Mock dependencies
        var mockVectorStore = new Mock<IVectorStore>();
        var mockEmbedding = new Mock<IEmbeddingService>();

        var service = new SearchService(
            mockVectorStore.Object,
            mockEmbedding.Object);

        // Act
        var results = await service.SearchAsync("test query");

        // Assert
        Assert.NotEmpty(results);
    }
}
```

### Integration Tests

Test full stack with real dependencies:

```csharp
public class FluxIndexIntegrationTests
{
    [Fact]
    public async Task IndexAndSearch_EndToEnd_Success()
    {
        // Arrange: Real context with SQLite
        var context = FluxIndexContext.CreateBuilder()
            .UseSQLiteInMemory()
            .Build();

        // Act: Index and search
        await context.Indexer.IndexDocumentAsync("test content", "doc-1");
        var results = await context.Retriever.SearchAsync("test");

        // Assert
        Assert.Single(results);
    }
}
```

## Best Practices

### 1. Use Builder Pattern

```csharp
// Good: Fluent configuration
var context = FluxIndexContext.CreateBuilder()
    .UseSQLite("fluxindex.db")
    .UseOpenAI(apiKey, "text-embedding-3-small")
    .Build();

// Avoid: Manual service registration
```

### 2. Dispose Resources

```csharp
// Good: Using statement
using var context = FluxIndexContext.CreateBuilder()
    .UseSQLite("fluxindex.db")
    .Build();

// Or: Explicit disposal
var context = FluxIndexContext.CreateBuilder().Build();
try
{
    // Use context
}
finally
{
    context.Dispose();
}
```

### 3. Use Cancellation Tokens

```csharp
// Good: Support cancellation
var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var results = await context.Retriever.SearchAsync(
    "query",
    maxResults: 10,
    cancellationToken: cts.Token);

// Handles timeouts and user cancellation
```

### 4. Batch When Possible

```csharp
// Good: Batch indexing
await context.Indexer.IndexBatchAsync(
    documents,
    parallelism: 8,
    cancellationToken);

// Avoid: Sequential indexing for large datasets
foreach (var doc in documents)
{
    await context.Indexer.IndexDocumentAsync(doc.Content, doc.Id);
}
```

## References

- [Getting Started](getting-started.md) - Setup and configuration
- [Tutorial](TUTORIAL.md) - Comprehensive examples
- [Testing Guide](TESTING.md) - Unit and integration testing
- [Benchmarks](../benchmarks/FluxIndex.Benchmarks/BENCHMARK_RESULTS.md) - Performance metrics
