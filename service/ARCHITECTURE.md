# FluxIndex-Service Architecture Design

## Vision

Transform FluxIndex from a RAG middleware library into a self-contained, enterprise-grade service that provides complete document intelligence and retrieval capabilities out of the box.

## Positioning

```
┌─────────────────────────────────────────────────────────────────┐
│                      FluxIndex-Service                          │
│                                                                 │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────────┐  │
│  │   Frontend   │  │   Backend    │  │    Infrastructure    │  │
│  │   (React)    │◄─►│   (C#/API)   │◄─►│  PG + Neo4j + Redis  │  │
│  └──────────────┘  └──────────────┘  └──────────────────────┘  │
│                                                                 │
│  Single Docker Image • Self-Contained • Production-Ready        │
└─────────────────────────────────────────────────────────────────┘
```

## Core Features

### 1. Document Management
- Multi-format document upload (PDF, DOCX, HTML, MD, TXT)
- Intelligent chunking with configurable strategies
- Automatic metadata extraction
- Version history and document lineage
- Bulk import/export

### 2. Knowledge Graph
- Document relationship visualization
- Entity extraction and linking
- Concept clustering
- Graph-based navigation
- Impact analysis

### 3. Hybrid Search
- Vector similarity search (pgvector)
- BM25 full-text search
- Graph-augmented retrieval
- Intelligent reranking
- Faceted search & filtering

### 4. RAG Pipeline
- QA pair generation
- Chunk enrichment
- Quality evaluation
- Context window optimization
- MCP-compatible API

### 5. Analytics & Monitoring
- Search analytics dashboard
- Query performance metrics
- Index health monitoring
- Usage statistics

---

## Backend Architecture (C# ASP.NET Core)

### Project Structure

```
service/
├── src/
│   ├── FluxIndex.Service.Api/           # ASP.NET Core Web API
│   │   ├── Controllers/
│   │   │   ├── DocumentsController.cs
│   │   │   ├── SearchController.cs
│   │   │   ├── GraphController.cs
│   │   │   ├── AnalyticsController.cs
│   │   │   └── AdminController.cs
│   │   ├── Hubs/
│   │   │   ├── IndexingHub.cs           # Real-time indexing progress
│   │   │   └── NotificationHub.cs       # System notifications
│   │   ├── Middleware/
│   │   │   ├── ApiKeyAuthMiddleware.cs
│   │   │   ├── RateLimitingMiddleware.cs
│   │   │   └── RequestLoggingMiddleware.cs
│   │   ├── Filters/
│   │   │   ├── ValidationFilter.cs
│   │   │   └── ExceptionFilter.cs
│   │   └── Program.cs
│   │
│   ├── FluxIndex.Service.Application/   # Business Logic
│   │   ├── Commands/                    # CQRS Commands
│   │   │   ├── Documents/
│   │   │   ├── Search/
│   │   │   └── Graph/
│   │   ├── Queries/                     # CQRS Queries
│   │   ├── Services/
│   │   │   ├── DocumentService.cs
│   │   │   ├── SearchOrchestrator.cs
│   │   │   ├── GraphService.cs
│   │   │   └── AnalyticsService.cs
│   │   ├── Validators/
│   │   └── Mappings/
│   │
│   ├── FluxIndex.Service.Domain/        # Domain Models
│   │   ├── Entities/
│   │   │   ├── Document.cs
│   │   │   ├── Collection.cs
│   │   │   ├── ApiKey.cs
│   │   │   └── User.cs
│   │   ├── Events/
│   │   └── ValueObjects/
│   │
│   ├── FluxIndex.Service.Infrastructure/# External Services
│   │   ├── Persistence/
│   │   │   ├── PostgreSQL/
│   │   │   ├── Neo4j/
│   │   │   └── Redis/
│   │   ├── BackgroundJobs/
│   │   │   ├── IndexingJob.cs
│   │   │   ├── EnrichmentJob.cs
│   │   │   └── CleanupJob.cs
│   │   └── External/
│   │       └── EmbeddingProviders/
│   │
│   └── FluxIndex.Service.Shared/        # Shared DTOs/Contracts
│       ├── DTOs/
│       ├── Contracts/
│       └── Constants/
│
├── tests/
│   ├── FluxIndex.Service.Api.Tests/
│   ├── FluxIndex.Service.Application.Tests/
│   └── FluxIndex.Service.Integration.Tests/
│
└── docker/
    ├── Dockerfile
    ├── docker-compose.yml
    └── docker-compose.prod.yml
```

