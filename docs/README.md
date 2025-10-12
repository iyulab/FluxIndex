# FluxIndex Documentation

Complete documentation for the FluxIndex RAG library.

## Quick Navigation

### Getting Started
- [Getting Started](getting-started.md) - 5-minute setup guide
- [Tutorial](tutorial.md) - Comprehensive usage examples
- [Cheat Sheet](cheat-sheet.md) - Quick reference

### Advanced Topics
- [Architecture](architecture.md) - Clean architecture design
- [RAG System](FLUXINDEX_RAG_SYSTEM.md) - Advanced RAG patterns

### Practical Resources
- [Examples](../samples/) - Working code samples
- [Benchmarks](../benchmarks/FluxIndex.Benchmarks/BENCHMARK_RESULTS.md) - Performance metrics
- [Tests](../tests/) - Unit and integration tests

## Learning Path

### Beginners
1. Start with [Getting Started](getting-started.md) for initial setup
2. Follow [Tutorial chapters 1-2](tutorial.md#1-setup-and-configuration) for basics
3. Use [Cheat Sheet](cheat-sheet.md) for quick reference
4. Try [RealWorldDemo](../samples/FluxIndex.RealWorldDemo/) sample

### Intermediate
1. Learn [Search Strategies](tutorial.md#3-search-strategies)
2. Understand [Document Processing](tutorial.md#4-document-processing)
3. Study [Architecture Guide](architecture.md)
4. Explore [FileFlux](../samples/FileFluxIndexSample/) sample

### Advanced
1. Master [Performance Optimization](tutorial.md#5-performance-optimization)
2. Review [Production Deployment](tutorial.md#6-production-deployment)
3. Deep dive into [RAG System Guide](FLUXINDEX_RAG_SYSTEM.md)
4. Extend with custom implementations

## Feature Overview

### Core Capabilities
- **Vector Search** - Semantic similarity with SQLite-vec or pgvector
- **Keyword Search** - BM25 algorithm for exact matching
- **Hybrid Search** - Reciprocal Rank Fusion combining both
- **Adaptive Search** - Auto-select strategy by query complexity

### AI Integration
- **OpenAI** - GPT embeddings and completions
- **Azure OpenAI** - Enterprise Azure deployment support
- **Custom AI** - Implement IEmbeddingService for any provider

### Document Processing
- **FileFlux** - PDF, DOCX, TXT processing
- **WebFlux** - Web page crawling and extraction
- **Batch Operations** - Efficient bulk indexing

### Performance Features
- **Semantic Caching** - Redis-based similarity caching
- **Connection Pooling** - Database connection optimization
- **Parallel Processing** - Multi-threaded batch operations

## Common Use Cases

### Knowledge Base Search
```csharp
var client = new FluxIndexClientBuilder()
    .UseSQLite("kb.db")
    .UseOpenAI(apiKey)
    .Build();

// Index documentation
await fileFlux.ProcessAndIndexAsync("docs/manual.pdf");

// Search
var results = await client.Retriever.SearchAsync("installation guide", topK: 5);
```

### FAQ System
```csharp
// Index FAQs
await client.Indexer.IndexDocumentAsync(
    content: "Q: How to install? A: Run dotnet add package FluxIndex.SDK",
    documentId: "faq-001",
    metadata: new { category = "installation" }
);

// Semantic search
var results = await client.Retriever.SearchAsync("setup instructions", topK: 3);
```

### Document Analysis
```csharp
// Index large document set
var files = Directory.GetFiles("research", "*.pdf");
foreach (var file in files)
{
    await fileFlux.ProcessAndIndexAsync(file);
}

// Analyze with complex query
var results = await client.Retriever.SearchAsync(
    query: "impact of AI on healthcare",
    topK: 10,
    options: new SearchOptions { SearchStrategy = SearchStrategy.Adaptive }
);
```

## Configuration Examples

### Development Setup
```csharp
var client = new FluxIndexClientBuilder()
    .UseSQLite("dev.db")
    .Build();
```

### Production Setup
```csharp
var client = new FluxIndexClientBuilder()
    .UsePostgreSQL(connectionString)
    .UseOpenAI(apiKey)
    .UseRedisCache("localhost:6379")
    .Build();
```

### Hybrid with Custom AI
```csharp
services.AddFluxIndex()
    .AddPostgreSQLVectorStore()
    .AddSingleton<IEmbeddingService, CustomEmbeddingService>()
    .UseRedisCache();
```

## Performance Benchmarks

Based on .NET 9.0, Intel i7-1360P, [full results](../benchmarks/FluxIndex.Benchmarks/BENCHMARK_RESULTS.md):

| Operation | Size | Time | Notes |
|-----------|------|------|-------|
| Batch Indexing | 1K chunks | 24ms | 8-thread parallelism |
| Batch Indexing | 10K chunks | 188ms | 3.5 KB/chunk |
| Search | 1K chunks | 0.6ms | Keyword + Vector |
| Cache Hit | Semantic | <5ms | 95% similarity |

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

### FluxIndexClient
```csharp
client.Indexer.IndexDocumentAsync(content, documentId)
client.Indexer.IndexBatchAsync(documents)
client.Retriever.SearchAsync(query, topK, options)
```

### Search Options
```csharp
new SearchOptions
{
    SearchStrategy = SearchStrategy.Adaptive,
    VectorWeight = 0.7f,
    KeywordWeight = 0.3f,
    MinScore = 0.7f,
    UseCache = true
}
```

### Builder Pattern
```csharp
new FluxIndexClientBuilder()
    .UseSQLite(path)
    .UsePostgreSQL(connString)
    .UseOpenAI(apiKey, model)
    .UseAzureOpenAI(endpoint, apiKey, deployment)
    .UseRedisCache(connString)
    .Build()
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
