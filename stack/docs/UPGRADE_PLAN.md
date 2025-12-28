# FluxIndex Stack Upgrade Plan
## Production-Grade Polyglot Persistence RAG Middleware

**Version**: 2.0 Target
**Date**: 2025-12-28
**Based on**: Research of LangChain, LlamaIndex, Haystack, Anthropic MCP, RAGAS

---

## Executive Summary

FluxIndex Stack을 **완전 자기완결형 RAG 미들웨어**로 업그레이드:
- **Polyglot Persistence**: PostgreSQL/pgvector + Qdrant + Neo4j + Redis
- **MCP Server**: Anthropic 표준 준수 AI 어시스턴트 통합
- **Complete UI**: 관리, 검색, 그래프 시각화, 평가 대시보드
- **Docker-Compose**: 단일 명령 배포

---

## Current State Analysis

### Implemented (✅)
| Component | Status | Notes |
|-----------|--------|-------|
| PostgreSQL/pgvector | ✅ Complete | Primary DB + Vector Store |
| Qdrant | ✅ Complete | High-perf Vector DB |
| Neo4j | ⚠️ Interface Only | Service not implemented |
| Redis | ✅ Complete | Caching + Pub/Sub |
| MCP Server | ⚠️ Basic | Tools only, no SSE/stdio |
| API Controllers | ✅ 12 endpoints | Documents, Search, Eval, etc. |
| Frontend | ✅ 8 pages | Dashboard, Documents, Search, Settings |
| Evaluation | ✅ Complete | RAGAS metrics, Quality Gate |

### Missing Components
| Component | Priority | Complexity |
|-----------|----------|------------|
| Neo4j Service Implementation | 🔴 High | Medium |
| MCP SSE/stdio Transport | 🔴 High | Medium |
| Graph Visualization UI | 🔴 High | Medium |
| OpenTelemetry Tracing | 🟡 Medium | Low |
| Agentic RAG (Multi-hop) | 🟡 Medium | High |
| WebFlux Connector | 🟡 Medium | Medium |
| Multi-tenancy | 🟢 Low | High |

---

## Phase 1: Core Infrastructure 완성
**Duration**: 1 week | **Priority**: 🔴 Critical

### 1.1 MCP Server 표준화 (Anthropic Spec 준수)

현재 MCP는 REST 기반 Tools만 제공. Anthropic 표준 준수 필요:

```
/mcp                    # Server info (existing)
/mcp/sse                # SSE transport (NEW)
/mcp/resources          # Resource listing (NEW)
/mcp/prompts            # Prompt templates (NEW)
/mcp/tools              # Tool definitions (existing, enhance)
```

**Tasks**:
- [ ] SSE (Server-Sent Events) transport 구현
- [ ] stdio transport wrapper (CLI 통합용)
- [ ] Resources API 추가 (document listing)
- [ ] Prompts API 추가 (RAG prompt templates)
- [ ] MCP protocol 버전 명시 (spec v1.0)

**Reference**: https://spec.modelcontextprotocol.io/specification/

### 1.2 Observability Layer

```yaml
Components:
  - OpenTelemetry SDK integration
  - Distributed tracing (Jaeger/Zipkin)
  - RAG-specific metrics:
    - search_latency_ms
    - retrieval_recall@k
    - embedding_generation_time
    - chunk_quality_score
  - Prometheus metrics endpoint
```

**Tasks**:
- [ ] OpenTelemetry.Instrumentation.AspNetCore 추가
- [ ] Custom RAG metrics collector
- [ ] /metrics endpoint (Prometheus format)
- [ ] Trace propagation across services
- [ ] Health check 강화 (/health/ready, /health/live)

### 1.3 Docker Compose 개선

```yaml
Additions:
  - Jaeger (tracing UI)
  - Prometheus (metrics)
  - Grafana (dashboards)
  - Traefik (reverse proxy, SSL)
```

**Files to create/update**:
- `docker-compose.yml` - 관측성 스택 추가
- `docker/grafana/` - RAG 대시보드 프리셋
- `docker/prometheus/` - 스크래핑 설정

---

