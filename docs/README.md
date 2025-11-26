# FluxIndex Documentation

Complete documentation for the FluxIndex RAG library.

## Quick Navigation

### Getting Started
- [Getting Started](getting-started.md) - 5-minute setup guide
- [Tutorial](TUTORIAL.md) - Comprehensive usage examples
- [Cheat Sheet](cheat-sheet.md) - Quick reference

### Advanced Topics
- [Architecture](architecture.md) - Clean architecture design
- [Testing Guide](TESTING.md) - Unit and integration testing patterns
- [RAG System](FLUXINDEX_RAG_SYSTEM.md) - Advanced RAG patterns
- [LocalReranker Guide](LOCAL_RERANKER_GUIDE.md) - Neural reranking integration

### Practical Resources
- [Examples](../samples/) - Working code samples
- [Benchmarks](../benchmarks/FluxIndex.Benchmarks/BENCHMARK_RESULTS.md) - Performance metrics
- [Development Tasks](../TASKS.md) - Roadmap and progress

## Learning Path

### Beginners
1. Start with [Getting Started](getting-started.md) for initial setup
2. Follow [Tutorial chapters 1-2](TUTORIAL.md#1-setup-and-configuration) for basics
3. Use [Cheat Sheet](cheat-sheet.md) for quick reference
4. Try [RealQualityTest](../samples/RealQualityTest/) sample

### Intermediate
1. Learn [Search Strategies](TUTORIAL.md#3-search-strategies)
2. Understand [Document Processing](TUTORIAL.md#4-document-processing)
3. Study [Architecture Guide](architecture.md)
4. Explore [FileFluxIndexSample](../samples/FileFluxIndexSample/) sample

### Advanced
1. Master [Performance Optimization](TUTORIAL.md#5-performance-optimization)
2. Review [Production Deployment](TUTORIAL.md#6-production-deployment)
3. Deep dive into [RAG System Guide](FLUXINDEX_RAG_SYSTEM.md)
4. Extend with custom implementations

## Feature Overview

### Core Capabilities
- **Vector Search** - Semantic similarity with SQLite-vec or pgvector
- **Keyword Search** - BM25 algorithm for exact matching
- **Hybrid Search** - Reciprocal Rank Fusion combining both
- **Adaptive Search** - Auto-select strategy by query complexity

### AI Integration
- **OpenAI** - GPT embeddings and completions (text-embedding-3-small)
- **Azure OpenAI** - Enterprise Azure deployment support
- **LocalReranker** - Cross-encoder neural reranking with fallback
- **Custom AI** - Implement IEmbeddingService for any provider

### Document Processing
- **FileFlux** - PDF, DOCX, TXT processing with chunking strategies
- **WebFlux** - Web page crawling and extraction with rate limiting
- **Batch Operations** - Efficient bulk indexing with parallelism

### Performance Features
- **Embedding Cache** - In-memory cache for repeated queries (100% improvement)
- **Semantic Caching** - Redis-based similarity caching for related queries
- **Connection Pooling** - Database connection optimization
- **Parallel Processing** - Multi-threaded batch operations (optimal: 8 threads)

## Common Use Cases

### Knowledge Base Search
```csharp
var context = FluxIndexContext.CreateBuilder()
    .UseSQLite("kb.db")
    .UseOpenAI(apiKey, "text-embedding-3-small")
    .Build();

// Index documentation
await fileFlux.ProcessAndIndexAsync("docs/manual.pdf");

// Search with adaptive strategy
var results = await context.Retriever.SearchAsync("installation guide", maxResults: 5);
```

### FAQ System
```csharp
// Index FAQs
await context.Indexer.IndexDocumentAsync(
    content: "Q: How to install? A: Run dotnet add package FluxIndex.SDK",
    documentId: "faq-001",
    metadata: new Dictionary<string, object> { ["category"] = "installation" }
);

// Semantic search
var results = await context.Retriever.SearchAsync("setup instructions", maxResults: 3);
```

### Document Analysis
```csharp
// Index large document set
var files = Directory.GetFiles("research", "*.pdf");
foreach (var file in files)
{
    await fileFlux.ProcessAndIndexAsync(file);
}

// Analyze with complex query using adaptive strategy
var results = await context.Retriever.SearchAsync(
    query: "impact of AI on healthcare",
    maxResults: 10
);
```

## Configuration Examples

### Development Setup
```csharp
var context = FluxIndexContext.CreateBuilder()
    .UseSQLite("dev.db")
    .Build();
```

### Production Setup
```csharp
var context = FluxIndexContext.CreateBuilder()
    .UsePostgreSQL(connectionString)
    .UseOpenAI(apiKey, "text-embedding-3-small")
    .UseResilientLocalReranker(options => options.ModelId = "quality")
    .UseRedisCache("localhost:6379")
    .Build();
```

### Hybrid with Custom AI
```csharp
var context = FluxIndexContext.CreateBuilder()
    .UsePostgreSQL(connectionString)
    .UseCustomEmbedding<CustomEmbeddingService>()
    .UseRedisCache("localhost:6379")
    .Build();
```

## Performance Benchmarks

Based on .NET 10.0, Intel i7-1360P, [full results](../benchmarks/FluxIndex.Benchmarks/BENCHMARK_RESULTS.md):

| Operation | Size | Time | Notes |
|-----------|------|------|-------|
| Batch Indexing | 1K chunks | 24ms | 8-thread parallelism |
| Batch Indexing | 10K chunks | 188ms | 3.5 KB/chunk average |
| Vector Search | 1K chunks | 0.6-0.7ms | In-memory embeddings |
| Embedding Cache Hit | Repeated query | 100% faster | Eliminates API calls (1036ms → 0ms) |
| Hybrid Search | 100 chunks | 383ms avg | With OpenAI API embedding |
| Semantic Cache Hit | Similar query | <5ms | Redis, 95% similarity threshold |

**Optimization Results**:
- Embedding cache provides 100% improvement for exact query matches
- Expected 30% latency reduction with 30% cache hit rate in production
- Optimal parallelism: 8 threads for batch indexing operations

## Troubleshooting

### Common Issues

**OpenAI API Errors**
- Check API key validity
- Verify model availability
- Monitor rate limits
- Use retry logic

**Database Locks**
- Enable WAL mode for SQLite
- Use PostgreSQL for production
- Check connection pooling

**Memory Issues**
- Reduce batch size
- Use pagination for large results
- Monitor GC collections

**Slow Search**
- Enable Redis caching
- Optimize chunk sizes (512-1024 tokens)
- Use appropriate search strategy
- Check database indexes

### Debug Tips

```csharp
// Enable logging
services.AddLogging(builder => builder.AddConsole());

// Check performance metrics
var results = await client.Retriever.SearchAsync(query, topK: 10);
Console.WriteLine($"Time: {results.Performance.TotalTime.TotalMilliseconds}ms");
Console.WriteLine($"Cache: {results.Performance.CacheHit}");
Console.WriteLine($"Strategy: {results.UsedStrategy}");
```

## API Quick Reference

### FluxIndexContext
```csharp
// Indexing
context.Indexer.IndexDocumentAsync(content, documentId, metadata)
context.Indexer.IndexBatchAsync(documents, parallelism)

// Searching
context.Retriever.SearchAsync(query, maxResults, cancellationToken)
```

### Builder Pattern
```csharp
FluxIndexContext.CreateBuilder()
    .UseSQLite(path)                                    // or UseSQLiteInMemory()
    .UsePostgreSQL(connString)
    .UseOpenAI(apiKey, model)                          // "text-embedding-3-small"
    .UseAzureOpenAI(endpoint, apiKey, deployment)
    .UseRedisCache(connString)
    .Build()
```

### Search Results
```csharp
var results = await context.Retriever.SearchAsync(query, maxResults: 10);

foreach (var result in results)
{
    Console.WriteLine($"Score: {result.Score:F2}");
    Console.WriteLine($"Content: {result.DocumentChunk.Content}");
    Console.WriteLine($"Metadata: {result.DocumentChunk.Metadata}");
}
```

## Additional Resources

- **GitHub**: [iyulab/FluxIndex](https://github.com/iyulab/FluxIndex)
- **NuGet**: [FluxIndex.SDK](https://www.nuget.org/packages/FluxIndex.SDK/)
- **Issues**: [GitHub Issues](https://github.com/iyulab/FluxIndex/issues)
- **CI/CD**: [Build Status](https://github.com/iyulab/FluxIndex/actions)

## Contributing

Contributions welcome! See [development roadmap](../TASKS.md) for planned features.

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests
5. Submit a pull request

---

**Start building your RAG system with FluxIndex!** 🚀
