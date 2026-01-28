# FluxIndex SDK Configuration Examples

This document provides comprehensive configuration examples for the FluxIndex SDK integration in FluxIndex.Stack.

## Table of Contents

1. [Basic Configuration](#basic-configuration)
2. [AI Provider Configurations](#ai-provider-configurations)
3. [Vector Store Configurations](#vector-store-configurations)
4. [Cache Configurations](#cache-configurations)
5. [Advanced Features](#advanced-features)
6. [Production Configuration](#production-configuration)

---

## Basic Configuration

### Minimal Development Setup (LocalEmbedder + SQLite)

**appsettings.Development.json**:
```json
{
  "FluxIndex": {
    "VectorStore": {
      "Provider": "SQLite",
      "ConnectionString": "Data Source=fluxindex_dev.db"
    },
    "Embedding": {
      "Provider": "LocalEmbedder",
      "ModelName": "all-MiniLM-L6-v2"
    },
    "Cache": {
      "CacheProvider": "Memory",
      "MaxCacheSize": 1000,
      "EnableSearchCache": true
    },
    "Indexing": {
      "ChunkingDefaults": {
        "Strategy": "Auto",
        "MaxChunkSize": 512,
        "OverlapSize": 64
      }
    }
  }
}
```

**Program.cs**:
```csharp
// Option 1: Use development preset
builder.Services.AddFluxIndexSDKDevelopment();

// Option 2: Use configuration
builder.Services.AddFluxIndexSDK(builder.Configuration);
```

---

## AI Provider Configurations

### OpenAI Configuration

```json
{
  "FluxIndex": {
    "Embedding": {
      "Provider": "OpenAI",
      "ApiKey": "sk-proj-...",
      "ModelName": "text-embedding-3-small"
    }
  }
}
```

**Supported Models**:
- `text-embedding-3-small` (1536 dimensions, cost-effective)
- `text-embedding-3-large` (3072 dimensions, higher quality)
- `text-embedding-ada-002` (1536 dimensions, legacy)

### Azure OpenAI Configuration

```json
{
  "FluxIndex": {
    "Embedding": {
      "Provider": "AzureOpenAI",
      "ApiKey": "your-azure-api-key",
      "ModelName": "text-embedding-ada-002",
      "ProviderSpecificOptions": {
        "Endpoint": "https://your-resource.openai.azure.com/"
      }
    }
  }
}
```

### LocalEmbedder Configuration (No API Key Required)

```json
{
  "FluxIndex": {
    "Embedding": {
      "Provider": "LocalEmbedder",
      "ModelName": "all-MiniLM-L6-v2"
    }
  }
}
```

**Available Local Models**:
- `all-MiniLM-L6-v2` (384 dimensions, fast, English)
- `all-mpnet-base-v2` (768 dimensions, better quality, English)
- `bge-small-en-v1.5` (384 dimensions, English)
- `multilingual-e5-small` (384 dimensions, 100+ languages including Korean)

### Multilingual Configuration

```json
{
  "FluxIndex": {
    "Embedding": {
      "Provider": "Multilingual",
      "ModelName": "multilingual-e5-small"
    }
  }
}
```

### GPUStack Configuration (Self-Hosted)

```json
{
  "FluxIndex": {
    "Embedding": {
      "Provider": "GPUStack",
      "ApiKey": "your-gpustack-key",
      "ModelName": "BAAI/bge-m3",
      "ProviderSpecificOptions": {
        "Endpoint": "http://localhost:80",
        "Dimensions": 1024
      }
    }
  }
}
```

### OpenAI-Compatible Endpoints (Ollama, LM Studio, vLLM)

```json
{
  "FluxIndex": {
    "Embedding": {
      "Provider": "OpenAICompatible",
      "ApiKey": "not-required-for-ollama",
      "ModelName": "nomic-embed-text",
      "ProviderSpecificOptions": {
        "Endpoint": "http://localhost:11434/v1",
        "Dimensions": 768
      }
    }
  }
}
```

---

## Vector Store Configurations

### PostgreSQL with pgvector

```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=localhost;Port=5432;Database=fluxindex;Username=postgres;Password=password"
  },
  "FluxIndex": {
    "VectorStore": {
      "Provider": "PostgreSQL",
      "MaxConnections": 20,
      "ConnectionTimeout": "00:00:30",
      "EnableAutoMigration": true
    }
  }
}
```

**Or specify connection string directly**:
```json
{
  "FluxIndex": {
    "VectorStore": {
      "Provider": "PostgreSQL",
      "ConnectionString": "Host=localhost;Database=fluxindex;Username=postgres;Password=password"
    }
  }
}
```

### SQLite (File-based)

```json
{
  "FluxIndex": {
    "VectorStore": {
      "Provider": "SQLite",
      "ConnectionString": "Data Source=/path/to/fluxindex.db"
    }
  }
}
```

### SQLite (In-Memory for Testing)

```json
{
  "FluxIndex": {
    "VectorStore": {
      "Provider": "InMemory"
    }
  }
}
```

---

## Cache Configurations

### Redis Cache (Production)

```json
{
  "ConnectionStrings": {
    "Redis": "localhost:6379,password=your-redis-password"
  },
  "FluxIndex": {
    "Cache": {
      "CacheProvider": "Redis",
      "EnableSearchCache": true,
      "EnableEmbeddingCache": true,
      "CacheTTL": "01:00:00"
    }
  }
}
```

### In-Memory Cache (Development)

```json
{
  "FluxIndex": {
    "Cache": {
      "CacheProvider": "Memory",
      "MaxCacheSize": 1000,
      "EnableSearchCache": true,
      "CacheTTL": "00:30:00"
    }
  }
}
```

### No Cache

```json
{
  "FluxIndex": {
    "Cache": {
      "CacheProvider": "None"
    }
  }
}
```

---

## Advanced Features

### Quality Monitoring

```json
{
  "FluxIndex": {
    "QualityMonitoring": {
      "EnableMonitoring": true,
      "EnableRealTimeAlerts": true,
      "MetricsInterval": "00:01:00",
      "AlertCheckInterval": "00:05:00",
      "MaxMetricsHistory": 1440
    }
  }
}
```

### Chunking Strategies

```json
{
  "FluxIndex": {
    "Indexing": {
      "ChunkingDefaults": {
        "Strategy": "Auto",
        "MaxChunkSize": 1024,
        "OverlapSize": 128,
        "PreserveFormatting": false
      },
      "MaxParallelDocuments": 8,
      "ChunkBatchSize": 100,
      "EnableProgressReporting": true
    }
  }
}
```

**Available Chunking Strategies**:
- `Auto` - Automatically selects best strategy
- `Fixed` - Fixed-size chunks
- `Sentence` - Sentence-based chunking
- `Paragraph` - Paragraph-based chunking
- `Semantic` - Semantic similarity-based chunking

### Search Configuration

```json
{
  "FluxIndex": {
    "Search": {
      "DefaultMaxResults": 20,
      "DefaultMinScore": 0.3,
      "DefaultVectorWeight": 0.7,
      "DefaultKeywordWeight": 0.3,
      "EnableHighlighting": true,
      "EnableFaceting": true,
      "SearchTimeout": "00:00:10"
    }
  }
}
```

### Parallel Processing

```json
{
  "FluxIndex": {
    "Indexing": {
      "MaxParallelDocuments": 8,
      "ChunkBatchSize": 100
    }
  }
}
```

---

## Production Configuration

### Full Production Setup with OpenAI

**appsettings.Production.json**:
```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=prod-db.example.com;Port=5432;Database=fluxindex;Username=fluxindex_user;Password=secure-password;SSL Mode=Require",
    "Redis": "prod-redis.example.com:6379,password=redis-password,ssl=true,abortConnect=false"
  },
  "FluxIndex": {
    "VectorStore": {
      "Provider": "PostgreSQL",
      "MaxConnections": 50,
      "ConnectionTimeout": "00:00:30",
      "EnableAutoMigration": false
    },
    "Embedding": {
      "Provider": "OpenAI",
      "ApiKey": "sk-proj-production-key",
      "ModelName": "text-embedding-3-small",
      "BatchSize": 100,
      "MaxRetries": 3,
      "RetryDelay": "00:00:01",
      "EnableCache": true
    },
    "Cache": {
      "CacheProvider": "Redis",
      "EnableSearchCache": true,
      "EnableEmbeddingCache": true,
      "CacheTTL": "06:00:00",
      "MaxCacheSize": 50000
    },
    "Indexing": {
      "MaxParallelDocuments": 16,
      "ChunkBatchSize": 200,
      "EnableProgressReporting": true,
      "ValidateEmbeddings": true,
      "ChunkingDefaults": {
        "Strategy": "Auto",
        "MaxChunkSize": 1024,
        "OverlapSize": 128
      }
    },
    "Search": {
      "DefaultMaxResults": 20,
      "DefaultMinScore": 0.5,
      "DefaultVectorWeight": 0.7,
      "DefaultKeywordWeight": 0.3,
      "SearchTimeout": "00:00:05"
    },
    "QualityMonitoring": {
      "EnableMonitoring": true,
      "EnableRealTimeAlerts": true,
      "MetricsInterval": "00:01:00",
      "AlertCheckInterval": "00:05:00"
    }
  }
}
```

**Program.cs**:
```csharp
// Use production preset with validation
builder.Services.AddFluxIndexSDKProduction(builder.Configuration);
```

### Hybrid Setup (Local Embeddings + Production Infrastructure)

```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=prod-db.example.com;Port=5432;Database=fluxindex",
    "Redis": "prod-redis.example.com:6379"
  },
  "FluxIndex": {
    "VectorStore": {
      "Provider": "PostgreSQL"
    },
    "Embedding": {
      "Provider": "LocalEmbedder",
      "ModelName": "all-mpnet-base-v2"
    },
    "Cache": {
      "CacheProvider": "Redis",
      "EnableSearchCache": true
    }
  }
}
```

This configuration uses local embeddings (no API costs) while leveraging production-grade infrastructure (PostgreSQL + Redis).

---

## Usage Examples

### Basic Usage in Controllers

```csharp
using FluxIndex.SDK;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class SearchController : ControllerBase
{
    private readonly IFluxIndexContext _fluxIndex;
    private readonly ILogger<SearchController> _logger;

    public SearchController(
        IFluxIndexContext fluxIndex,
        ILogger<SearchController> logger)
    {
        _fluxIndex = fluxIndex;
        _logger = logger;
    }

    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] SearchRequest request)
    {
        try
        {
            var results = await _fluxIndex.SearchAsync(
                request.Query,
                maxResults: request.MaxResults ?? 10,
                minScore: request.MinScore ?? 0.5f,
                cancellationToken: HttpContext.RequestAborted);

            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed for query: {Query}", request.Query);
            return StatusCode(500, "Search failed");
        }
    }
}
```

### Advanced Usage with Direct Retriever Access

```csharp
using FluxIndex.SDK;

public class DocumentService
{
    private readonly Retriever _retriever;
    private readonly Indexer _indexer;

    public DocumentService(Retriever retriever, Indexer indexer)
    {
        _retriever = retriever;
        _indexer = indexer;
    }

    public async Task<string> IndexDocumentAsync(string content, Dictionary<string, object> metadata)
    {
        var document = Document.Create(content, metadata);
        return await _indexer.IndexDocumentAsync(document);
    }

    public async Task<IEnumerable<SearchResult>> SearchAsync(string query)
    {
        var vectorResults = await _retriever.SearchAsync(query, maxResults: 10);
        // Process results...
        return ConvertResults(vectorResults);
    }
}
```

---

## Environment Variables

You can also configure FluxIndex using environment variables:

```bash
# Vector Store
FluxIndex__VectorStore__Provider=PostgreSQL
FluxIndex__VectorStore__ConnectionString="Host=localhost;Database=fluxindex"

# Embedding
FluxIndex__Embedding__Provider=OpenAI
FluxIndex__Embedding__ApiKey=sk-proj-...
FluxIndex__Embedding__ModelName=text-embedding-3-small

# Cache
FluxIndex__Cache__CacheProvider=Redis
FluxIndex__Cache__RedisConnectionString=localhost:6379
```

---

## Troubleshooting

### Common Configuration Errors

1. **Missing API Key**:
   ```
   Error: OpenAI API key is required
   Solution: Set FluxIndex:Embedding:ApiKey in configuration
   ```

2. **Invalid Connection String**:
   ```
   Error: PostgreSQL connection string not configured
   Solution: Set ConnectionStrings:PostgreSQL or FluxIndex:VectorStore:ConnectionString
   ```

3. **Model Not Found**:
   ```
   Error: LocalEmbedder model 'invalid-model' not found
   Solution: Use valid model name (all-MiniLM-L6-v2, all-mpnet-base-v2, etc.)
   ```

### Validation

Use the production preset to automatically validate configuration:

```csharp
try
{
    builder.Services.AddFluxIndexSDKProduction(builder.Configuration);
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"Configuration error: {ex.Message}");
    throw;
}
```

---

## Performance Tuning

### Indexing Performance

```json
{
  "FluxIndex": {
    "Indexing": {
      "MaxParallelDocuments": 16,
      "ChunkBatchSize": 200
    },
    "Embedding": {
      "BatchSize": 100
    }
  }
}
```

### Search Performance

```json
{
  "FluxIndex": {
    "Cache": {
      "CacheProvider": "Redis",
      "EnableSearchCache": true,
      "CacheTTL": "01:00:00"
    },
    "Search": {
      "SearchTimeout": "00:00:05"
    }
  }
}
```

### Cost Optimization

Use LocalEmbedder to eliminate API costs:

```json
{
  "FluxIndex": {
    "Embedding": {
      "Provider": "LocalEmbedder",
      "ModelName": "all-mpnet-base-v2"
    }
  }
}
```

**Cost Comparison**:
- OpenAI `text-embedding-3-small`: $0.020 per 1M tokens
- LocalEmbedder: $0.00 (runs locally on CPU/GPU)

---

## Best Practices

1. **Development**: Use `AddFluxIndexSDKDevelopment()` for quick setup
2. **Production**: Use `AddFluxIndexSDKProduction()` with full validation
3. **API Keys**: Store in Azure Key Vault or AWS Secrets Manager
4. **Caching**: Always use Redis cache in production for better performance
5. **Monitoring**: Enable quality monitoring in production environments
6. **Chunking**: Start with `Auto` strategy and tune based on your content
7. **Local Embeddings**: Consider LocalEmbedder for cost savings and privacy

---

## Migration Guide

### From Old API to New Storage Architecture (v0.x)

The storage architecture has been redesigned with the principle that **RDB, VectorDB, and GraphDB are not toggle features** - they are auto-maximized based on configured providers.

**Before** (old API):
```csharp
var builder = new FluxIndexContextBuilder();
builder.UseSQLite(dbPath);           // ❌ Old API
builder.UsePostgreSQLGraph();        // ❌ Removed
builder.WithoutGraph();              // ❌ Removed
builder.VectorOnly();                // ❌ Removed
var context = builder.Build();
```

**After** (new API):
```csharp
var builder = new FluxIndexContextBuilder();

// Option 1: Local mode (SQLite handles all)
builder.UseLocalStorage(dbPath);

// Option 2: PostgreSQL mode
builder.UsePostgreSQL(connectionString);

// Option 3: Best-in-class (PostgreSQL + Qdrant + Neo4j)
builder.UseBestInClass(
    postgresConnectionString,
    qdrant => { qdrant.Host = "localhost"; qdrant.Port = 6334; },
    neo4j => { neo4j.Uri = "bolt://localhost:7687"; });

var context = builder.Build();
```

**API Changes Summary**:
| Old API | New API | Notes |
|---------|---------|-------|
| `UseSQLite()` | `UseLocalStorage()` | SQLite handles Vector + Graph + RDB + Cache |
| `UseSQLiteInMemory()` | `UseLocalStorage(":memory:")` | In-memory SQLite |
| `UsePostgreSQLGraph()` | Removed | Auto-determined by provider |
| `UseSQLiteGraph()` | Removed | Auto-determined by provider |
| `WithoutGraph()` | Removed | Features can't be disabled |
| `WithoutSemanticCache()` | Removed | Features can't be disabled |
| `VectorOnly()` | Removed | Features can't be disabled |
| `UseNeo4jGraph()` | `UseNeo4j()` | Specialized graph provider |
| - | `UseBestInClass()` | New preset for production |

### From Manual DI Registration to Extension Method

**Before**:
```csharp
services.AddSingleton<IVectorStore, PostgreSQLVectorStore>();
services.AddSingleton<IEmbeddingService, OpenAIEmbeddingService>();
// ... many more registrations
```

**After**:
```csharp
services.AddFluxIndexSDK(configuration);
// All services registered automatically
```

---

## Additional Resources

- [FluxIndex SDK Documentation](../src/FluxIndex.SDK/README.md)
- [FluxIndex Core Documentation](../src/FluxIndex.Core/README.md)
- [API Reference](../docs/api-reference.md)
- [Architecture Overview](../docs/architecture.md)