## Phase 2: GraphRAG 완전 구현
**Duration**: 1.5 weeks | **Priority**: 🔴 Critical

### 2.1 Neo4j Service Implementation

`INeo4jGraphService` 인터페이스 완전 구현:

```csharp
public class Neo4jGraphService : INeo4jGraphService
{
    // Entity storage with properties
    // Relationship management
    // Community detection (Louvain algorithm)
    // Path finding (Dijkstra, A*)
    // Query expansion via graph traversal
}
```

**Tasks**:
- [ ] Neo4jGraphService.cs 구현
- [ ] Cypher query builder 유틸리티
- [ ] Entity extraction → Graph storage 파이프라인
- [ ] Community detection 스케줄러
- [ ] Graph statistics API

### 2.2 GraphRAG Search Integration

```
Search Flow:
1. Query → Entity extraction
2. Entities → Graph traversal (2-hop)
3. Related entities → Query expansion
4. Expanded query → Vector search
5. Results → Graph-based reranking
```

**Tasks**:
- [ ] GraphAugmentedSearchService 구현
- [ ] Entity-chunk linking 자동화
- [ ] Graph-based reranking algorithm
- [ ] Configurable traversal depth

### 2.3 Graph Controller & API

```yaml
Endpoints:
  GET  /api/v1/graph/nodes           # List nodes
  GET  /api/v1/graph/nodes/{id}      # Node detail
  GET  /api/v1/graph/edges           # List edges
  GET  /api/v1/graph/neighbors/{id}  # Neighbor nodes
  POST /api/v1/graph/paths           # Find paths
  POST /api/v1/graph/communities     # Run community detection
  GET  /api/v1/graph/statistics      # Graph stats
  GET  /api/v1/graph/export          # Export (JSON-LD/RDF)
```

### 2.4 Graph Visualization UI

React Flow 기반 인터랙티브 그래프:

```tsx
// GraphPage.tsx
<ReactFlow
  nodes={graphNodes}
  edges={graphEdges}
  onNodeClick={handleNodeDetail}
  fitView
>
  <MiniMap />
  <Controls />
  <Background />
</ReactFlow>
```

**Tasks**:
- [ ] GraphPage.tsx 생성
- [ ] useGraph.ts hook
- [ ] Node/Edge 스타일링 (entity type별)
- [ ] Path highlighting
- [ ] Community clustering view
- [ ] Export to PNG/SVG

---

## Phase 3: Agentic RAG
**Duration**: 2 weeks | **Priority**: 🟡 Medium

### 3.1 Query Decomposition

복잡한 쿼리를 하위 쿼리로 분해:

```
Input: "Compare the security features of AWS and Azure for healthcare"

Decomposed:
1. "AWS security features for healthcare"
2. "Azure security features for healthcare"
3. "Healthcare compliance requirements cloud"

Synthesis: Combine results with comparison logic
```

**Tasks**:
- [ ] QueryDecompositionService 구현
- [ ] LLM-based decomposition strategy
- [ ] Rule-based fallback
- [ ] Sub-query parallel execution
- [ ] Result synthesis

### 3.2 Multi-Hop Reasoning

```
Hop 1: "Who founded OpenAI?" → "Sam Altman, Elon Musk, ..."
Hop 2: "What other companies did Sam Altman lead?" → "Y Combinator"
Hop 3: "What startups came from Y Combinator?" → "Airbnb, Stripe, ..."
```

**Tasks**:
- [ ] MultiHopRetrievalService 구현
- [ ] Hop context accumulation
- [ ] Early termination (confidence threshold)
- [ ] Cycle detection
- [ ] Max hop limiting

### 3.3 Iterative Retrieval

```
Loop:
1. Initial retrieval
2. Evaluate result quality
3. If insufficient → Reformulate query
4. Re-retrieve with expanded context
5. Repeat until quality threshold or max iterations
```

**Tasks**:
- [ ] IterativeRetrievalService
- [ ] Quality evaluation (self-RAGAS)
- [ ] Query reformulation strategies
- [ ] Convergence detection

### 3.4 Agent API Endpoints

```yaml
POST /api/v1/agents/query-decompose
POST /api/v1/agents/multi-hop
POST /api/v1/agents/iterative-search
POST /api/v1/agents/rag-complete    # Full agentic RAG
```

