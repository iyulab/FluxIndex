# FluxIndex: Core Philosophy, Role, Scope & Goals

A comprehensive definition of FluxIndex's identity and boundaries as a RAG infrastructure library.

---

## 1. Core Philosophy

### 1.1 Clean Architecture First

FluxIndex strictly follows Clean Architecture principles with **unidirectional dependency flow**:

```
┌─────────────────────────────────────────────────────┐
│               SDK Layer                              │
│             (FluxIndex.SDK)                         │
│  FluxIndexContext, Builder Pattern                  │
├─────────────────────────────────────────────────────┤
│              Provider Packages (Optional)            │
│  FluxIndex.AI.*    FluxIndex.Storage.*              │
│  FluxIndex.Cache.* FluxIndex.Extensions.*           │
├─────────────────────────────────────────────────────┤
│              Core Infrastructure                     │
│              (FluxIndex.Core)                       │
│   Domain + Application (NO AI Dependencies)         │
└─────────────────────────────────────────────────────┘
```

**Key Principle**: Core layer has ZERO AI provider dependencies. All AI capabilities are optional add-ons.

### 1.2 AI Provider Agnosticism

```csharp
// Core works without any AI provider
services.AddFluxIndexCore();  // BM25, local algorithms

// AI providers are optional plug-ins
services.AddFluxIndexOpenAI(config);    // Optional
services.AddFluxIndexAnthropic(config); // Optional
services.AddCustomEmbedding<T>();       // Custom implementations
```

### 1.3 Infrastructure Over Application

FluxIndex is an **infrastructure library**, NOT an application framework:

| Include (Infrastructure) | Exclude (Application) |
|-------------------------|----------------------|
| Type adapters & converters | Application-specific caching policies |
| Service wrappers for unified API | Custom parallelization patterns |
| DI registration convenience methods | Business workflow orchestration |
| Interface definitions | UI/UX callbacks |
| Basic integration workflows | Distributed caching implementations |

### 1.4 Local-First / Edge AI

FluxIndex supports **privacy-focused, local-first architecture**:

- SQLite for zero-infrastructure deployment (Vector + Graph + RDB + Cache)
- In-process search with no network latency
- MCP server for AI assistant integration
- Supports 100K-1M vectors in edge environments

### 1.5 Three Storage Modes

FluxIndex provides three deployment patterns:

| Mode | Configuration | Components |
|------|---------------|------------|
| **Local** | `UseLocalStorage()` | SQLite handles all (Vector, Graph, RDB, Cache) |
| **Full** | `UseBestInClass()` | PostgreSQL + Qdrant + Neo4j (best-in-class) |
| **Custom** | Mix providers | User-defined combination |

**Auto-Maximize Principle**: RDB, VectorDB, GraphDB are not features to toggle. They automatically activate based on configured storage.

### 1.6 Modular & Composable

Each capability is an **independent, optional module**:

- Storage: SQLite, PostgreSQL, Qdrant, Neo4j
- AI: OpenAI, Azure, LMSupply, custom implementations
- Cache: Redis, In-Memory, SQLite
- Extensions: FileFlux, WebFlux, FluxCurator, FluxImprover

---

## 2. Role & Purpose

### 2.1 Primary Identity

> **"FluxIndex: The bridge between document chunks and search-ready context"**

FluxIndex transforms raw text chunks into **indexed, enriched, searchable units** optimized for RAG retrieval.

### 2.2 Position in Ecosystem

```
┌─────────────┐     ┌─────────────┐
│  FileFlux   │     │  WebFlux    │
│  (Files)    │     │  (URLs)     │
└──────┬──────┘     └──────┬──────┘
       │  Text Chunks       │
       └─────────┬──────────┘
                 ▼
        ┌─────────────────┐
        │   FluxIndex     │
        │                 │
        │ • Enrichment    │
        │ • Embedding     │
        │ • Indexing      │
        │ • Search        │
        │ • Reranking     │
        └────────┬────────┘
                 │
                 ├─────────────────┐
                 │                 ▼
                 │        ┌─────────────────┐
                 │        │  FluxImprover   │
                 │        │  (Quality)      │
                 │        │                 │
                 │        │ • Enrichment    │
                 │        │ • QA Generation │
                 │        │ • Evaluation    │
                 │        │ • Filtering     │
                 │        └────────┬────────┘
                 │                 │
                 └─────────┬───────┘
                           ▼ Context
        ┌─────────────────────────────────┐
        │          LLM / Agent            │
        └─────────────────────────────────┘
```

### 2.3 FluxImprover Integration

FluxImprover is a **quality enhancement library** that complements FluxIndex's indexing and search capabilities:

