# FluxIndex Guide

RAG library for .NET - Complete usage guide.

**Related Guides:**
- [Advanced RAG Services](./ADVANCED_RAG.md) - Dynamic fusion, listwise reranking, entity extraction, community detection
- [API Reference](./REFERENCE.md) - Complete API documentation

## Quick Start

### Installation

```bash
# Required
dotnet add package FluxIndex.SDK
dotnet add package FluxIndex.Storage.SQLite      # or PostgreSQL

# Optional
dotnet add package FluxIndex.AI.OpenAI           # AI embeddings
dotnet add package FluxIndex.AI.LocalReranker    # Neural reranking
dotnet add package FluxIndex.Extensions.FileFlux # PDF/DOCX processing
dotnet add package FluxIndex.Cache.Redis         # Semantic caching
```

### Basic Usage

```csharp
using FluxIndex.SDK;

// 1. Setup
var context = FluxIndexContext.CreateBuilder()
    .UseSQLite("fluxindex.db")
    .UseOpenAI("your-api-key", "text-embedding-3-small")  // Optional
    .Build();

// 2. Index
await context.Indexer.IndexDocumentAsync(
    content: "FluxIndex is a .NET RAG library.",
    documentId: "doc-001"
);

// 3. Search
var results = await context.Retriever.SearchAsync("RAG library", maxResults: 5);

foreach (var r in results)
    Console.WriteLine($"[{r.Score:F2}] {r.DocumentChunk.Content}");
```

---

## Configuration

### Development

```csharp
var context = FluxIndexContext.CreateBuilder()
    .UseSQLite("dev.db")
    .Build();
```

### Production

```csharp
var context = FluxIndexContext.CreateBuilder()
    .UsePostgreSQL("Host=localhost;Database=fluxindex;...")
    .UseOpenAI(apiKey, "text-embedding-3-small")
    .UseResilientLocalReranker(o => o.ModelId = "quality")
    .UseRedisCache("localhost:6379")
    .Build();
```

### appsettings.json

```json
{
  "FluxIndex": {
    "ConnectionString": "Data Source=fluxindex.db",
    "OpenAI": { "ApiKey": "sk-...", "Model": "text-embedding-3-small" }
  }
}
```

---

## Indexing

### Single Document

```csharp
await context.Indexer.IndexDocumentAsync(
    content: "Document content...",
    documentId: "doc-001",
    metadata: new Dictionary<string, object>
    {
        ["category"] = "tech",
        ["author"] = "John"
    }
);
```

### Batch Indexing

```csharp
await context.Indexer.IndexBatchAsync(documents, parallelism: 8);
// Performance: 1K chunks ~24ms, 10K chunks ~188ms
```

### File Processing (PDF, DOCX)

```csharp
services.AddFileFluxIntegration(options =>
{
    options.DefaultChunkingStrategy = "Semantic";
    options.DefaultMaxChunkSize = 1024;
    options.DefaultOverlapSize = 128;
});

var fileFlux = provider.GetRequiredService<FileFluxIntegration>();
await fileFlux.ProcessAndIndexAsync("document.pdf");
```

---

## Search Strategies

| Strategy | Use Case | Performance |
|----------|----------|-------------|
| **Adaptive** (default) | Auto-select best approach | Varies |
| **Vector** | Semantic similarity | ~100ms |
| **Keyword** (BM25) | Exact term matching | ~50ms |
| **Hybrid** | Vector + Keyword combined | ~150ms |

### Adaptive Search (Recommended)

```csharp
// Automatically selects best strategy based on query
var results = await context.Retriever.SearchAsync(
    query: "How does machine learning work?",
    maxResults: 10
);
```

### Hybrid Search

```csharp
// Combines BM25 keyword search + vector semantic search
var results = await context.Retriever.SearchAsync(
    query: "neural network implementation",
    maxResults: 10
);
```

---

## Real-World Examples

### Support Chatbot

```csharp
public class SupportChatbot
{
    private readonly IFluxIndexContext _context;

    public async Task<string> Answer(string question)
    {
        var docs = await _context.Retriever.SearchAsync(question, maxResults: 5);
        var context = string.Join("\n", docs.Select(d => d.DocumentChunk.Content));

        // Use LLM to generate response with retrieved context
        return await _llm.GenerateAsync($"Context:\n{context}\n\nQuestion: {question}");
    }
}
```

