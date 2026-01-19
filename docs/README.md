# FluxIndex Documentation

RAG infrastructure library for .NET - Simple, fast, and local-first.

## Documentation

| Document | Description |
|----------|-------------|
| [**GUIDE.md**](GUIDE.md) | Quick start, configuration, indexing, search, examples |
| [**REFERENCE.md**](REFERENCE.md) | Architecture, retrieval mechanisms, advanced topics |
| [**ADVANCED_RAG.md**](ADVANCED_RAG.md) | HyDE, Contextual Retrieval, Query Expansion |
| [**FLUXINDEX_PHILOSOPHY.md**](FLUXINDEX_PHILOSOPHY.md) | Core philosophy, role, and scope |

### Subdirectories

| Directory | Contents |
|-----------|----------|
| [design/](design/) | Technical design documents (FileVault, etc.) |
| [research/](research/) | Embedding strategy, polyglot persistence research |
| [archive/](archive/) | Completed roadmaps and historical research |

## Quick Start

```csharp
using FluxIndex.SDK;

// 1. Setup (InMemory embedding for testing)
var context = FluxIndexContext.CreateBuilder()
    .UseSQLite("fluxindex.db")
    .Build();  // InMemory embedding by default

// 2. Index
await context.Indexer.IndexDocumentAsync(
    content: "FluxIndex is a .NET RAG library.",
    documentId: "doc-001"
);

// 3. Search
var results = await context.Retriever.SearchAsync("RAG library", maxResults: 5);
```

## Installation

```bash
dotnet add package FluxIndex.SDK
dotnet add package FluxIndex.Storage.SQLite      # or PostgreSQL
```

## Features

- **Vector Search** - Semantic similarity with SQLite-vec or pgvector
- **Keyword Search** - BM25 algorithm for exact matching
- **Hybrid Search** - Reciprocal Rank Fusion combining both
- **Adaptive Search** - Auto-select strategy by query complexity
- **Neural Reranking** - Cross-encoder with LMSupply
- **Vector Quantization** - 4-32x memory compression
- **Graph Traversal** - BFS, DFS, PageRank for document relationships
- **AI Provider Agnostic** - Core provides abstract base classes for any embedding provider
- **FileVault** - Git-like file tracking for RAG indexing with folder monitoring

## Resources

- [Examples](../samples/) - Working code samples
- [Stack](../stack/) - Full RAG service with API and UI
- [GitHub](https://github.com/iyulab/FluxIndex) - Issues & contributions