| Component | Role | Integration |
|-----------|------|-------------|
| **ChunkEnrichmentService** | LLM-based chunk enrichment | Summaries, keywords generation |
| **QAGenerationService** | Q&A pair generation | Training data, evaluation sets |
| **RAGEvaluationService** | RAG pipeline evaluation | Answerability, Faithfulness, Relevancy |
| **ChunkFilteringService** | 3-stage LLM filtering | Initial → Self-Reflection → Critic |

**Integration Pattern**:
```csharp
// FluxIndex.Extensions.FluxImprover bridges the two libraries
services.AddFluxImproverIntegration();  // Adapters + Pipeline
services.AddFluxImproverFullIntegration();  // + Parallel + Cached executors
```

**FluxImprover Pipeline**:
```
Chunk → [Enrichment] → [QA Generation] → [Evaluation] → Enhanced Chunk + QA Dataset
```

### 2.4 Core Responsibilities

| Responsibility | Description |
|---------------|-------------|
| **Hybrid Search** | Vector (semantic) + BM25 (keyword) + RRF fusion |
| **Embedding Management** | Generate, cache, and store vector embeddings |
| **Contextual Enrichment** | Generate contextual headers, summaries |
| **Reranking** | Neural (LocalReranker) + algorithmic fallback |
| **Graph Traversal** | BFS, DFS, Dijkstra, PageRank-style importance |
| **Vector Quantization** | Scalar, Product, Binary quantization |
| **Semantic Caching** | Query-level caching with similarity matching |
| **MCP Integration** | Model Context Protocol server for AI assistants |

---

## 3. Scope Definition

### 3.1 What FluxIndex DOES

#### Search & Retrieval
- [x] Vector search (cosine similarity, L2 distance)
- [x] Keyword search (BM25 algorithm)
- [x] Hybrid search (RRF, WeightedSum, Product, Maximum, HarmonicMean)
- [x] Adaptive strategy selection based on query complexity
- [x] Small-to-Big context expansion

#### Processing & Enrichment
- [x] Contextual header generation (rule-based + LLM hybrid)
- [x] Metadata enrichment (keywords, topics, entities)
- [x] Chunk relationship analysis (Sequential, Semantic, Hierarchical, Reference)
- [x] Quality scoring and importance calculation

#### Storage & Optimization
- [x] Vector storage (SQLite, PostgreSQL/pgvector)
- [x] Embedding caching (in-memory, Redis)
- [x] Vector quantization (Int8, Int4, Product, Binary)
- [x] HNSW auto-tuning for PostgreSQL
- [x] Batch indexing with parallelism

#### Advanced Features
- [x] Graph traversal (BFS, DFS, Dijkstra, cycle detection)
- [x] PageRank-style document importance
- [x] Transitive closure computation
- [x] MCP server for AI assistant integration
- [x] HyDE & QuOTE query transformation

### 3.2 What FluxIndex DOES NOT Do

#### Delegated to FileFlux
- [ ] File parsing (PDF, DOCX, XLSX, TXT, etc.)
- [ ] Text chunking and segmentation
- [ ] HeadingPath extraction
- [ ] PageNumber extraction
- [ ] ContextDependency scoring
- [ ] Basic topics/keywords extraction (AI-based)

#### Delegated to WebFlux
- [ ] Web crawling and content extraction
- [ ] SEO/OG metadata extraction
- [ ] Breadcrumbs extraction
- [ ] robots.txt, sitemap.xml handling

#### Application Responsibility
- [ ] Application-specific caching strategies (TTL policies, eviction rules)
- [ ] Custom parallelization patterns (batch sizes, concurrency limits)
- [ ] Business workflow orchestration
- [ ] UI/UX callbacks and progress reporting
- [ ] Distributed caching infrastructure decisions

### 3.3 Responsibility Matrix

| Feature | FileFlux | WebFlux | FluxIndex | FluxImprover |
|---------|:--------:|:--------:|:---------:|:------------:|
| File parsing | ✅ | - | - | - |
| Web crawling | - | ✅ | - | - |
| Text extraction | ✅ | ✅ | - | - |
| Chunking | ✅ | ✅ | - | - |
| AI topics/keywords | ✅ | - | - | - |
| Quality Score | ✅ | - | - | - |
| HeadingPath | ✅ (requested) | ✅ (requested) | - | - |
| PageNumber | ✅ (requested) | - | - | - |
| SEO/OG metadata | - | ✅ (requested) | - | - |
| Embedding generation | - | - | ✅ | - |
| Vector storage | - | - | ✅ | - |
| Hybrid search | - | - | ✅ | - |
| Reranking | - | - | ✅ | - |
| Contextual Header | - | - | ✅ | - |
| Graph traversal | - | - | ✅ | - |
| LLM Chunk Enrichment | - | - | - | ✅ |
| Q&A Generation | - | - | - | ✅ |
| RAG Evaluation | - | - | - | ✅ |
| 3-Stage Filtering | - | - | - | ✅ |

