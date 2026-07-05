# FluxIndex Documentation

RAG infrastructure library for .NET - Simple, fast, and local-first.

## Quick Links

| Document | Description |
|----------|-------------|
| [**GUIDE.md**](GUIDE.md) | Storage modes, setup, indexing, search, examples |
| [**AI_PROVIDER_INTEGRATION.md**](AI_PROVIDER_INTEGRATION.md) | OpenAI, Azure, LMSupply, custom embedding/LLM integration |
| [**REFERENCE.md**](REFERENCE.md) | Architecture, retrieval mechanisms, advanced topics |
| [**ADVANCED_RAG.md**](ADVANCED_RAG.md) | HyDE, GraphRAG, Self-RAG, Corrective RAG |
| [**FILEVAULT_GUIDE.md**](FILEVAULT_GUIDE.md) | FileVault file-to-vector sync, folder watching |
| [**FLUXINDEX_PHILOSOPHY.md**](FLUXINDEX_PHILOSOPHY.md) | Core philosophy, role, and scope |
| [**MIGRATION.md**](MIGRATION.md) | Upgrade checklists (0.2.x → 0.13.x) |

---

## Storage Modes

FluxIndex automatically maximizes available capabilities - no feature toggles needed.

| Mode | Configuration | Components |
|------|---------------|------------|
| **Local** | `UseLocalStorage()` | SQLite (Vector + Graph + RDB + Cache) |
| **Full** | `UseBestInClass()` | PostgreSQL + Qdrant + Neo4j |
| **Custom** | Mix providers | Your choice of components |

```csharp
// Local mode (development, edge AI)
var ctx = FluxIndexContext.CreateBuilder()
    .UseLocalStorage("fluxindex.db")
    .Build();

// Full mode (production)
var ctx = FluxIndexContext.CreateBuilder()
    .UseBestInClass(pgConn, qdrantConfig, neo4jConfig)
    .Build();
```

See [GUIDE.md](GUIDE.md) for detailed setup instructions.

---

## Quick Start

```csharp
using FluxIndex.SDK;

// 1. Setup (InMemory embedding for testing)
var context = FluxIndexContext.CreateBuilder()
    .UseLocalStorage("fluxindex.db")
    .Build();

// 2. Index
await context.Indexer.IndexDocumentAsync(
    content: "FluxIndex is a .NET RAG library for semantic search.",
    documentId: "doc-001"
);

// 3. Search
var results = await context.Retriever.SearchAsync("RAG library", maxResults: 5);
```

---

## Installation

```bash
dotnet add package FluxIndex.SDK
dotnet add package FluxIndex.Storage.SQLite      # Local mode
# or
dotnet add package FluxIndex.Storage.PostgreSQL  # Production
dotnet add package FluxIndex.Storage.Qdrant      # High-performance vector
dotnet add package FluxIndex.Storage.Neo4j       # Graph database
```

---

## Features

### Search
- **Vector Search** - Semantic similarity with SQLite-vec or pgvector
- **Keyword Search** - BM25 algorithm for exact matching
- **Hybrid Search** - Reciprocal Rank Fusion (RRF) combining both
- **Adaptive Search** - Auto-select strategy by query complexity

### GraphRAG
- **Entity Graph** - Named entity extraction and relationship mapping
- **Community Detection** - Hierarchical clustering for global search
- **Graph Traversal** - BFS, DFS, Dijkstra, PageRank-style importance

### Optimization
- **Neural Reranking** - Cross-encoder with LMSupply
- **Vector Quantization** - 4-32x memory compression
- **Semantic Caching** - Query result caching with similarity matching

### Integration
- **AI Provider Agnostic** - Core provides abstract base classes
- **FileVault** - Git-like file tracking for RAG indexing
- **MCP Server** - Model Context Protocol for AI assistants

---

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│               SDK Layer (FluxIndex.SDK)                  │
│         FluxIndexContext, Builder Pattern                │
│      FileFlux, WebFlux, FluxCurator, FluxImprover       │
├─────────────────────────────────────────────────────────┤
│              Storage Providers                           │
│  SQLite | PostgreSQL | Qdrant | Neo4j | Redis           │
├─────────────────────────────────────────────────────────┤
│              Core (FluxIndex.Core)                       │
│   BM25, Graph Traversal, Quantization, Base Classes     │
└─────────────────────────────────────────────────────────┘
```

---

## Resources

- [Examples](../samples/) - Working code samples
- [GitHub](https://github.com/iyulab/FluxIndex) - Issues & contributions
