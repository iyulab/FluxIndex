# FluxIndex

[![CI/CD](https://github.com/iyulab/FluxIndex/actions/workflows/build-and-release.yml/badge.svg)](https://github.com/iyulab/FluxIndex/actions/workflows/build-and-release.yml)
[![NuGet](https://img.shields.io/nuget/v/FluxIndex.SDK.svg?label=FluxIndex.SDK)](https://www.nuget.org/packages/FluxIndex.SDK/)
[![License](https://img.shields.io/github/license/iyulab/FluxIndex)](LICENSE)

**RAG library for .NET 10.0** - Build semantic search and retrieval systems with vector + keyword hybrid search.

## Key Features

- **Hybrid Search** - Vector (semantic) + Keyword (BM25) with automatic strategy selection
- **High Performance** - Embedding cache (100% faster), batch indexing (24ms/1K chunks)
- **Local Reranking** - Cross-encoder neural reranking with automatic algorithmic fallback
- **Graph Traversal** - BFS/DFS, Dijkstra shortest path, PageRank-style importance
- **Vector Quantization** - Scalar (Int8/Int4), Product Quantization, Binary (32x compression)
- **Multiple Storage** - SQLite, PostgreSQL with pgvector
- **Local-First AI** - Built-in LMSupply (ONNX-based), bring your own embedding service
- **Document Processing** - PDF/DOCX/TXT via FileFlux, web crawling via WebFlux
- **MCP Server** - Model Context Protocol for AI assistant integration
- **Production Ready** - Redis caching, clean architecture, .NET 10.0

## Quick Start

```bash
dotnet add package FluxIndex.SDK
dotnet add package FluxIndex.Storage.SQLite
```

```csharp
using FluxIndex.SDK;

// 1. Setup (LMSupply embedding - no API key required)
var context = FluxIndexContext.CreateBuilder()
    .UseSQLite("fluxindex.db")
    .UseLMSupplyEmbedding()  // Built-in ONNX-based embedding
    .UseResilientLocalReranker()  // Auto fallback to algorithmic
    .Build();

// 2. Index
await context.Indexer.IndexDocumentAsync(
    "FluxIndex is a RAG library for .NET", "doc-001");

// 3. Search
var results = await context.Retriever.SearchAsync("RAG library", maxResults: 5);
```

### Using Custom Embedding Service

FluxIndex is AI provider-agnostic. Implement `IEmbeddingService` for your preferred provider:

```csharp
// Example: Custom OpenAI embedding service
public class MyOpenAIEmbeddingService : IEmbeddingService
{
    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        // Your OpenAI API call here
    }
}

// Register your implementation
services.AddSingleton<IEmbeddingService, MyOpenAIEmbeddingService>();

var context = FluxIndexContext.CreateBuilder()
    .UseSQLite("fluxindex.db")
    .UseEmbeddingService<MyOpenAIEmbeddingService>()
    .Build();
```

## MCP Server

FluxIndex provides Model Context Protocol (MCP) server for AI assistant integration.

**Available Tools**: `search`, `memorize`, `unmemorize`, `status`

See [FluxIndex.MCP](./src/FluxIndex.MCP/) for integration details.

## Performance

| Operation | Performance | Notes |
|-----------|-------------|-------|
| Batch Indexing | 24ms/1K chunks | 8-thread parallelism |
| Vector Search | 0.6ms/query | In-memory embeddings |
| Embedding Cache | 100% faster | Eliminates API calls |
| Semantic Cache | <5ms | Redis, 95% similarity |

Full benchmarks: [BENCHMARK_RESULTS.md](./benchmarks/FluxIndex.Benchmarks/BENCHMARK_RESULTS.md)

## Package Structure

| Package | Description |
|---------|-------------|
| **FluxIndex.Core** | Interfaces and core logic |
| **FluxIndex.SDK** | All-in-one SDK with LMSupply, FileFlux, WebFlux, FluxCurator, FluxImprover |
| **FluxIndex.Storage.SQLite** | SQLite vector store |
| **FluxIndex.Storage.PostgreSQL** | PostgreSQL with pgvector |
| **FluxIndex.Cache.Redis** | Redis semantic cache |

## Documentation

- [Getting Started](./docs/getting-started.md) - Setup and configuration
- [Tutorial](./docs/TUTORIAL.md) - Comprehensive examples
- [Architecture](./docs/architecture.md) - Design principles and patterns
- [Cheat Sheet](./docs/cheat-sheet.md) - Quick reference
- [Testing Guide](./docs/TESTING.md) - Unit and integration testing
- [LocalReranker Guide](./docs/LOCAL_RERANKER_GUIDE.md) - Neural reranking integration
- [Vector Quantization Guide](./docs/VECTOR_QUANTIZATION_GUIDE.md) - Memory optimization with quantization

## Examples

- [RealQualityTest](./samples/RealQualityTest/) - LMSupply + SQLite integration
- [WebFluxSample](./samples/WebFluxSample/) - Web crawling

## Requirements

- .NET 10.0 or later
- SQLite or PostgreSQL

## License

MIT License - see [LICENSE](LICENSE) file.

## Contributing

Contributions are welcome! Please feel free to submit issues and pull requests.
