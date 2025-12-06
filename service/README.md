# FluxIndex Service

A self-contained RAG (Retrieval-Augmented Generation) service built on the FluxIndex library stack.

## Overview

FluxIndex Service transforms the FluxIndex library into a production-ready, self-contained service with:

- **Backend**: ASP.NET Core Web API with Clean Architecture
- **Frontend**: React + TypeScript + Vite + shadcn/ui
- **Database**: PostgreSQL with pgvector for vector storage
- **Cache**: Redis for caching and rate limiting
- **Graph**: Neo4j for document relationships (optional)

## Quick Start

### Development Mode

1. Start infrastructure services:
```bash
docker-compose -f docker-compose.dev.yml up -d
```

2. Start the backend:
```bash
cd src/FluxIndex.Service.Api
dotnet run
```

3. Start the frontend:
```bash
cd frontend
npm install
npm run dev
```

4. Open http://localhost:5173

### Production Mode

1. Copy `.env.example` to `.env` and configure:
```bash
cp .env.example .env
# Edit .env with your OpenAI API key
```

2. Start all services:
```bash
docker-compose up -d
```

3. Open http://localhost:3000

## Architecture

```
service/
├── src/
│   ├── FluxIndex.Service.Domain/      # Domain entities
│   ├── FluxIndex.Service.Application/ # Business logic
│   ├── FluxIndex.Service.Infrastructure/ # Data access
│   ├── FluxIndex.Service.Shared/      # DTOs
│   └── FluxIndex.Service.Api/         # Web API
├── frontend/                          # React frontend
├── docker-compose.yml                 # Production compose
└── docker-compose.dev.yml             # Development compose
```

## API Endpoints

### Collections
- `GET /api/v1/collections` - List collections
- `POST /api/v1/collections` - Create collection
- `GET /api/v1/collections/{id}` - Get collection
- `PUT /api/v1/collections/{id}` - Update collection
- `DELETE /api/v1/collections/{id}` - Delete collection

### Documents
- `GET /api/v1/documents` - List documents
- `POST /api/v1/documents/upload` - Upload document
- `GET /api/v1/documents/{id}` - Get document
- `DELETE /api/v1/documents/{id}` - Delete document

### Search
- `POST /api/v1/search` - Semantic search

### Jobs
- `GET /api/v1/jobs` - List indexing jobs (paginated)
- `GET /api/v1/jobs/summary` - Get job status summary
- `GET /api/v1/jobs/{id}` - Get job status
- `POST /api/v1/jobs/{id}/cancel` - Cancel a pending/processing job

### Analytics
- `GET /api/v1/analytics/system` - System statistics
- `GET /api/v1/analytics/search` - Search analytics
- `GET /api/v1/analytics/documents` - Document analytics

## Authentication

All API endpoints (except health checks) require an API key in the `X-API-Key` header.

### Roles
- **Reader**: Search and read operations
- **Writer**: Upload and modify documents
- **Admin**: Full access including API key management

## Configuration

### Backend (appsettings.json)

```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=localhost;Port=5432;Database=fluxindex;...",
    "Redis": "localhost:6379"
  },
  "FluxIndex": {
    "Embedding": {
      "Provider": "OpenAI",
      "Model": "text-embedding-3-small"
    }
  }
}
```

### Environment Variables

- `OPENAI_API_KEY`: Your OpenAI API key
- `ConnectionStrings__PostgreSQL`: PostgreSQL connection string
- `ConnectionStrings__Redis`: Redis connection string

## Development

### Prerequisites

- .NET 10.0 SDK
- Node.js 22+
- Docker and Docker Compose
- PostgreSQL with pgvector (or use Docker)

### Building

```bash
# Build backend
dotnet build

# Build frontend
cd frontend && npm run build
```

### Testing

```bash
# Run backend tests
dotnet test

# Run frontend tests
cd frontend && npm test
```

## License

MIT
