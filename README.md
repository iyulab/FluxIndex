# FluxIndex

[![CI/CD](https://github.com/iyulab/FluxIndex/actions/workflows/build-and-release.yml/badge.svg)](https://github.com/iyulab/FluxIndex/actions/workflows/build-and-release.yml)
[![NuGet](https://img.shields.io/nuget/v/FluxIndex.SDK.svg?label=FluxIndex.SDK)](https://www.nuget.org/packages/FluxIndex.SDK/)
[![License](https://img.shields.io/github/license/iyulab/FluxIndex)](LICENSE)

.NET 9.0 RAG library for building retrieval-augmented generation systems with vector and hybrid search.

## Overview

FluxIndex provides document indexing and semantic retrieval for .NET applications. It combines vector embeddings with keyword search using clean architecture principles and modular design.

## Features

- **Vector Search** - Semantic similarity using embeddings (SQLite-vec, pgvector)
- **Keyword Search** - BM25 algorithm for exact term matching
- **Hybrid Search** - Combines vector + keyword with Reciprocal Rank Fusion
- **Adaptive Search** - Automatic strategy selection based on query complexity
- **Embedding Cache** - In-memory cache for repeated queries (100% improvement)
- **Storage Options** - SQLite (dev), PostgreSQL (production)
- **AI Agnostic** - Works with OpenAI, Azure OpenAI, or custom services
- **Document Processing** - FileFlux integration for PDF/DOCX/TXT
- **Web Crawling** - WebFlux integration for web content extraction
- **Semantic Caching** - Redis-based similarity caching
- **Clean Architecture** - Modular DI-based design

## Installation

```bash
# Minimal setup (local development)
dotnet add package FluxIndex.SDK
dotnet add package FluxIndex.Storage.SQLite

# Production setup
dotnet add package FluxIndex.SDK
dotnet add package FluxIndex.AI.OpenAI
dotnet add package FluxIndex.Storage.PostgreSQL
dotnet add package FluxIndex.Cache.Redis

# Document processing (optional)
dotnet add package FluxIndex.Extensions.FileFlux
dotnet add package FluxIndex.Extensions.WebFlux
```

## Quick Start

```csharp
using FluxIndex.SDK;

// Setup with SQLite and OpenAI
var context = FluxIndexContext.CreateBuilder()
    .UseSQLite("fluxindex.db")
    .UseOpenAI("your-api-key", "text-embedding-3-small")
    .Build();

// Index documents
await context.Indexer.IndexDocumentAsync(
    content: "FluxIndex is a .NET RAG library for semantic search.",
    documentId: "doc-001"
);

// Search with adaptive strategy
var results = await context.Retriever.SearchAsync(
    query: "RAG library for .NET",
    maxResults: 5
);

foreach (var result in results)
{
    Console.WriteLine($"Score: {result.Score:F2}");
    Console.WriteLine($"Content: {result.DocumentChunk.Content}");
}
```

## Performance

Based on [benchmarks](./benchmarks/FluxIndex.Benchmarks/BENCHMARK_RESULTS.md) with .NET 9.0 on Intel i7-1360P:

| Operation | Dataset | Performance | Notes |
|-----------|---------|-------------|-------|
| Batch Indexing | 1,000 chunks | 24ms | 8-thread parallelism |
| Batch Indexing | 10,000 chunks | 188ms | 3.5 KB/chunk |
| Vector Search | 1,000 chunks | 0.6-0.7ms | In-memory embeddings |
| Embedding Cache Hit | Repeated query | 100% faster | Eliminates API calls |
| Hybrid Search | 100 chunks | 383ms avg | With OpenAI API |
| Semantic Cache | Similar query | <5ms | 95% similarity threshold |

**Optimization Features**:
- In-memory embedding cache for repeated queries
- Semantic caching with Redis for similar queries
- Optimal parallelism: 8 threads for batch operations
- Expected 30% latency reduction with realistic cache hit rates

## Advanced Usage

See [Tutorial](./docs/tutorial.md) for detailed examples:

- **Hybrid Search** - Combine vector and keyword search with RRF
- **Adaptive Search** - Automatic strategy selection based on query complexity
- **Embedding Cache** - In-memory cache for repeated queries
- **Semantic Caching** - Redis-based similarity caching for related queries
- **Document Processing** - FileFlux integration for PDF/DOCX/TXT
- **Web Crawling** - WebFlux integration for web content extraction
- **Batch Operations** - Efficient bulk indexing with parallelism

## Documentation

- [Getting Started](./docs/getting-started.md) - Setup and configuration
- [Tutorial](./docs/tutorial.md) - Comprehensive examples
- [Architecture](./docs/architecture.md) - Design principles
- [Cheat Sheet](./docs/cheat-sheet.md) - Quick reference

## Examples

- [RealWorldDemo](./samples/FluxIndex.RealWorldDemo/) - OpenAI + SQLite integration
- [FileFluxSample](./samples/FileFluxIndexSample/) - PDF/DOCX processing
- [WebFluxSample](./samples/WebFluxSample/) - Web crawling

## Requirements

- .NET 9.0 or later
- SQLite or PostgreSQL
- OpenAI API key (optional)

## License

MIT License - see [LICENSE](LICENSE) file.

## Contributing

See [development roadmap](./TASKS.md) for planned features.
