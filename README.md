# FluxIndex

[![CI/CD](https://github.com/iyulab/FluxIndex/actions/workflows/build-and-release.yml/badge.svg)](https://github.com/iyulab/FluxIndex/actions/workflows/build-and-release.yml)
[![NuGet](https://img.shields.io/nuget/v/FluxIndex.SDK.svg?label=FluxIndex.SDK)](https://www.nuget.org/packages/FluxIndex.SDK/)
[![License](https://img.shields.io/github/license/iyulab/FluxIndex)](LICENSE)

**RAG library for .NET 10.0** - Build semantic search and retrieval systems with vector + keyword hybrid search.

## Key Features

- **Hybrid Search** - Vector (semantic) + Keyword (BM25) with automatic strategy selection
- **High Performance** - Embedding cache (100% faster), batch indexing (24ms/1K chunks)
- **Multiple Storage** - SQLite, PostgreSQL with pgvector
- **AI Flexibility** - OpenAI, Azure OpenAI, or custom embedding services
- **Document Processing** - PDF/DOCX/TXT via FileFlux, web crawling via WebFlux
- **Production Ready** - Redis caching, clean architecture, .NET 10.0

## Quick Start

```bash
dotnet add package FluxIndex.SDK
dotnet add package FluxIndex.Storage.SQLite
```

```csharp
using FluxIndex.SDK;

// 1. Setup
var context = FluxIndexContext.CreateBuilder()
    .UseSQLite("fluxindex.db")
    .UseOpenAI("your-api-key", "text-embedding-3-small")
    .Build();

// 2. Index
await context.Indexer.IndexDocumentAsync(
    "FluxIndex is a RAG library for .NET", "doc-001");

// 3. Search
var results = await context.Retriever.SearchAsync("RAG library", maxResults: 5);
```

👉 **See [Tutorial](./docs/TUTORIAL.md) for complete examples and best practices**

## Performance

| Operation | Performance | Notes |
|-----------|-------------|-------|
| Batch Indexing | 24ms/1K chunks | 8-thread parallelism |
| Vector Search | 0.6ms/query | In-memory embeddings |
| Embedding Cache | 100% faster | Eliminates API calls |
| Semantic Cache | <5ms | Redis, 95% similarity |

Full benchmarks: [BENCHMARK_RESULTS.md](./benchmarks/FluxIndex.Benchmarks/BENCHMARK_RESULTS.md)

## Documentation

- [Getting Started](./docs/getting-started.md) - Setup and configuration
- [Tutorial](./docs/TUTORIAL.md) - Comprehensive examples
- [Architecture](./docs/architecture.md) - Design principles and patterns
- [Cheat Sheet](./docs/cheat-sheet.md) - Quick reference
- [Testing Guide](./docs/TESTING.md) - Unit and integration testing

## Examples

- [RealQualityTest](./samples/RealQualityTest/) - OpenAI + SQLite integration
- [FileFluxIndexSample](./samples/FileFluxIndexSample/) - PDF/DOCX processing
- [WebFluxSample](./samples/WebFluxSample/) - Web crawling
- [IntegrationTestSample](./samples/IntegrationTestSample/) - Integration testing patterns

## Requirements

- .NET 10.0 or later
- SQLite or PostgreSQL
- OpenAI API key (optional)

## License

MIT License - see [LICENSE](LICENSE) file.

## Contributing

See [development roadmap](./TASKS.md) for planned features.
