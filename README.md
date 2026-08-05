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
- **Document Processing** - PDF/DOCX/TXT via FileFlux, web crawling via WebFlux (opt-in `FluxIndex.Integrations.*` packages)
- **MCP Server** - Model Context Protocol for AI assistant integration
- **Production Ready** - Redis caching, clean architecture, .NET 10.0

## Quick Start

```bash
dotnet add package FluxIndex.SDK
dotnet add package FluxIndex.Storage.SQLite
```

```csharp
using FluxIndex.SDK;
using FluxIndex.Storage.SQLite;

// 1. Setup (InMemory embedding for testing)
// UseSQLite() selects the provider; AddSQLiteStorage() registers it. Both are required —
// Build() throws if you name a store without registering it.
// Build() also creates the schema for every component it enables (vector store, graph store,
// entity graph, semantic cache), touching only the tables those components own — see
// docs/GUIDE.md "What Build() provisions" to opt out and manage the schema yourself.
var context = FluxIndexContext.CreateBuilder()
    .UseSQLite("fluxindex.db")
    .AddSQLiteStorage()
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
    .AddSQLiteStorage()
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
| **FluxIndex.SDK** | [![NuGet](https://img.shields.io/nuget/v/FluxIndex.SDK.svg)](https://www.nuget.org/packages/FluxIndex.SDK/) | RAG orchestration core — context, indexer, retriever, DI helpers. No pipeline dependencies |
| **FluxIndex.Storage.SQLite** | [![NuGet](https://img.shields.io/nuget/v/FluxIndex.Storage.SQLite.svg)](https://www.nuget.org/packages/FluxIndex.Storage.SQLite/) | SQLite vector store |
| **FluxIndex.Storage.PostgreSQL** | [![NuGet](https://img.shields.io/nuget/v/FluxIndex.Storage.PostgreSQL.svg)](https://www.nuget.org/packages/FluxIndex.Storage.PostgreSQL/) | PostgreSQL with pgvector |
| **FluxIndex.Storage.Neo4j** | [![NuGet](https://img.shields.io/nuget/v/FluxIndex.Storage.Neo4j.svg)](https://www.nuget.org/packages/FluxIndex.Storage.Neo4j/) | Neo4j graph database |
| **FluxIndex.Storage.Qdrant** | [![NuGet](https://img.shields.io/nuget/v/FluxIndex.Storage.Qdrant.svg)](https://www.nuget.org/packages/FluxIndex.Storage.Qdrant/) | Qdrant vector database |
| **FluxIndex.Cache.Redis** | [![NuGet](https://img.shields.io/nuget/v/FluxIndex.Cache.Redis.svg)](https://www.nuget.org/packages/FluxIndex.Cache.Redis/) | Redis semantic cache |
| **FluxIndex.Integrations.FileFlux** | [![NuGet](https://img.shields.io/nuget/v/FluxIndex.Integrations.FileFlux.svg)](https://www.nuget.org/packages/FluxIndex.Integrations.FileFlux/) | Document parsing/chunking + the document processing pipeline |
| **FluxIndex.Integrations.WebFlux** | [![NuGet](https://img.shields.io/nuget/v/FluxIndex.Integrations.WebFlux.svg)](https://www.nuget.org/packages/FluxIndex.Integrations.WebFlux/) | Web content ingestion |
| **FluxIndex.Integrations.FluxCurator** | [![NuGet](https://img.shields.io/nuget/v/FluxIndex.Integrations.FluxCurator.svg)](https://www.nuget.org/packages/FluxIndex.Integrations.FluxCurator/) | Text preprocessing (PII detection, intelligent splitting) |
| **FluxIndex.Integrations.FluxImprover** | [![NuGet](https://img.shields.io/nuget/v/FluxIndex.Integrations.FluxImprover.svg)](https://www.nuget.org/packages/FluxIndex.Integrations.FluxImprover/) | LLM-based chunk quality enhancement |

> **Moved:** File-to-vector synchronization (formerly `FluxIndex.Extensions.FileVault`) was extracted to the **[FluxFeed](https://github.com/iyulab/FluxFeed)** repository in 0.16.0. Install `FluxFeed` for git-like file tracking / folder-monitoring document ingestion; it feeds into FluxIndex.

### Storage capability matrix

Metadata filtering (`SearchAsync(..., filters:)`) is honored by **every** store — stores without
native pushdown fall back to a correctness backstop applied before the topK trim. Native pushdown
matters for recall/performance at scale:

| Store | Search metadata filter | `DeleteByFilterAsync` | Notes |
|-------|------------------------|------------------------|-------|
| PostgreSQL | ✅ native (jsonb `@>`, multi-value = per-element OR) | ✅ single SQL DELETE | Add a GIN index on `Metadata` for large collections: `CREATE INDEX ON vectors USING gin (metadata jsonb_path_ops);` |
| Qdrant | ✅ native (payload filter, multi-value = MatchAny) | ✅ | Payload indexes created on startup |
| SQLite (sqlite-vec) | ✅ post-KNN with over-fetch | ✅ | vec0 cannot index metadata; KNN window is widened ×3 when filters are present |
| SQLite (in-memory scan) | ✅ pre-trim | ✅ | Full scan store |
| InMemory (SDK) | ✅ pre-trim | ✅ | |

Filter semantics (identical across every store):

- **Keys combine with AND** — a chunk must satisfy every filter entry.
- **Scalar value** → equality. Values compare by their JSON text representation (`"true"`,
  invariant-culture numbers, ordinal strings), so a value that round-trips through a JSON column
  still matches the raw value you filter on. The same semantics apply in the SDK's `Retriever`.
- **Collection value** (`List<string>`, arrays, JSON arrays …) → **match ANY element** (OR within
  the key — Qdrant MatchAny, PostgreSQL per-element jsonb containment). One query replaces an
  N-way fan-out: `filters: new() { ["document_id"] = fileHashes }`.
- **Unsupported values** (arbitrary objects, nested/empty collections) **throw
  `ArgumentException`** — never a silent zero-result.

Filters match **chunk** metadata. The `metadata` argument of `Indexer.IndexDocumentAsync(content,
documentId, metadata)` and `IndexChunksAsync(chunks, documentId, metadata)` is copied onto that
document's chunks for exactly this reason — a chunk's own metadata wins on key collision:

```csharp
await context.Indexer.IndexDocumentAsync(
    "tenant content", "doc-001", new() { ["workspace_id"] = "ws-a" });

