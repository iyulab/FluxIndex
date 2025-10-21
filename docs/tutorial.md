# FluxIndex Tutorial

Comprehensive guide for using FluxIndex in production .NET applications.

## Table of Contents

1. [Setup and Configuration](#1-setup-and-configuration)
2. [Basic Indexing and Search](#2-basic-indexing-and-search)
3. [Search Strategies](#3-search-strategies)
4. [Document Processing](#4-document-processing)
5. [Performance Optimization](#5-performance-optimization)
6. [Production Deployment](#6-production-deployment)

---

## 1. Setup and Configuration

### Minimal Setup (Development)

```csharp
using FluxIndex.SDK;

var context = FluxIndexContext.CreateBuilder()
    .UseSQLite("fluxindex.db")
    .Build();
```

### With AI Embeddings

```csharp
var context = FluxIndexContext.CreateBuilder()
    .UseSQLite("fluxindex.db")
    .UseOpenAI(apiKey: "your-api-key", model: "text-embedding-3-small")
    .Build();
```

### Production Setup

```csharp
var context = FluxIndexContext.CreateBuilder()
    .UsePostgreSQL("Host=localhost;Database=fluxindex;Username=user;Password=pass")
    .UseOpenAI("your-api-key", "text-embedding-3-small")
    .UseRedisCache("localhost:6379")
    .Build();
```

### Dependency Injection

```csharp
using Microsoft.Extensions.DependencyInjection;
using FluxIndex.SDK;

var services = new ServiceCollection();

services.AddSingleton<IFluxIndexContext>(provider =>
{
    return FluxIndexContext.CreateBuilder()
        .UseSQLite("fluxindex.db")
        .UseOpenAI("your-api-key", "text-embedding-3-small")
        .Build();
});

var serviceProvider = services.BuildServiceProvider();
var context = serviceProvider.GetRequiredService<IFluxIndexContext>();
```

---

## 2. Basic Indexing and Search

### Single Document Indexing

```csharp
var documentId = await context.Indexer.IndexDocumentAsync(
    content: "FluxIndex is a .NET RAG library for semantic search.",
    documentId: "doc-001"
);
```

### Indexing with Metadata

```csharp
await context.Indexer.IndexDocumentAsync(
    content: "Document content here...",
    documentId: "doc-002",
    metadata: new Dictionary<string, object>
    {
        ["title"] = "Getting Started",
        ["category"] = "tutorial",
        ["author"] = "John Doe",
        ["created_at"] = DateTime.UtcNow,
        ["tags"] = new[] { "rag", "search", "dotnet" }
    }
);
```

### Batch Indexing

```csharp
// Prepare document entities
var documents = new List<Document>();

for (int i = 0; i < 100; i++)
{
    var doc = new Document
    {
        Id = $"doc-{i:D3}",
        CreatedAt = DateTime.UtcNow
    };

    doc.AddChunk(new DocumentChunk
    {
        Id = $"chunk-{i:D3}",
        DocumentId = doc.Id,
        Content = $"Content for document {i}",
        ChunkIndex = 0,
        TotalChunks = 1
    });

    documents.Add(doc);
}

// Batch indexing with optimal parallelism (based on benchmarks: 8 threads)
await context.Indexer.IndexBatchAsync(documents, parallelism: 8);

// Expected performance: 24ms for 1K chunks, 188ms for 10K chunks
```

### Basic Search

```csharp
var results = await context.Retriever.SearchAsync(
    query: "RAG library for .NET",
    maxResults: 5
);

foreach (var result in results)
{
    Console.WriteLine($"Document: {result.DocumentChunk.DocumentId}");
    Console.WriteLine($"Score: {result.Score:F2}");
    Console.WriteLine($"Content: {result.DocumentChunk.Content}");

    if (result.DocumentChunk.Metadata.ContainsKey("title"))
    {
        Console.WriteLine($"Title: {result.DocumentChunk.Metadata["title"]}");
    }

    Console.WriteLine("---");
}
```

---

## 3. Search Strategies

FluxIndex provides multiple search strategies. The **Adaptive** strategy (default) automatically selects the best approach based on query characteristics.

### Adaptive Search (Recommended - Default)

Automatically analyzes query complexity and selects the optimal strategy.

```csharp
// Simple usage - Adaptive strategy is applied by default
var results = await context.Retriever.SearchAsync(
    query: "How do neural networks learn from data?",
    maxResults: 10
);

// Adaptive strategy automatically selects:
// - Keyword search for simple, exact-match queries
// - Vector search for complex semantic queries
// - Hybrid search for balanced needs
```

### Hybrid Search

Combines BM25 keyword search with vector semantic search using Reciprocal Rank Fusion (RRF).

```csharp
var results = await context.Retriever.SearchAsync(
    query: "neural network implementation details",
    maxResults: 10
);

// Hybrid search provides:
// - Keyword matching for exact technical terms
// - Semantic understanding for conceptual queries
// - Best balance of precision and recall
```

### Performance Optimization

FluxIndex includes automatic performance optimizations:

**Embedding Cache** (Phase 7.3):
- In-memory cache for repeated queries
- 100% performance improvement for cache hits
- Eliminates redundant OpenAI API calls
- Expected 30% latency reduction in production

```csharp
// First query - generates embedding via API (~1000ms)
var results1 = await context.Retriever.SearchAsync("RAG library", maxResults: 5);

// Same query - retrieves from cache (<1ms)
var results2 = await context.Retriever.SearchAsync("RAG library", maxResults: 5);

// Cache automatically manages:
// - Up to 1000 queries (LRU eviction)
// - Thread-safe concurrent access
// - Exact string matching
```

**Redis Semantic Cache** (Optional):
```csharp
// Enable Redis for similar query caching
var context = FluxIndexContext.CreateBuilder()
    .UseSQLite("fluxindex.db")
    .UseOpenAI(apiKey, "text-embedding-3-small")
    .UseRedisCache("localhost:6379")
    .Build();

// Similar queries share cached results (95% similarity threshold)
await context.Retriever.SearchAsync("What is RAG?", maxResults: 5);       // Cache miss
await context.Retriever.SearchAsync("Explain RAG systems", maxResults: 5); // Cache hit (~95% similar)
```

---

## 4. Document Processing

### FileFlux Integration (PDF, DOCX, TXT)

```bash
dotnet add package FluxIndex.Extensions.FileFlux
```

```csharp
using FluxIndex.Extensions.FileFlux;

services.AddFluxIndex()
    .AddSQLiteVectorStore()
    .UseOpenAIEmbedding(apiKey);

services.AddFileFluxIntegration(options =>
{
    options.DefaultChunkingStrategy = "Semantic";  // or "Fixed", "Auto"
    options.DefaultMaxChunkSize = 1024;
    options.DefaultOverlapSize = 128;
});

var provider = services.BuildServiceProvider();
var fileFlux = provider.GetRequiredService<FileFluxIntegration>();

// Process single file
var documentId = await fileFlux.ProcessAndIndexAsync("manual.pdf");

// Process with custom options
var docId = await fileFlux.ProcessAndIndexAsync(
    filePath: "technical-doc.pdf",
    options: new ProcessingOptions
    {
        ChunkingStrategy = "Semantic",
        MaxChunkSize = 512,
        OverlapSize = 64,
        Metadata = new Dictionary<string, object>
        {
            ["source"] = "manual",
            ["version"] = "1.0"
        }
    }
);

// Process directory
var fileFlux = provider.GetRequiredService<FileFluxIntegration>();
var files = Directory.GetFiles("docs", "*.pdf");

foreach (var file in files)
{
    await fileFlux.ProcessAndIndexAsync(file);
    Console.WriteLine($"Indexed: {file}");
}
```

### WebFlux Integration (Web Crawling)

```bash
dotnet add package FluxIndex.Extensions.WebFlux
```

```csharp
using FluxIndex.Extensions.WebFlux;

services.AddWebFluxIntegration(options =>
{
    options.DefaultMaxChunkSize = 512;
    options.DefaultChunkOverlap = 50;
    options.UserAgent = "MyRAGBot/1.0";
    options.RateLimitDelay = TimeSpan.FromSeconds(1);
});

var webFlux = provider.GetRequiredService<WebFluxIntegration>();

// Crawl and index single URL
var docId = await webFlux.ProcessAndIndexAsync("https://example.com/docs");

// Crawl multiple pages
var urls = new[]
{
    "https://example.com/page1",
    "https://example.com/page2",
    "https://example.com/page3"
};

foreach (var url in urls)
{
    await webFlux.ProcessAndIndexAsync(url);
}
```

---

## 5. Performance Optimization

### Built-in Optimizations

FluxIndex includes automatic performance optimizations enabled by default:

**1. Embedding Cache (In-Memory)**
- Caches query embeddings to eliminate redundant API calls
- 100% latency improvement for repeated queries
- Thread-safe with LRU eviction (1000 query limit)
- No configuration required - works automatically

**2. SQLite WAL Mode**
- Write-Ahead Logging enabled by default
- Reduces database lock contention
- Improves concurrent read/write performance

**3. Optimal Parallelism**
- Batch indexing uses 8 threads by default (based on benchmarks)
- Can be customized via `parallelism` parameter

### Semantic Caching with Redis (Optional)

Add Redis for similarity-based caching across similar queries:

```bash
dotnet add package FluxIndex.Cache.Redis
```

```csharp
var context = FluxIndexContext.CreateBuilder()
    .UseSQLite("fluxindex.db")
    .UseOpenAI(apiKey, "text-embedding-3-small")
    .UseRedisCache("localhost:6379")
    .Build();

// Redis caches similar queries (95% similarity threshold)
await context.Retriever.SearchAsync("What is RAG?", maxResults: 5);
await context.Retriever.SearchAsync("Explain RAG", maxResults: 5); // Cache hit
```

### Batch Processing

Optimal configuration based on benchmarks:

```csharp
var documents = LoadLargeDocumentSet();

// Efficient batch indexing with optimal settings
await context.Indexer.IndexBatchAsync(
    documents: documents,
    parallelism: 8  // Optimal thread count from benchmarks
);

// Expected performance:
// - 1K chunks: ~24ms
// - 10K chunks: ~188ms
// - Vector search: 0.6-0.7ms per query
```

### Connection Pooling (PostgreSQL)

For production environments with PostgreSQL:

```csharp
var context = FluxIndexContext.CreateBuilder()
    .UsePostgreSQL("Host=localhost;Database=fluxindex;Username=user;Password=pass;Pooling=true;MinPoolSize=5;MaxPoolSize=20")
    .UseOpenAI(apiKey, "text-embedding-3-small")
    .UseRedisCache("localhost:6379")
    .Build();
```

---

## 6. Production Deployment

### Complete Production Setup

```csharp
using FluxIndex.SDK;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();

// Logging
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.AddApplicationInsights();
});

// FluxIndex with production settings
services.AddSingleton<IFluxIndexContext>(provider =>
{
    return FluxIndexContext.CreateBuilder()
        .UsePostgreSQL(config.GetConnectionString("Database"))
        .UseOpenAI(
            config["OpenAI:ApiKey"],
            config["OpenAI:Model"] ?? "text-embedding-3-small"
        )
        .UseRedisCache(config.GetConnectionString("Redis"))
        .Build();
});

var serviceProvider = services.BuildServiceProvider();
```

### appsettings.json

```json
{
  "Database": {
    "ConnectionString": "Host=localhost;Database=fluxindex;Username=user;Password=pass",
    "EnablePooling": true,
    "MinPoolSize": 5,
    "MaxPoolSize": 20,
    "EnableWAL": true
  },
  "OpenAI": {
    "ApiKey": "your-api-key",
    "Model": "text-embedding-3-small",
    "Dimensions": 1536,
    "MaxRetries": 3,
    "RetryDelay": "00:00:01"
  },
  "Redis": {
    "ConnectionString": "localhost:6379",
    "CacheTTL": "01:00:00",
    "SimilarityThreshold": 0.95
  },
  "FileFlux": {
    "DefaultChunkingStrategy": "Semantic",
    "DefaultMaxChunkSize": 1024,
    "DefaultOverlapSize": 128
  },
  "WebFlux": {
    "DefaultMaxChunkSize": 512,
    "DefaultChunkOverlap": 50,
    "RateLimitDelay": "00:00:01"
  }
}
```

### Error Handling

```csharp
try
{
    var results = await context.Retriever.SearchAsync(query, maxResults: 10);
}
catch (Exception ex) when (ex.Message.Contains("OpenAI"))
{
    logger.LogError(ex, "OpenAI API error: {Message}", ex.Message);
    // Embedding cache helps reduce API failures for repeated queries
    // Retry with exponential backoff or use fallback
}
catch (Exception ex)
{
    logger.LogError(ex, "Search error: {Message}", ex.Message);
    // Handle other errors
}
```

### Health Checks

```csharp
services.AddHealthChecks()
    .AddCheck("fluxindex", () =>
    {
        try
        {
            // Verify FluxIndexContext is operational
            var context = serviceProvider.GetRequiredService<IFluxIndexContext>();
            return HealthCheckResult.Healthy("FluxIndex operational");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("FluxIndex unavailable", ex);
        }
    });
```

### Deployment Checklist

- [ ] Configure connection strings
- [ ] Set API keys in environment variables
- [ ] Enable WAL mode for SQLite (dev) or use PostgreSQL (prod)
- [ ] Configure Redis caching
- [ ] Set up logging and monitoring
- [ ] Configure health checks
- [ ] Test with production data
- [ ] Monitor performance metrics
- [ ] Set up backup strategy

---

## Best Practices

1. **Use Adaptive Search** - Let FluxIndex choose the best strategy
2. **Enable Caching** - Reduce latency for common queries
3. **Batch Operations** - Process multiple documents together
4. **Monitor Performance** - Track metrics and optimize
5. **Handle Errors Gracefully** - Implement fallbacks
6. **Use PostgreSQL in Production** - Better scalability than SQLite
7. **Set Appropriate Chunk Sizes** - 512-1024 tokens per chunk
8. **Use Metadata** - Enables filtering and better organization

## Performance Expectations

Based on Phase 7.3 benchmarks (.NET 9.0, Intel i7-1360P):

**Indexing Performance**:
- Batch indexing (1K chunks): 24ms with 8-thread parallelism
- Batch indexing (10K chunks): 188ms (3.5 KB/chunk average)
- Expected throughput: ~50K chunks/second

**Search Performance**:
- Vector search: 0.6-0.7ms per query (in-memory embeddings)
- Hybrid search with OpenAI API: 383ms average (100 chunks)
- Embedding cache hit: 100% improvement (1036ms → <1ms)
- Redis semantic cache hit: <5ms (95% similarity threshold)

**Cache Effectiveness**:
- Embedding cache for exact matches: 100% latency reduction
- Expected production improvement: 30% with 30% cache hit rate
- Semantic cache for similar queries: 95%+ latency reduction

**Optimal Settings**:
- Thread parallelism: 8 threads for batch operations
- Chunk size: 512-1024 tokens per chunk
- Embedding cache: 1000 queries (LRU eviction)

See [full benchmarks](../benchmarks/FluxIndex.Benchmarks/BENCHMARK_RESULTS.md) and [Phase 7.3 results](../benchmarks/FluxIndex.Benchmarks/PHASE_7.3_RESULTS.md) for details.

---

## Next Steps

- [Architecture](./architecture.md) - Understand the internal design
- [Examples](../samples/) - See working implementations
- [API Reference](./api-reference.md) - Detailed API documentation
- [Benchmarks](../benchmarks/FluxIndex.Benchmarks/BENCHMARK_RESULTS.md) - Performance analysis

For questions or issues, visit the [GitHub repository](https://github.com/iyulab/FluxIndex).
