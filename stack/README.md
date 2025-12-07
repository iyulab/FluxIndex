# FluxIndex Stack

A complete, self-contained RAG (Retrieval-Augmented Generation) platform built on the FluxIndex library.

## Overview

**FluxIndex** is a library/SDK for building LLM AI solutions.
**FluxIndex Stack** is a complete, self-contained service that leverages ALL FluxIndex capabilities:

- **API Server**: RESTful API for document management and search
- **MCP Server**: Model Context Protocol endpoint for AI assistant integration
- **Document Pipeline**: Extract → Chunk → Enrich → Index → Memorize
- **Multi-Provider AI**: OpenAI, Anthropic, Google, Azure, Local models
- **Full Infrastructure**: PostgreSQL + pgvector, Neo4j, Qdrant, Redis

```
┌─────────────────────────────────────────────────────────────────┐
│                      FluxIndex Stack                             │
├──────────────────────┬──────────────────────────────────────────┤
│      API Server      │              MCP Server                   │
│   (REST Endpoints)   │     (AI Assistant Integration)           │
├──────────────────────┴──────────────────────────────────────────┤
│                    Application Layer                             │
│   Document Management │ Search │ Indexing │ Analytics            │
├─────────────────────────────────────────────────────────────────┤
│                    FluxIndex SDK/Core                            │
│   Indexer │ Retriever │ Embeddings │ Reranking │ Quantization   │
├─────────────────────────────────────────────────────────────────┤
│                    Infrastructure                                │
│   PostgreSQL+pgvector │ Qdrant │ Neo4j │ Redis │ FileFlux       │
└─────────────────────────────────────────────────────────────────┘
```

## Quick Start

### One-Command Start (Development)

```powershell
./start-dev.ps1
```

This starts all infrastructure (PostgreSQL, Redis, Neo4j, Qdrant) and the API server.

### Docker Compose (Production)

```bash
# Copy and configure environment
cp .env.example .env
# Edit .env with your API keys

# Start full stack
docker-compose up -d
```

## Architecture

```
stack/
├── src/
│   ├── FluxIndex.Stack.Api/           # Web API + MCP endpoints
│   ├── FluxIndex.Stack.Application/   # Business logic & services
│   ├── FluxIndex.Stack.Domain/        # Domain entities
│   ├── FluxIndex.Stack.Infrastructure/# Data access & external services
│   └── FluxIndex.Stack.Shared/        # DTOs & common types
├── frontend/                          # React admin UI
├── docker/                            # Docker configurations
├── docker-compose.yml                 # Production compose
├── docker-compose.dev.yml             # Development compose
└── FluxIndex.Stack.sln               # Solution file
```

## Core Features

### Document Pipeline

| Stage | Description | FluxIndex Component |
|-------|-------------|---------------------|
| **Extract** | Parse files (PDF, DOCX, MD, TXT) | FileFlux Integration |
| **Chunk** | Intelligent text splitting | IChunkingService |
| **Enrich** | AI metadata extraction | IMetadataExtractor |
| **Embed** | Generate vector embeddings | IEmbeddingService |
| **Index** | Store in vector database | IVectorStore |
| **Memorize** | Full document context | SDK.Indexer |

### Search Capabilities

| Mode | Description | Implementation |
|------|-------------|----------------|
| **Vector** | Semantic similarity search | SDK.Retriever.SearchAsync |
| **Keyword** | BM25/TF-IDF text search | IBM25Service |
| **Hybrid** | Combined vector + keyword | HybridSearchAsync + RRF |
| **Quantized** | Fast approximate search | SearchQuantizedAsync |

### AI Provider Support

| Provider | Embeddings | Completions | Reranking |
|----------|------------|-------------|-----------|
| OpenAI | ✅ | ✅ | ✅ |
| Azure OpenAI | ✅ | ✅ | ✅ |
| Anthropic | ✅ | ✅ | - |
| Google Gemini | ✅ | ✅ | - |
| Local (ONNX) | ✅ | - | ✅ |

## API Endpoints