### API Design (RESTful + OpenAPI)

```yaml
# API Versioning: /api/v1/

# Collections
POST   /api/v1/collections                    # Create collection
GET    /api/v1/collections                    # List collections
GET    /api/v1/collections/{id}               # Get collection
PUT    /api/v1/collections/{id}               # Update collection
DELETE /api/v1/collections/{id}               # Delete collection

# Documents
POST   /api/v1/documents                      # Upload document
POST   /api/v1/documents/bulk                 # Bulk upload
GET    /api/v1/documents                      # List documents
GET    /api/v1/documents/{id}                 # Get document
GET    /api/v1/documents/{id}/chunks          # Get document chunks
PUT    /api/v1/documents/{id}                 # Update document
DELETE /api/v1/documents/{id}                 # Delete document
POST   /api/v1/documents/{id}/reindex         # Reindex document
POST   /api/v1/documents/{id}/enrich          # Enrich chunks
POST   /api/v1/documents/{id}/generate-qa     # Generate QA pairs

# Search
POST   /api/v1/search                         # Hybrid search
POST   /api/v1/search/vector                  # Vector-only search
POST   /api/v1/search/keyword                 # Keyword-only search
POST   /api/v1/search/graph                   # Graph-augmented search
POST   /api/v1/search/mcp                     # MCP-compatible search

# Graph
GET    /api/v1/graph/nodes                    # Get graph nodes
GET    /api/v1/graph/edges                    # Get graph edges
GET    /api/v1/graph/neighbors/{nodeId}       # Get neighbors
GET    /api/v1/graph/path                     # Find path
POST   /api/v1/graph/cluster                  # Cluster analysis

# Analytics
GET    /api/v1/analytics/overview             # System overview
GET    /api/v1/analytics/search               # Search analytics
GET    /api/v1/analytics/documents            # Document analytics
GET    /api/v1/analytics/performance          # Performance metrics

# Admin
GET    /api/v1/admin/health                   # Health check
GET    /api/v1/admin/status                   # System status
POST   /api/v1/admin/api-keys                 # Create API key
GET    /api/v1/admin/settings                 # Get settings
PUT    /api/v1/admin/settings                 # Update settings

# WebSocket Hubs
/hubs/indexing                                # Indexing progress
/hubs/notifications                           # System notifications
```

### Authentication & Authorization

```csharp
// API Key Authentication (Primary)
[ApiKey]
public class DocumentsController : ControllerBase { }

// Role-based access
public enum ApiKeyRole
{
    Reader,      // Search, read documents
    Writer,      // Upload, modify documents
    Admin        // Full access including settings
}
```

### Background Jobs (Hangfire/Quartz)

```csharp
public interface IIndexingJob
{
    Task IndexDocumentAsync(Guid documentId, CancellationToken ct);
    Task BulkIndexAsync(IEnumerable<Guid> documentIds, CancellationToken ct);
    Task ReindexCollectionAsync(Guid collectionId, CancellationToken ct);
}

public interface IMaintenanceJob
{
    Task OptimizeVectorIndexAsync(CancellationToken ct);
    Task CleanupOrphanedChunksAsync(CancellationToken ct);
    Task UpdateGraphRelationshipsAsync(CancellationToken ct);
}
```

---

## Frontend Architecture (React + TypeScript)

### Tech Stack

```
React 18+          - UI Framework
TypeScript 5+      - Type Safety
Vite               - Build Tool
TanStack Query     - Server State
Zustand            - Client State
React Router       - Routing
shadcn/ui          - Component Library
Tailwind CSS       - Styling
Recharts           - Charts
React Flow         - Graph Visualization
```

### Project Structure