---

## Phase 4: Connectors & Sources
**Duration**: 1.5 weeks | **Priority**: 🟡 Medium

### 4.1 WebFlux Integration (Web Crawling)

```csharp
// WebCrawlConnector
public interface IWebCrawlConnector
{
    Task<CrawlResult> CrawlUrlAsync(string url, CrawlOptions options);
    Task<IEnumerable<CrawlResult>> CrawlSitemapAsync(string sitemapUrl);
    Task ScheduleCrawlAsync(CrawlSchedule schedule);
}
```

**Tasks**:
- [ ] WebFlux SDK 통합
- [ ] Crawl job scheduling (Hangfire)
- [ ] robots.txt 준수
- [ ] Rate limiting per domain
- [ ] Content deduplication

### 4.2 Cloud Storage Connectors

```yaml
Connectors:
  - AWS S3
  - Azure Blob Storage
  - Google Cloud Storage
  - MinIO (self-hosted S3)
```

**Tasks**:
- [ ] ICloudStorageConnector 인터페이스
- [ ] S3Connector 구현
- [ ] AzureBlobConnector 구현
- [ ] Sync scheduler (polling/webhook)
- [ ] Incremental sync (change detection)

### 4.3 File System Watcher

```csharp
// Local directory monitoring
public interface IFileWatcherService
{
    Task WatchDirectoryAsync(string path, WatchOptions options);
    event EventHandler<FileChangeEvent> OnFileChanged;
}
```

**Tasks**:
- [ ] FileSystemWatcher integration
- [ ] Change batching (debounce)
- [ ] Filter patterns (include/exclude)
- [ ] Auto-index on change

### 4.4 Connector Management UI

```
/connectors               # List all connectors
/connectors/new           # Add connector wizard
/connectors/{id}/status   # Connector health
/connectors/{id}/logs     # Sync logs
```

---

## Phase 5: Enterprise Features
**Duration**: 2 weeks | **Priority**: 🟢 Future

### 5.1 Multi-tenancy

```sql
-- Tenant isolation strategy: Row-Level Security
ALTER TABLE documents ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON documents
    USING (tenant_id = current_setting('app.tenant_id')::uuid);
```

**Tasks**:
- [ ] Tenant entity & management
- [ ] RLS policies (PostgreSQL)
- [ ] Qdrant collection-per-tenant
- [ ] Neo4j database-per-tenant (or labels)
- [ ] Redis key prefix isolation

### 5.2 Enhanced RBAC

```yaml
Roles:
  - SuperAdmin: Full system access
  - TenantAdmin: Tenant management
  - Editor: Document CRUD
  - Viewer: Read-only search
  - API: Programmatic access only

Permissions:
  - documents:read, documents:write, documents:delete
  - search:execute, search:advanced
  - graph:read, graph:write
  - settings:read, settings:write
  - admin:users, admin:tenants
```

**Tasks**:
- [ ] Permission entity
- [ ] Role-permission mapping
- [ ] Policy-based authorization
- [ ] UI permission checks
- [ ] Audit logging

### 5.3 Document-Level ACL

```csharp
public class DocumentAcl
{
    public Guid DocumentId { get; set; }
    public List<AclEntry> Entries { get; set; }
}

public record AclEntry(
    string PrincipalId,     // User or Role ID
    PrincipalType Type,     // User, Role, Group
    Permission Permission); // Read, Write, Delete, Share
```

### 5.4 Audit Logging

```sql
CREATE TABLE audit_logs (
    id UUID PRIMARY KEY,
    timestamp TIMESTAMPTZ NOT NULL,
    actor_id UUID,
    actor_type VARCHAR(50),
    action VARCHAR(100) NOT NULL,
    resource_type VARCHAR(100),
    resource_id UUID,
    details JSONB,
    ip_address INET,
    user_agent TEXT
);
```

---

## Phase 6: UI/UX 완성
**Duration**: 1.5 weeks | **Priority**: 🟡 Medium

### 6.1 RAG Playground

인터랙티브 RAG 테스트 환경:

