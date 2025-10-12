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

var client = new FluxIndexClientBuilder()
    .UseSQLite("fluxindex.db")
    .Build();
```

### With AI Embeddings

```csharp
var client = new FluxIndexClientBuilder()
    .UseSQLite("fluxindex.db")
    .UseOpenAI(apiKey: "your-api-key", model: "text-embedding-3-small")
    .Build();
```

### Production Setup

```csharp
var client = new FluxIndexClientBuilder()
    .UsePostgreSQL("Host=localhost;Database=fluxindex;Username=user;Password=pass")
    .UseOpenAI("your-api-key")
    .UseRedisCache("localhost:6379")
    .Build();
```

### Dependency Injection

```csharp
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

services.AddFluxIndex()
    .AddSQLiteVectorStore()
    .UseOpenAIEmbedding(apiKey: "your-api-key");

var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<FluxIndexClient>();
```

---

## 2. Basic Indexing and Search

### Single Document Indexing

```csharp
var documentId = await client.Indexer.IndexDocumentAsync(
    content: "FluxIndex is a .NET RAG library for semantic search.",
    documentId: "doc-001"
);
```

### Indexing with Metadata

```csharp
await client.Indexer.IndexDocumentAsync(
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
var documents = new[]
{
    new { Content = "First document", Id = "doc-1" },
    new { Content = "Second document", Id = "doc-2" },
    new { Content = "Third document", Id = "doc-3" }
};

foreach (var doc in documents)
{
    await client.Indexer.IndexDocumentAsync(doc.Content, doc.Id);
}

// Or use batch operation (faster)
var requests = documents.Select(d =>
    new IndexRequest(d.Content, d.Id)
).ToList();

await client.Indexer.IndexBatchAsync(requests);
```

### Basic Search

```csharp
var results = await client.Retriever.SearchAsync(
    query: "RAG library",
    topK: 5
);

foreach (var result in results)
{
    Console.WriteLine($"Document: {result.DocumentId}");
    Console.WriteLine($"Score: {result.Score:F2}");
    Console.WriteLine($"Content: {result.Content}");

    if (result.Metadata.ContainsKey("title"))
    {
        Console.WriteLine($"Title: {result.Metadata["title"]}");
    }

    Console.WriteLine("---");
}
```

---

## 3. Search Strategies

### Keyword Search (BM25)

Best for exact term matching and known keywords.

```csharp
var results = await client.Retriever.SearchAsync(
    query: "exact phrase search",
    topK: 10,
    options: new SearchOptions
    {
        SearchStrategy = SearchStrategy.KeywordOnly,
        MinScore = 0.5f
    }
);
```

### Vector Search (Semantic)

Best for meaning-based similarity.

```csharp
var results = await client.Retriever.SearchAsync(
    query: "machine learning algorithms",
    topK: 10,
    options: new SearchOptions
    {
        SearchStrategy = SearchStrategy.DirectVector,
        MinScore = 0.7f
    }
);
```

### Hybrid Search (Recommended)

Combines keyword and semantic search for best results.

```csharp
var results = await client.Retriever.SearchAsync(
    query: "neural network implementation",
    topK: 10,
    options: new SearchOptions
    {
        SearchStrategy = SearchStrategy.Hybrid,
        VectorWeight = 0.7f,      // 70% semantic
        KeywordWeight = 0.3f,     // 30% keyword
        MinScore = 0.6f
    }
);
```

### Adaptive Search (Auto-select)

Automatically selects the best strategy based on query complexity.

```csharp
var results = await client.Retriever.SearchAsync(
    query: "How do neural networks learn from data?",
    topK: 10,
    options: new SearchOptions
    {
        SearchStrategy = SearchStrategy.Adaptive
    }
);
```

### Two-Stage Search (Small-to-Big)

Retrieves small chunks, then expands to surrounding context.

```csharp
var results = await client.Retriever.SearchAsync(
    query: "detailed explanation needed",
    topK: 5,
    options: new SearchOptions
    {
        SearchStrategy = SearchStrategy.TwoStage,
        ExpandContext = true,
        ContextWindow = 2  // Expand 2 chunks before/after
    }
);
```

### Filtering Results

```csharp
var results = await client.Retriever.SearchAsync(
    query: "machine learning",
    topK: 10,
    options: new SearchOptions
    {
        SearchStrategy = SearchStrategy.Hybrid,
        Filter = result =>
            result.Metadata.ContainsKey("category") &&
            result.Metadata["category"].ToString() == "AI",
        MinScore = 0.7f
    }
);
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

### Semantic Caching (Redis)

```bash
dotnet add package FluxIndex.Cache.Redis
```

```csharp
var client = new FluxIndexClientBuilder()
    .UseSQLite("fluxindex.db")
    .UseOpenAI(apiKey)
    .UseRedisCache("localhost:6379")
    .Build();

// Search with caching
var results = await client.Retriever.SearchAsync(
    query: "frequently asked question",
    topK: 5,
    options: new SearchOptions
    {
        UseCache = true,
        CacheTTL = TimeSpan.FromHours(1),
        SimilarityThreshold = 0.95f  // Cache hit threshold
    }
);
```

### Batch Processing

Based on benchmarks, optimal batch size is 1,000-5,000 chunks with 8 threads.

```csharp
var documents = LoadLargeDocumentSet();

// Efficient batch indexing
await client.Indexer.IndexBatchAsync(
    documents: documents,
    options: new IndexingOptions
    {
        BatchSize = 1000,
        MaxParallelism = 8,
        UseCache = true
    }
);

// Expected performance: ~24ms for 1K chunks, ~188ms for 10K chunks
```

### Connection Pooling (PostgreSQL)

```csharp
services.AddFluxIndex()
    .UsePostgreSQLVectorStore(options =>
    {
        options.ConnectionString = "Host=localhost;Database=fluxindex;...";
        options.EnablePooling = true;
        options.MinPoolSize = 5;
        options.MaxPoolSize = 20;
        options.ConnectionTimeout = 30;
    })
    .UseOpenAIEmbedding(apiKey)
    .UseRedisCache("localhost:6379");
```

### Query Optimization

```csharp
// Use adaptive search for varying query types
var results = await client.Retriever.SearchAsync(
    query: userQuery,
    topK: 10,
    options: new SearchOptions
    {
        SearchStrategy = SearchStrategy.Adaptive,  // Auto-selects best strategy
        UseCache = true,                           // Enable semantic caching
        MinScore = 0.7f,                           // Filter low-quality results
        UseReranking = true                        // Optional: rerank results
    }
);
```

### Monitoring Performance

```csharp
var results = await client.Retriever.SearchAsync(query, topK: 10);

// Access performance metrics
Console.WriteLine($"Total time: {results.Performance.TotalTime.TotalMilliseconds}ms");
Console.WriteLine($"Cache hit: {results.Performance.CacheHit}");
Console.WriteLine($"Results: {results.Performance.ResultCount}");
Console.WriteLine($"Strategy: {results.UsedStrategy}");
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
services.AddFluxIndex()
    .UsePostgreSQLVectorStore(config.GetSection("Database"))
    .UseOpenAIEmbedding(config.GetSection("OpenAI"))
    .UseRedisCache(config.GetConnectionString("Redis"));

// Document processing
services.AddFileFluxIntegration(config.GetSection("FileFlux"));
services.AddWebFluxIntegration(config.GetSection("WebFlux"));

var provider = services.BuildServiceProvider();
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
    var results = await client.Retriever.SearchAsync(query, topK: 10);
}
catch (OpenAIException ex)
{
    logger.LogError(ex, "OpenAI API error: {Message}", ex.Message);
    // Fallback to keyword-only search
    var fallbackResults = await client.Retriever.SearchAsync(
        query,
        topK: 10,
        options: new SearchOptions { SearchStrategy = SearchStrategy.KeywordOnly }
    );
}
catch (DatabaseException ex)
{
    logger.LogError(ex, "Database error: {Message}", ex.Message);
    // Retry or alert
}
```

### Health Checks

```csharp
services.AddHealthChecks()
    .AddCheck("fluxindex-database", () =>
    {
        try
        {
            var count = client.Indexer.GetDocumentCount();
            return HealthCheckResult.Healthy($"Database accessible: {count} documents");
        }
        catch
        {
            return HealthCheckResult.Unhealthy("Database not accessible");
        }
    })
    .AddCheck("fluxindex-cache", () =>
    {
        // Check Redis connection
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

Based on benchmarks (.NET 9.0, Intel i7-1360P):

- **Batch Indexing**: 24ms/1K chunks, 188ms/10K chunks
- **Search**: 0.6-0.7ms/1K chunks
- **Optimal Parallelism**: 8 threads
- **Cache Hit Rate**: 60-80% with semantic caching

See [full benchmarks](../benchmarks/FluxIndex.Benchmarks/BENCHMARK_RESULTS.md) for details.

---

## Next Steps

- [Architecture](./architecture.md) - Understand the internal design
- [Examples](../samples/) - See working implementations
- [API Reference](./api-reference.md) - Detailed API documentation
- [Benchmarks](../benchmarks/FluxIndex.Benchmarks/BENCHMARK_RESULTS.md) - Performance analysis

For questions or issues, visit the [GitHub repository](https://github.com/iyulab/FluxIndex).