### Document Q&A

```csharp
// Index company documents
var files = Directory.GetFiles("docs", "*.pdf");
foreach (var file in files)
    await fileFlux.ProcessAndIndexAsync(file);

// Search with source citations
var results = await context.Retriever.SearchAsync("remote work policy");
var sources = results.Select(r => r.DocumentChunk.Metadata["file_path"]).Distinct();
```

---

## Advanced RAG

### Self-Correcting Search

```csharp
// Self-RAG: Iterative quality improvement
var selfRag = provider.GetRequiredService<ISelfRAGService>();
var result = await selfRag.SearchAsync("complex technical question");
// Automatically refines results until quality threshold met

// Corrective RAG: Document grading + web augmentation
var crag = provider.GetRequiredService<ICorrectiveRAGService>();
var corrected = await crag.RetrieveWithCorrectionAsync("specific query");
// Grades documents, replaces low-quality with web-augmented content
```

### Intelligent Query Routing

```csharp
// Agentic Router: Auto-selects best retrieval strategy
var router = provider.GetRequiredService<IAgenticRetrievalRouter>();

var result = await router.RouteAndRetrieveAsync("What is machine learning?");
// Automatically chooses: SemanticSearch, HybridSearch, SelfRAG, etc.

// Check routing decision
Console.WriteLine($"Strategy: {result.Decision.PrimaryStrategy}");
Console.WriteLine($"Query Type: {result.Decision.QueryAnalysis.Type}");
```

### GraphRAG for Complex Questions

```csharp
// Entity-aware search for relational queries
var graphRag = provider.GetRequiredService<IGraphRAGService>();

var result = await graphRag.SearchAsync(
    "How are Machine Learning and Neural Networks related?",
    new GraphRAGOptions { EnableGlobalSearch = true });
```

### DI Registration

```csharp
services.AddFluxIndexCore();

// Add advanced RAG services
services.AddSelfRAGService();
services.AddCorrectiveRAGService();
services.AddAgenticRetrievalRouter();
services.AddGraphRAGServices();
```

---

## Performance Tips

| Optimization | Benefit |
|-------------|---------|
| Embedding Cache (built-in) | 100% improvement for repeated queries |
| Redis Semantic Cache | ~95% improvement for similar queries |
| Batch Indexing (8 threads) | 50K chunks/second throughput |
| PostgreSQL + pgvector | Production scalability |
| LocalReranker | Better relevance ranking |

### Enable Caching

```csharp
var context = FluxIndexContext.CreateBuilder()
    .UseSQLite("fluxindex.db")
    .UseOpenAI(apiKey, "text-embedding-3-small")
    .UseRedisCache("localhost:6379")  // Semantic caching
    .Build();

// First query: ~1000ms (API call)
// Same query: <1ms (cache hit)
// Similar query: <5ms (Redis semantic cache)
```

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Slow search | Enable Redis caching, verify embedding cache |
| Database locked | Use singleton pattern, switch to PostgreSQL |
| Rate limits | Reduce parallelism, add delays |
| Poor relevance | Adjust chunk size (512-1024), try different strategy |
| Out of memory | Process in smaller batches |

---

## API Quick Reference

```csharp
// Builder
FluxIndexContext.CreateBuilder()
    .UseSQLite(path) / .UsePostgreSQL(conn)
    .UseOpenAI(key, model) / .UseAzureOpenAI(endpoint, key, deployment)
    .UseResilientLocalReranker(options)
    .UseRedisCache(conn)
    .Build()

// Indexing
context.Indexer.IndexDocumentAsync(content, documentId, metadata)
context.Indexer.IndexBatchAsync(documents, parallelism)

// Search
context.Retriever.SearchAsync(query, maxResults, cancellationToken)

// Results
result.Score          // 0.0 - 1.0
result.DocumentChunk.Content
result.DocumentChunk.Metadata
```

---

## Next Steps

- [Technical Reference](REFERENCE.md) - Architecture, retrieval mechanisms, advanced topics
- [Examples](../samples/) - Working code samples
- [GitHub](https://github.com/iyulab/FluxIndex) - Issues & contributions