### Documents
```
POST   /api/v1/documents/upload     # Upload and index document
GET    /api/v1/documents            # List documents (paginated)
GET    /api/v1/documents/{id}       # Get document with chunks
PUT    /api/v1/documents/{id}       # Update document metadata
DELETE /api/v1/documents/{id}       # Delete document
POST   /api/v1/documents/{id}/reindex  # Re-process document
```

### Chunks
```
GET    /api/v1/documents/{id}/chunks      # List chunks
GET    /api/v1/chunks/{chunkId}           # Get chunk details
PUT    /api/v1/chunks/{chunkId}           # Update chunk content/metadata
DELETE /api/v1/chunks/{chunkId}           # Delete chunk
POST   /api/v1/chunks/{chunkId}/enrich    # Re-enrich chunk metadata
```

### Search
```
POST   /api/v1/search              # Hybrid search
POST   /api/v1/search/vector       # Vector-only search
POST   /api/v1/search/keyword      # Keyword-only search
```

### MCP (Model Context Protocol)
```
POST   /mcp/tools/memorize         # Index content into knowledge base
POST   /mcp/tools/search           # Search knowledge base
POST   /mcp/tools/unmemorize       # Remove content from knowledge base
GET    /mcp/tools/status           # Get system status
```

### Collections & Analytics
```
GET/POST/PUT/DELETE /api/v1/collections
GET    /api/v1/analytics/system
GET    /api/v1/analytics/search
GET    /api/v1/analytics/documents
```

## Infrastructure

### Docker Compose Services

| Service | Image | Port | Purpose |
|---------|-------|------|---------|
| **postgres** | pgvector/pgvector:pg17 | 5432 | Primary DB + Vector Store |
| **qdrant** | qdrant/qdrant:v1.12.4 | 6333, 6334 | High-perf Vector DB |
| **neo4j** | neo4j:5.26-community | 7474, 7687 | Knowledge Graph |
| **redis** | redis:7.4-alpine | 6379 | Cache + Rate Limiting |
| **api** | fluxindex-stack-api | 5000 | API Server |
| **frontend** | fluxindex-stack-frontend | 3000 | Admin UI |

### Resource Requirements

| Environment | RAM | CPU | Disk |
|-------------|-----|-----|------|
| Development | 8GB+ | 4 cores | 20GB |
| Production | 16GB+ | 8 cores | 100GB+ |

## Configuration

### Environment Variables

```bash
# AI Providers
OPENAI_API_KEY=sk-...
AZURE_OPENAI_ENDPOINT=https://...
AZURE_OPENAI_KEY=...
ANTHROPIC_API_KEY=...
GOOGLE_API_KEY=...

# Database
POSTGRES_USER=fluxindex
POSTGRES_PASSWORD=fluxindex
POSTGRES_DB=fluxindex

# Neo4j
NEO4J_PASSWORD=fluxindex123

# Features
ENABLE_MCP=true
ENABLE_QDRANT=true
ENABLE_NEO4J=true
```

### appsettings.json

```json
{
  "FluxIndex": {
    "Embedding": {
      "Provider": "OpenAI",
      "Model": "text-embedding-3-small",
      "Dimension": 1536
    },
    "VectorStore": {
      "Provider": "PostgreSQL",
      "UseQuantization": true,
      "QuantizationType": "Scalar"
    },
    "Chunking": {
      "Strategy": "Intelligent",
      "MaxChunkSize": 1024,
      "Overlap": 128
    }
  }
}
```

## Development

### Prerequisites

- .NET 10.0 SDK
- Node.js 22+
- Docker & Docker Compose
- (Optional) OpenAI API key

### Build & Run

```bash
# Restore and build
dotnet build FluxIndex.Stack.sln

# Run API server
cd src/FluxIndex.Stack.Api
dotnet run

# Run frontend
cd frontend
npm install && npm run dev
```

### Testing

```bash
# Backend tests
dotnet test

# Frontend tests
cd frontend && npm test
```

## Roadmap

- [x] Basic API server
- [x] PostgreSQL + pgvector integration
- [x] Docker Compose infrastructure
- [ ] Full FluxIndex SDK integration
- [ ] MCP endpoint implementation
- [ ] Multi-AI provider configuration
- [ ] Chunk-level CRUD API
- [ ] Document re-indexing pipeline
- [ ] Admin UI enhancements

## License

MIT