```
service/ui/
├── src/
│   ├── app/
│   │   ├── layout.tsx
│   │   └── providers.tsx
│   │
│   ├── components/
│   │   ├── ui/                      # shadcn/ui components
│   │   │   ├── button.tsx
│   │   │   ├── input.tsx
│   │   │   ├── dialog.tsx
│   │   │   └── ...
│   │   ├── common/
│   │   │   ├── Header.tsx
│   │   │   ├── Sidebar.tsx
│   │   │   ├── Loading.tsx
│   │   │   └── ErrorBoundary.tsx
│   │   ├── documents/
│   │   │   ├── DocumentList.tsx
│   │   │   ├── DocumentCard.tsx
│   │   │   ├── DocumentViewer.tsx
│   │   │   ├── ChunkList.tsx
│   │   │   └── UploadDialog.tsx
│   │   ├── search/
│   │   │   ├── SearchBar.tsx
│   │   │   ├── SearchResults.tsx
│   │   │   ├── FilterPanel.tsx
│   │   │   └── ResultCard.tsx
│   │   ├── graph/
│   │   │   ├── GraphViewer.tsx
│   │   │   ├── NodeDetail.tsx
│   │   │   └── GraphControls.tsx
│   │   └── analytics/
│   │       ├── OverviewCards.tsx
│   │       ├── SearchChart.tsx
│   │       └── PerformanceChart.tsx
│   │
│   ├── pages/
│   │   ├── Dashboard.tsx
│   │   ├── Documents.tsx
│   │   ├── DocumentDetail.tsx
│   │   ├── Search.tsx
│   │   ├── Graph.tsx
│   │   ├── Analytics.tsx
│   │   └── Settings.tsx
│   │
│   ├── api/
│   │   ├── client.ts               # Axios instance
│   │   ├── documents.ts            # Document API
│   │   ├── search.ts               # Search API
│   │   ├── graph.ts                # Graph API
│   │   └── analytics.ts            # Analytics API
│   │
│   ├── hooks/
│   │   ├── useDocuments.ts
│   │   ├── useSearch.ts
│   │   ├── useGraph.ts
│   │   ├── useAnalytics.ts
│   │   └── useWebSocket.ts
│   │
│   ├── stores/
│   │   ├── authStore.ts
│   │   ├── uiStore.ts
│   │   └── searchStore.ts
│   │
│   ├── types/
│   │   ├── document.ts
│   │   ├── search.ts
│   │   ├── graph.ts
│   │   └── api.ts
│   │
│   ├── lib/
│   │   ├── utils.ts
│   │   └── constants.ts
│   │
│   └── styles/
│       └── globals.css
│
├── public/
├── package.json
├── tailwind.config.js
├── tsconfig.json
└── vite.config.ts
```

### Key Features

#### 1. Document Management UI
```tsx
// DocumentList with virtual scrolling
<DocumentList
  documents={documents}
  onSelect={handleSelect}
  onDelete={handleDelete}
  enableBulkActions
  enableDragDrop
/>

// Document viewer with chunk highlighting
<DocumentViewer
  document={document}
  highlightChunks={searchResults}
  showMetadata
  showQAPairs
/>
```

#### 2. Search Interface
```tsx
// Advanced search with filters
<SearchPage>
  <SearchBar
    onSearch={handleSearch}
    suggestions={recentQueries}
  />
  <FilterPanel
    collections={collections}
    dateRange={dateRange}
    contentTypes={contentTypes}
  />
  <SearchResults
    results={results}
    loading={loading}
    onLoadMore={loadMore}
  />
</SearchPage>
```

#### 3. Graph Visualization (React Flow)
```tsx
// Interactive knowledge graph
<GraphViewer
  nodes={graphNodes}
  edges={graphEdges}
  onNodeClick={handleNodeClick}
  onEdgeClick={handleEdgeClick}
  layout="force-directed"
  minimap
  controls
/>
```

#### 4. Real-time Updates (SignalR)
```tsx
// WebSocket hook for live updates
const { progress, status } = useIndexingProgress(documentId);

// Progress indicator
<IndexingProgress
  documentId={documentId}
  progress={progress}
  status={status}
/>
```

---

## Infrastructure Architecture

### Database Schema (PostgreSQL)