---

## 4. Goals & Success Criteria

### 4.1 Primary Goals

#### G1: Production-Ready RAG Infrastructure
- High reliability with comprehensive error handling
- Extensive test coverage (246+ tests)
- Clean architecture for maintainability
- Proper logging and diagnostics

#### G2: High Performance
| Metric | Target | Current |
|--------|--------|---------|
| Batch Indexing | <50ms/1K chunks | 24ms/1K chunks ✅ |
| Vector Search | <5ms/query | 0.6ms/query ✅ |
| Embedding Cache | 100% improvement | 100% ✅ |
| Semantic Cache | <5ms hit | <5ms ✅ |

#### G3: AI Provider Flexibility
- Zero lock-in to any AI provider
- Core functionality works without AI services
- Easy swapping of providers via DI
- Support for custom implementations

#### G4: Developer Experience
- Fluent builder API for configuration
- Comprehensive documentation
- Clear error messages
- Easy integration with existing projects

### 4.2 Design Principles

1. **Evidence > Assumptions**: All performance claims are benchmarked
2. **Code > Documentation**: Working implementations over specifications
3. **Efficiency > Verbosity**: Minimal API surface, maximum capability
4. **Local > Cloud**: Support edge deployment without cloud dependencies
5. **Composition > Inheritance**: Modular providers over monolithic solutions

### 4.3 Quality Standards

| Dimension | Standard |
|-----------|----------|
| **Functional** | 100% test pass rate, comprehensive edge case coverage |
| **Structural** | Clean Architecture compliance, SOLID principles |
| **Performance** | Sub-millisecond search, efficient memory usage |
| **Security** | Secure local storage, API key protection |

---

## 5. Architectural Decisions

### 5.1 Key Patterns

| Pattern | Purpose | Implementation |
|---------|---------|----------------|
| **Builder** | Fluent configuration | `FluxIndexContextBuilder` |
| **Repository** | Data access abstraction | `IDocumentRepository`, `IVectorStore` |
| **Factory** | Entity creation control | `Document.Create()` |
| **Strategy** | Search algorithm selection | `SearchStrategy` enum |
| **Adapter** | AI provider integration | `IEmbeddingService` implementations |

### 5.2 Extension Points

```csharp
// Custom embedding service
public class CustomEmbeddingService : IEmbeddingService { }

// Custom vector store
public class CustomVectorStore : IVectorStore { }

// Custom reranker
public class CustomReranker : IReranker { }

// Custom semantic cache
public class CustomSemanticCache : ISemanticCacheService { }
```

### 5.3 Trade-offs

| Decision | Trade-off | Rationale |
|----------|-----------|-----------|
| SQLite first | Limited to ~1M vectors | Edge AI focus, zero infrastructure |
| No HNSW in SQLite | Brute force search | sqlite-vec limitation, acceptable for edge |
| Local reranker fallback | Slightly lower quality | Resilience over perfection |
| AI provider agnostic | More integration code | Maximum flexibility |

---

## 6. Evolution Roadmap

### Current State (v0.x)
- ✅ Hybrid search (Vector + BM25 + RRF)
- ✅ Multiple storage backends (SQLite, PostgreSQL, Qdrant, Neo4j)
- ✅ AI provider flexibility (abstract base classes)
- ✅ Graph traversal (BFS, DFS, Dijkstra, PageRank)
- ✅ Vector quantization (Int8, Int4, Binary, Product)
- ✅ MCP server
- ✅ GraphRAG (Entity Graph + Community Detection)
- ✅ Three storage modes (Local, Full, Custom)

### In Progress
- [ ] Storage Provider abstraction refinement
- [ ] Enhanced metadata filtering API
- [ ] Query decomposition for complex queries

---

## 7. Summary

### FluxIndex Identity Statement

> **FluxIndex is a production-ready, AI-agnostic RAG infrastructure library for .NET that transforms document chunks into search-ready context through hybrid search, intelligent enrichment, and modular provider architecture.**

### Core Tenets

1. **Clean Architecture**: Strict layer separation, unidirectional dependencies
2. **AI Agnostic**: Core works without AI; providers are optional
3. **Infrastructure Focus**: Library scope, not application logic
4. **Local-First**: Edge-ready, privacy-respecting design
5. **Modular**: Composable providers for maximum flexibility

### Boundary Rules

- **FluxIndex owns**: Indexing, searching, reranking, caching, contextual enrichment
- **FileFlux/WebFlux own**: Parsing, chunking, structure extraction
- **FluxImprover owns**: LLM enrichment, Q&A generation, RAG evaluation, chunk filtering
- **Applications own**: Caching policies, parallelization, workflows

---

*Document Version: 1.0*
*Created: 2025-11-30*
*Based on: Codebase analysis, existing documentation, research documents*
