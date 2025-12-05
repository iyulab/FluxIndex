# FluxIndex Demo

A comprehensive demo application showcasing FluxIndex RAG infrastructure capabilities with PostgreSQL (pgvector), Neo4j, and Redis.

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- (Optional) OpenAI API Key for cloud embeddings

### 1. Start Infrastructure

```powershell
# Windows
.\setup.ps1 start

# Linux/Mac
./setup.sh start
```

This starts:
- **PostgreSQL 16** with pgvector extension (port 5432)
- **Neo4j 5.26** for graph relationships (ports 7474, 7687)
- **Redis 7** for caching (port 6379)

### 2. Configure Environment

Edit `.env` file or copy from `.env.example`:

```bash
# Storage backend: sqlite or postgresql
STORAGE_BACKEND=postgresql

# PostgreSQL connection
POSTGRES_HOST=localhost
POSTGRES_PORT=5432
POSTGRES_USER=fluxindex
POSTGRES_PASSWORD=fluxindex123
POSTGRES_DB=fluxindex

# AI Configuration (leave empty for local embeddings)
OPENAI_API_KEY=
```

### 3. Run the Demo

```powershell
cd FluxIndex.Demo
dotnet run
```

Open http://localhost:5000 in your browser.

## Features

### Document Processing
- Upload PDF, DOCX, TXT, MD, HTML, JSON, CSV files
- Automatic chunking with FileFlux
- Embedding generation (OpenAI or local models)

### Semantic Search
- Vector similarity search with pgvector
- Optional neural reranking
- MCP-compatible API endpoints

### API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/health` | GET | Health check |
| `/api/status` | GET | System status |
| `/api/upload` | POST | Upload document |
| `/api/search` | POST | Semantic search |
| `/api/documents` | GET | List documents |
| `/api/documents/{id}` | GET | Document detail |
| `/api/documents/{id}` | DELETE | Delete document |
| `/api/mcp/search` | POST | MCP-style search |

## Management Scripts

### PowerShell (Windows)

```powershell
# Start all services
.\setup.ps1 start

# Check status
.\setup.ps1 status

# View logs
.\setup.ps1 logs postgres

# Stop services
.\setup.ps1 stop

# Clean up (removes data)
.\setup.ps1 clean

# Build application
.\setup.ps1 build

# Run API tests
.\setup.ps1 test
```

### Bash (Linux/Mac)

```bash
# Start all services
./setup.sh start

# Check status
./setup.sh status

# View logs
./setup.sh logs neo4j

# Stop services
./setup.sh stop
```

## API Testing

Run comprehensive API tests:

```powershell
# Windows
.\test-api.ps1

# With custom base URL
.\test-api.ps1 -BaseUrl "http://localhost:5000"
```

## Architecture

```
┌─────────────────────────────────────────────────────┐
│                  FluxIndex Demo UI                   │
│              (HTML/CSS/JavaScript)                   │
└─────────────────────┬───────────────────────────────┘
                      │
┌─────────────────────▼───────────────────────────────┐
│              FluxIndex Demo API                      │
│           (ASP.NET Core Minimal API)                │
├─────────────────────────────────────────────────────┤
│  IndexingService  │  SearchService  │  DemoState    │
└────────┬──────────┴────────┬────────┴───────────────┘
         │                   │
┌────────▼───────────────────▼────────────────────────┐
│                FluxIndex Core                        │
├──────────────┬──────────────┬───────────────────────┤
│  FileFlux    │  Embeddings  │   Reranking           │
│  (Chunking)  │  (OpenAI/    │   (Local Neural)      │
│              │   Local)     │                       │
└──────────────┴──────────────┴───────────────────────┘
         │                   │
┌────────▼───────────────────▼────────────────────────┐
│              Storage Layer                           │
├──────────────┬──────────────┬───────────────────────┤
│ PostgreSQL   │   Neo4j      │   Redis               │
│ + pgvector   │  (Graphs)    │  (Cache)              │
└──────────────┴──────────────┴───────────────────────┘
```

## Configuration Options

### Storage Backends

| Backend | Use Case | Configuration |
|---------|----------|---------------|
| SQLite | Development, testing | `STORAGE_BACKEND=sqlite` |
| PostgreSQL | Production, scaling | `STORAGE_BACKEND=postgresql` |

### Embedding Models

| Provider | Model | Dimensions | Configuration |
|----------|-------|------------|---------------|
| OpenAI | text-embedding-3-small | 1536 | Set `OPENAI_API_KEY` |
| OpenAI | text-embedding-3-large | 3072 | Set `OPENAI_API_KEY` |
| Local | all-MiniLM-L6-v2 | 384 | Leave `OPENAI_API_KEY` empty |

## Neo4j Browser

Access Neo4j browser at http://localhost:7474

Default credentials:
- Username: `neo4j`
- Password: `fluxindex123`

## Troubleshooting

### Docker containers won't start
```powershell
# Check Docker status
docker info

# Check container logs
.\setup.ps1 logs
```

### PostgreSQL connection failed
```powershell
# Verify PostgreSQL is running
docker compose ps postgres

# Check health
docker compose exec postgres pg_isready -U fluxindex
```

### Embedding errors
- Ensure `OPENAI_API_KEY` is set correctly (or leave empty for local)
- Check API quota and billing status

## License

MIT License - See main FluxIndex repository for details.