```sql
-- Collections
CREATE TABLE collections (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(255) NOT NULL,
    description TEXT,
    settings JSONB DEFAULT '{}',
    created_at TIMESTAMP DEFAULT NOW(),
    updated_at TIMESTAMP DEFAULT NOW()
);

-- Documents
CREATE TABLE documents (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    collection_id UUID REFERENCES collections(id),
    title VARCHAR(500) NOT NULL,
    source_type VARCHAR(50),
    source_path TEXT,
    content_hash VARCHAR(64),
    metadata JSONB DEFAULT '{}',
    status VARCHAR(20) DEFAULT 'pending',
    created_at TIMESTAMP DEFAULT NOW(),
    updated_at TIMESTAMP DEFAULT NOW()
);

-- Chunks (with pgvector)
CREATE TABLE chunks (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id UUID REFERENCES documents(id) ON DELETE CASCADE,
    chunk_index INT NOT NULL,
    content TEXT NOT NULL,
    embedding vector(1536),           -- or 384 for local models
    metadata JSONB DEFAULT '{}',
    qa_pairs JSONB DEFAULT '[]',
    quality_scores JSONB DEFAULT '{}',
    token_count INT,
    created_at TIMESTAMP DEFAULT NOW()
);

-- Create HNSW index for vector search
CREATE INDEX chunks_embedding_idx ON chunks
USING hnsw (embedding vector_cosine_ops)
WITH (m = 16, ef_construction = 64);

-- Full-text search index
CREATE INDEX chunks_content_fts_idx ON chunks
USING gin (to_tsvector('english', content));

-- API Keys
CREATE TABLE api_keys (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(100) NOT NULL,
    key_hash VARCHAR(64) NOT NULL,
    role VARCHAR(20) DEFAULT 'reader',
    permissions JSONB DEFAULT '{}',
    last_used_at TIMESTAMP,
    expires_at TIMESTAMP,
    created_at TIMESTAMP DEFAULT NOW()
);

-- Search History (for analytics)
CREATE TABLE search_history (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    query TEXT NOT NULL,
    result_count INT,
    latency_ms INT,
    api_key_id UUID REFERENCES api_keys(id),
    metadata JSONB DEFAULT '{}',
    created_at TIMESTAMP DEFAULT NOW()
);
```

### Neo4j Schema (Graph)

```cypher
// Node types
(:Document {id, title, created_at})
(:Chunk {id, document_id, chunk_index, content_preview})
(:Entity {name, type, confidence})
(:Topic {name, embedding})
(:Collection {id, name})

// Relationships
(:Document)-[:BELONGS_TO]->(:Collection)
(:Document)-[:HAS_CHUNK]->(:Chunk)
(:Chunk)-[:MENTIONS]->(:Entity)
(:Chunk)-[:ABOUT]->(:Topic)
(:Chunk)-[:REFERENCES]->(:Chunk)
(:Document)-[:SIMILAR_TO {score}]->(:Document)
(:Entity)-[:RELATED_TO {type}]->(:Entity)
```

### Redis Structure

```
# Sessions/Auth
session:{api_key_id}           -> {permissions, rate_limit_remaining}

# Caching
cache:search:{query_hash}      -> {results, timestamp}
cache:embedding:{content_hash} -> {vector, model}
cache:document:{id}            -> {metadata, chunk_count}

# Rate Limiting
rate:{api_key_id}:{window}     -> request_count

# Real-time
pubsub:indexing:{document_id}  -> progress updates
pubsub:notifications           -> system alerts

# Queues
queue:indexing                 -> pending documents
queue:enrichment               -> chunks to enrich
```

---

## Docker Deployment

### Multi-stage Dockerfile

```dockerfile
# ============================================
# Stage 1: Build .NET Backend
# ============================================
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS backend-build
WORKDIR /src

# Copy and restore
COPY service/src/ ./
RUN dotnet restore FluxIndex.Service.Api/FluxIndex.Service.Api.csproj
RUN dotnet publish FluxIndex.Service.Api/FluxIndex.Service.Api.csproj \
    -c Release -o /app/publish --no-restore

# ============================================
# Stage 2: Build React Frontend
# ============================================
FROM node:22-alpine AS frontend-build
WORKDIR /app

COPY service/ui/package*.json ./
RUN npm ci

COPY service/ui/ ./
RUN npm run build

# ============================================
# Stage 3: Production Image
# ============================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS production

# Install supervisord for process management
RUN apt-get update && apt-get install -y supervisor && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Copy backend
COPY --from=backend-build /app/publish ./

# Copy frontend to wwwroot
COPY --from=frontend-build /app/dist ./wwwroot

# Copy supervisor config
COPY docker/supervisord.conf /etc/supervisor/conf.d/supervisord.conf

# Expose ports
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s \
    CMD curl -f http://localhost:8080/api/v1/admin/health || exit 1

# Entry point
ENTRYPOINT ["/usr/bin/supervisord", "-c", "/etc/supervisor/conf.d/supervisord.conf"]
```

### Docker Compose (All-in-One Development)