```
Features:
- Query input with parameter tuning
- Real-time retrieval visualization
- Chunk preview with highlighting
- A/B testing (different configs)
- Response streaming
- Prompt template editor
```

### 6.2 Observability Dashboard

```
Panels:
- Search latency P50/P95/P99
- Retrieval accuracy over time
- Index health status
- Query volume heatmap
- Error rate by endpoint
- Cache hit ratio
- Embedding generation time
```

### 6.3 Graph Explorer

```
Features:
- Full-screen graph view
- Node search & filter
- Community coloring
- Path highlighting
- Node detail sidebar
- Relationship filtering
- Export options (PNG, JSON, CSV)
```

### 6.4 Connector Dashboard

```
Features:
- Connector status cards
- Sync history timeline
- Error log viewer
- Manual sync trigger
- Schedule editor
```

### 6.5 Mobile-Responsive Design

- Tailwind breakpoints 최적화
- Touch-friendly controls
- Collapsible sidebars
- Bottom navigation (mobile)

---

## Docker Compose: Final Stack

```yaml
services:
  # === Application ===
  api:            # ASP.NET Core API
  frontend:       # React UI
  worker:         # Background jobs

  # === Storage ===
  postgres:       # PostgreSQL + pgvector
  qdrant:         # Vector DB
  neo4j:          # Graph DB
  redis:          # Cache

  # === Observability ===
  jaeger:         # Distributed tracing
  prometheus:     # Metrics
  grafana:        # Dashboards

  # === Infrastructure ===
  traefik:        # Reverse proxy + SSL

volumes:
  postgres-data:
  qdrant-data:
  neo4j-data:
  redis-data:
  grafana-data:
  prometheus-data:
```

---

## Success Metrics

| Metric | Current | Target |
|--------|---------|--------|
| Search Latency P95 | ~300ms | <100ms |
| MCP Spec Compliance | 40% | 100% |
| Graph Features | 0% | 100% |
| Observability | 20% | 90% |
| UI Pages | 8 | 15 |
| Test Coverage | ~60% | 80% |
| Docker Compose Up Time | ~2min | <1min |

---

## Timeline Summary

| Phase | Duration | Start | End |
|-------|----------|-------|-----|
| Phase 1: Core Infrastructure | 1 week | Week 1 | Week 1 |
| Phase 2: GraphRAG | 1.5 weeks | Week 2 | Week 3 |
| Phase 3: Agentic RAG | 2 weeks | Week 3 | Week 5 |
| Phase 4: Connectors | 1.5 weeks | Week 5 | Week 6 |
| Phase 5: Enterprise | 2 weeks | Week 7 | Week 8 |
| Phase 6: UI/UX | 1.5 weeks | Week 8 | Week 9 |

**Total**: ~9 weeks for full implementation

---

## Appendix A: Technology Decisions

### MCP Transport Choice
- **SSE**: Primary for real-time, web-based integrations
- **stdio**: Secondary for CLI tools (Claude Code, etc.)

### Graph Database Choice
- **Neo4j**: Already in stack, excellent GDS library
- Alternative considered: Dgraph, TigerGraph

### Observability Stack
- **OpenTelemetry**: Industry standard, vendor-neutral
- **Jaeger**: Best OSS distributed tracing
- **Prometheus + Grafana**: Industry standard metrics

### UI Framework
- **React Flow**: Best graph visualization library
- **Recharts**: Consistent with existing charts
- **shadcn/ui**: Already in use, extend

---

## Appendix B: Research Sources

1. **MCP Specification**: https://spec.modelcontextprotocol.io/
2. **RAGAS Framework**: https://docs.ragas.io/
3. **LangSmith Tracing**: https://docs.smith.langchain.com/
4. **GraphRAG (Microsoft)**: https://microsoft.github.io/graphrag/
5. **Anthropic Contextual Retrieval**: https://www.anthropic.com/news/contextual-retrieval
6. **Qdrant Hybrid Search**: https://qdrant.tech/articles/hybrid-search/
7. **Neo4j GDS**: https://neo4j.com/docs/graph-data-science/current/

---

*Document Version: 1.0*
*Last Updated: 2025-12-28*
