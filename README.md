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
- **AI Provider Agnostic** - Core provides abstract base classes, bring your own embedding service
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

// 1. Setup (InMemory embedding for testing)
var context = FluxIndexContext.CreateBuilder()
    .UseSQLite("fluxindex.db")
    .Build();

// 2. Index
await context.Indexer.IndexDocumentAsync(
    "FluxIndex is a RAG library for .NET", "doc-001");

// 3. Search
var results = await context.Retriever.SearchAsync("RAG library", maxResults: 5);
```

> **Note (testing embedder)**: without a registered `IEmbeddingService` the builder falls
> back to `InMemoryEmbeddingService`, whose vectors are deterministic but **not semantically
> meaningful** — similarity scores cluster near 0, so with the default `minScore` a search
> typically returns **no results**. Pass `minScore: 0` while smoke-testing, and register a
> real embedding service (below) for meaningful retrieval.

### Using Custom Embedding Service

FluxIndex is AI provider-agnostic. Extend `EmbeddingServiceBase` for your preferred provider:

```csharp
// Example: LMSupply embedding (local ONNX-based, no API key)
public class LMSupplyEmbedder : EmbeddingServiceBase, IAsyncDisposable
{
    private readonly IEmbeddingModel _model;
    private LMSupplyEmbedder(IEmbeddingModel model) => _model = model;

    public static async Task<LMSupplyEmbedder> CreateAsync(string modelId = "default")
    {
        var model = await LocalEmbedder.LoadAsync(modelId);
        return new LMSupplyEmbedder(model);
    }

    protected override async Task<float[]> EmbedCoreAsync(string text, CancellationToken ct)
        => await _model.EmbedAsync(text, ct);

    public override int GetEmbeddingDimension() => _model.Dimensions;
    public override string GetModelName() => _model.ModelId;
    public ValueTask DisposeAsync() => _model.DisposeAsync();
}

// Register and use
var context = FluxIndexContext.CreateBuilder()
    .UseSQLite("fluxindex.db")
    .ConfigureServices(s => s.AddSingleton<IEmbeddingService>(
        LMSupplyEmbedder.CreateAsync().GetAwaiter().GetResult()))
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

| Package | NuGet | Description |
|---------|-------|-------------|
| **FluxIndex.Core** | [![NuGet](https://img.shields.io/nuget/v/FluxIndex.Core.svg)](https://www.nuget.org/packages/FluxIndex.Core/) | Interfaces, abstract base classes, and core logic |
| **FluxIndex.SDK** | [![NuGet](https://img.shields.io/nuget/v/FluxIndex.SDK.svg)](https://www.nuget.org/packages/FluxIndex.SDK/) | All-in-one SDK with FileFlux, WebFlux, FluxCurator, FluxImprover |
| **FluxIndex.Storage.SQLite** | [![NuGet](https://img.shields.io/nuget/v/FluxIndex.Storage.SQLite.svg)](https://www.nuget.org/packages/FluxIndex.Storage.SQLite/) | SQLite vector store |
| **FluxIndex.Storage.PostgreSQL** | [![NuGet](https://img.shields.io/nuget/v/FluxIndex.Storage.PostgreSQL.svg)](https://www.nuget.org/packages/FluxIndex.Storage.PostgreSQL/) | PostgreSQL with pgvector |
| **FluxIndex.Storage.Neo4j** | [![NuGet](https://img.shields.io/nuget/v/FluxIndex.Storage.Neo4j.svg)](https://www.nuget.org/packages/FluxIndex.Storage.Neo4j/) | Neo4j graph database |
| **FluxIndex.Storage.Qdrant** | [![NuGet](https://img.shields.io/nuget/v/FluxIndex.Storage.Qdrant.svg)](https://www.nuget.org/packages/FluxIndex.Storage.Qdrant/) | Qdrant vector database |
| **FluxIndex.Cache.Redis** | [![NuGet](https://img.shields.io/nuget/v/FluxIndex.Cache.Redis.svg)](https://www.nuget.org/packages/FluxIndex.Cache.Redis/) | Redis semantic cache |

> **Moved:** File-to-vector synchronization (formerly `FluxIndex.Extensions.FileVault`) was extracted to the **[FluxFeed](https://github.com/iyulab/FluxFeed)** repository in 0.16.0. Install `FluxFeed` for git-like file tracking / folder-monitoring document ingestion; it feeds into FluxIndex.

### Which package do I need?

| Scenario | Packages |
|----------|---------|
| Embeddings + vector search only (no native deps, no document parsing) | `FluxIndex.Core` + storage |
| Full RAG pipeline (PDF, DOCX, HWP, web crawling) | `FluxIndex.SDK` + storage |
| File system monitoring + auto-indexing (document ingestion) | [`FluxFeed`](https://github.com/iyulab/FluxFeed) (feeds into FluxIndex) |
| Local AI embedding (ONNX, no API key required) | `FluxIndex.Providers.LMSupply` |

**Minimal setup** — bring your own embedding service, no native binaries:

```bash
dotnet add package FluxIndex.Core
dotnet add package FluxIndex.Storage.SQLite
```

**Full SDK** — includes document processing (PDF, DOCX, HWP, web crawling):

```bash
dotnet add package FluxIndex.SDK
dotnet add package FluxIndex.Storage.SQLite
```

## Documentation

- [Guide](./docs/GUIDE.md) - Quick start and configuration
- [Reference](./docs/REFERENCE.md) - Architecture and API reference
- [Advanced RAG](./docs/ADVANCED_RAG.md) - HyDE, Contextual Retrieval, Query Expansion
- [Philosophy](./docs/FLUXINDEX_PHILOSOPHY.md) - Core principles and design philosophy

## Examples

- [RealQualityTest](./samples/RealQualityTest/) - LMSupply + SQLite integration
- [WebFluxSample](./samples/WebFluxSample/) - Web crawling with WebFlux
- [ChunkingQualityTest](./samples/ChunkingQualityTest/) - FileFlux chunking analysis
- [FileFluxIndexSample](./samples/FileFluxIndexSample/) - Document indexing workflow

## Requirements

- .NET 10.0 or later
- SQLite or PostgreSQL

## License

MIT License - see [LICENSE](LICENSE) file.

## Contributing

Contributions are welcome! Please feel free to submit issues and pull requests.