```yaml
version: '3.8'

services:
  fluxindex:
    build:
      context: ..
      dockerfile: docker/Dockerfile
    ports:
      - "8080:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ConnectionStrings__PostgreSQL=Host=postgres;Database=fluxindex;Username=fluxindex;Password=fluxindex123
      - ConnectionStrings__Neo4j=bolt://neo4j:7687
      - ConnectionStrings__Redis=redis:6379
      - Embedding__Provider=local
      - Embedding__Model=all-MiniLM-L6-v2
    depends_on:
      postgres:
        condition: service_healthy
      neo4j:
        condition: service_healthy
      redis:
        condition: service_healthy

  postgres:
    image: pgvector/pgvector:pg16
    environment:
      POSTGRES_USER: fluxindex
      POSTGRES_PASSWORD: fluxindex123
      POSTGRES_DB: fluxindex
    volumes:
      - postgres_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U fluxindex"]
      interval: 5s
      timeout: 5s
      retries: 5

  neo4j:
    image: neo4j:5-community
    environment:
      NEO4J_AUTH: neo4j/fluxindex123
    volumes:
      - neo4j_data:/data
    healthcheck:
      test: ["CMD-SHELL", "wget -qO- http://localhost:7474 || exit 1"]
      interval: 10s
      timeout: 10s
      retries: 10

  redis:
    image: redis:7-alpine
    volumes:
      - redis_data:/data
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 5s
      timeout: 5s
      retries: 5

volumes:
  postgres_data:
  neo4j_data:
  redis_data:
```

---

## Configuration

### appsettings.json

```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=localhost;Database=fluxindex;Username=fluxindex;Password=fluxindex123",
    "Neo4j": "bolt://localhost:7687",
    "Redis": "localhost:6379"
  },
  "Embedding": {
    "Provider": "local",
    "Model": "all-MiniLM-L6-v2",
    "Dimensions": 384,
    "OpenAI": {
      "ApiKey": "",
      "Model": "text-embedding-3-small"
    }
  },
  "Search": {
    "DefaultTopK": 10,
    "MaxTopK": 100,
    "EnableReranking": true,
    "VectorWeight": 0.7,
    "KeywordWeight": 0.3
  },
  "Storage": {
    "MaxFileSize": 104857600,
    "AllowedExtensions": [".pdf", ".docx", ".html", ".md", ".txt"],
    "ChunkSize": 1000,
    "ChunkOverlap": 200
  },
  "RateLimiting": {
    "RequestsPerMinute": 60,
    "RequestsPerDay": 10000
  },
  "Features": {
    "EnableGraphFeatures": true,
    "EnableAnalytics": true,
    "EnableQAGeneration": true
  }
}
```

---

## Migration Path

### Phase 1: Foundation (Week 1-2)
- [ ] Create service directory structure
- [ ] Set up backend project with Clean Architecture
- [ ] Configure PostgreSQL with pgvector
- [ ] Implement basic CRUD for documents
- [ ] Set up frontend with Vite + React + shadcn/ui

### Phase 2: Core Features (Week 3-4)
- [ ] Implement hybrid search API
- [ ] Build document upload and processing
- [ ] Create search UI with results display
- [ ] Add real-time indexing progress (SignalR)
- [ ] Implement basic authentication (API keys)

### Phase 3: Advanced Features (Week 5-6)
- [ ] Integrate Neo4j for graph features
- [ ] Build graph visualization UI
- [ ] Add analytics dashboard
- [ ] Implement background job processing
- [ ] Add bulk operations support

### Phase 4: Polish & Deploy (Week 7-8)
- [ ] Optimize performance
- [ ] Add comprehensive error handling
- [ ] Write API documentation (OpenAPI)
- [ ] Create Docker production image
- [ ] Set up CI/CD pipeline

---

## Success Metrics

| Metric | Target |
|--------|--------|
| Search Latency (P95) | < 200ms |
| Document Processing | < 5s per 100KB |
| Concurrent Users | 100+ |
| Uptime | 99.9% |
| API Response Time | < 100ms |

---

## Open Questions

1. **Authentication**: API keys only, or also support OAuth/OIDC?
2. **Multi-tenancy**: Single tenant or multi-tenant architecture?
3. **Embedding**: Support multiple embedding providers simultaneously?
4. **Backup**: Built-in backup/restore functionality?
5. **Plugins**: Plugin system for custom processors?

---

*Document Version: 1.0*
*Last Updated: 2025-12-05*