var scoped = await context.Retriever.SearchAsync(
    "content", filter: new() { ["workspace_id"] = "ws-a" });

await vectorStore.DeleteByFilterAsync(new() { ["workspace_id"] = "ws-a" });
```

> **Keyword leg (since 0.22.0)**: indexing populates the BM25 keyword index, and both
> `AddSQLiteStorage()` and `AddPostgreSQLStorage()` persist it in the same database as the vectors — so
> `KeywordSearchAsync`/`HybridSearchAsync` keep working after a restart.
> `WithIndexerOptions(o => o.IndexKeyword = false)` stops the indexer adding to it — which means
> keyword and hybrid search have nothing to match against, so use it only if you do not use them.
>
> | Storage | Keyword index | Survives restart |
> |---------|---------------|------------------|
> | SQLite (`AddSQLiteStorage`) | same database as the vectors | ✅ |
> | PostgreSQL (`AddPostgreSQLStorage`) | same database as the vectors (**new in 0.23.0**) | ✅ |
> | Qdrant, other stores | process memory | ❌ — hybrid degrades to vector-only, and a warning is logged |
>
> Ranking is identical on every SQL backend: the BM25 scoring and the index schema are shared, and only
> SQL dialect differs per store. Vector search and metadata filtering are unaffected by the keyword leg.
> Documents indexed before the keyword index existed need one reindex to appear in it.

#### Scoping the keyword leg (since 0.25.0)

The keyword index takes the same filter vocabulary as the vector store, so one filter object scopes
both legs of a hybrid index:

```csharp
var scoped = await keywordSearch.SearchAsync("quarterly report", new KeywordSearchOptions
{
    MaxResults = 10,
    MetadataFilter = new Dictionary<string, object> { ["workspace_id"] = "ws-a" }
});

// Symmetric with IVectorStore.DeleteByFilterAsync - one call clears a scope from both legs.
int removed = await keywordSearch.DeleteByFilterAsync(
    new Dictionary<string, object> { ["workspace_id"] = "ws-a" });
```

- **The filter is pushed into the query, not applied to the results.** `MaxResults` selects the top N
  *within* the scope. Filtering after truncation would return nothing whenever another scope's
  documents fill the global top N — a false negative that grows with the size of the shared index.
- **Semantics match the vector store's**: keys AND together, a collection value matches any element,
  and an unfilterable value throws `ArgumentException` rather than silently widening the filter.
- **Only scalar metadata is filterable** (strings, numbers, booleans, dates, GUIDs, and collections of
  those). Object-valued metadata stays readable on the returned chunk but cannot be filtered on.
- Values are compared by the same text form on every backend, so a number indexed as `7` matches a
  filter supplied as `7` without the caller knowing the index stores text. Chunks indexed before
  0.25.0 need one reindex to gain the filter dimension.

#### Scoping a hybrid query

A scope belongs to the query, so declare it once and both legs take it:

```csharp
var results = await context.Retriever.SearchAsync("quarterly report", new SearchOptions
{
    TopK = 10,
    UseHybridSearch = true,
    MetadataFilters = { ["workspace_id"] = "ws-a" }   // applies to the vector AND keyword legs
});
```

> ⚠️ **Changed in 0.25.0.** Before this, `MetadataFilters` was honoured by vector-only search and
> **dropped on the hybrid path** — turning hybrid search on silently widened results to the whole
> index. If you worked around it, the workaround is no longer needed. The same applies to Qdrant's
> hybrid service, which passed no filter on either leg.

At the Core layer the equivalent is `HybridSearchOptions.Filters`. A leg that carries its own
non-empty filter (`VectorOptions.Filters` / `SparseOptions.Filters`) keeps it, so you can still
differ per leg deliberately; `EffectiveVectorFilters` / `EffectiveSparseFilters` report what will
actually be applied.

A filter value the keyword index cannot match throws `ArgumentException` — the sparse leg degrades
to empty on a *backend* failure, but a malformed filter is a caller error and reaches you rather
than quietly returning unscoped results.

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

## Requirements

- .NET 10.0 or later
- SQLite or PostgreSQL

## License

MIT License - see [LICENSE](LICENSE) file.

## Contributing

Contributions are welcome! Please feel free to submit issues and pull requests.
