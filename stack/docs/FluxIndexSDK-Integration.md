# FluxIndex SDK Integration Guide

This document describes the FluxIndex SDK integration in the FluxIndex.Stack service.

## Overview

The `FluxIndexServiceExtensions` provides comprehensive integration of the FluxIndex SDK into the FluxIndex.Stack infrastructure layer. This integration supports:

- **Multiple AI Providers**: OpenAI, Azure OpenAI, LocalEmbedder, GPUStack, and OpenAI-compatible endpoints
- **Multiple Vector Stores**: PostgreSQL with pgvector, SQLite
- **Caching Strategies**: Redis, in-memory
- **Advanced Features**: Quality monitoring, hybrid search, semantic caching, quantization
- **Document Processing**: FileFlux integration for intelligent chunking

## Quick Start

### 1. Basic Setup (Development)

Add the following to your `appsettings.Development.json`:

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
    }
  }
}
```

The SDK is automatically registered when you call `AddInfrastructure()`:

```csharp
builder.Services.AddInfrastructure(builder.Configuration);
```

### 2. Production Setup

For production, use the production preset in `Program.cs`:

```csharp
// Validate configuration and register services
builder.Services.AddFluxIndexSDKProduction(builder.Configuration);
```

Add production configuration to `appsettings.Production.json`:

```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=prod-db;Database=fluxindex;Username=user;Password=pass",
    "Redis": "prod-redis:6379,password=redispass"
  },
  "FluxIndex": {
    "VectorStore": {
      "Provider": "PostgreSQL"
    },
    "Embedding": {
      "Provider": "OpenAI",
      "ApiKey": "sk-proj-...",
      "ModelName": "text-embedding-3-small"
    },
    "Cache": {
      "CacheProvider": "Redis"
    }
  }
}
```

## Extension Methods

### `AddFluxIndexSDK(IConfiguration, Action<FluxIndexOptions>?)`

Main extension method that configures FluxIndex SDK from IConfiguration.

**Usage**:
```csharp
services.AddFluxIndexSDK(configuration);

// With additional configuration
services.AddFluxIndexSDK(configuration, options =>
{
    options.Search.DefaultMaxResults = 20;
    options.QualityMonitoring.EnableMonitoring = true;
});
```

### `AddFluxIndexSDKDevelopment(string?)`

Simplified development setup with sensible defaults.

**Features**:
- SQLite in-memory database (or custom connection string)
- LocalEmbedder (no API keys required)
- In-memory caching
- Limited parallelism (2 threads)

**Usage**:
```csharp
// In-memory database
services.AddFluxIndexSDKDevelopment();

// Custom PostgreSQL
services.AddFluxIndexSDKDevelopment("Host=localhost;Database=dev");
```

### `AddFluxIndexSDKProduction(IConfiguration)`

Production preset with configuration validation.

**Features**:
- Validates all required configuration
- Enables quality monitoring
- Optimizes parallel processing
- Sets production timeouts

**Usage**:
```csharp
try
{
    services.AddFluxIndexSDKProduction(configuration);
}
catch (InvalidOperationException ex)
{
    // Handle configuration errors
    logger.LogError(ex, "FluxIndex configuration invalid");
    throw;
}
```

### `AddFluxIndexSDK(Action<FluxIndexContextBuilder>)`

Advanced custom configuration using the builder pattern.

**Usage**:
```csharp
services.AddFluxIndexSDK(builder =>
{
    // Local mode (SQLite handles all storage)
    builder.UseLocalStorage("fluxindex.db");

    // Or PostgreSQL mode
    builder.UsePostgreSQL(connectionString);

    // Add specialized providers (auto-maximize)
    builder.UseQdrant("localhost", 6334, "chunks", 1536);  // Vector (overrides PostgreSQL vector)
    builder.UseNeo4j("bolt://localhost:7687", "neo4j", "password");  // Graph (overrides PostgreSQL graph)

    // Or best-in-class preset (PostgreSQL + Qdrant + Neo4j)
    builder.UseBestInClass(
        postgresConnectionString,
        qdrant => { qdrant.Host = "localhost"; qdrant.Port = 6334; },
        neo4j => { neo4j.Uri = "bolt://localhost:7687"; });
});
```

## Configuration Options

### Vector Store Configuration

```json
{
  "FluxIndex": {
    "VectorStore": {
      "Provider": "PostgreSQL",        // PostgreSQL, SQLite, InMemory
      "ConnectionString": "...",
      "MaxConnections": 20,
      "ConnectionTimeout": "00:00:30",
      "EnableAutoMigration": false
    }
  }
}
```

**Supported Providers**:
- `PostgreSQL` / `Postgres` / `pgvector` - PostgreSQL with pgvector extension
- `SQLite` - File-based SQLite database
- `InMemory` - SQLite in-memory (testing only)

### Embedding Service Configuration

```json
{
  "FluxIndex": {
    "Embedding": {
      "Provider": "LocalEmbedder",    // LocalEmbedder, OpenAI, AzureOpenAI, etc.
      "ApiKey": "",                   // Not required for LocalEmbedder
      "ModelName": "all-MiniLM-L6-v2",
      "BatchSize": 100,
      "EnableCache": true,
      "ProviderSpecificOptions": {
        "Endpoint": "https://...",    // For Azure/GPUStack/Compatible
        "Dimensions": 1536
      }
    }
  }
}
```

**Supported Providers**:

1. **LocalEmbedder** (Recommended for development, no API costs):
   - `all-MiniLM-L6-v2` (384 dimensions, fast)
   - `all-mpnet-base-v2` (768 dimensions, better quality)
   - `multilingual-e5-small` (384 dimensions, 100+ languages)

2. **OpenAI**:
   - `text-embedding-3-small` (1536 dimensions, $0.020/1M tokens)
   - `text-embedding-3-large` (3072 dimensions, $0.130/1M tokens)

3. **AzureOpenAI** / **Azure**:
   - Requires `Endpoint` in `ProviderSpecificOptions`
   - Model is deployment name, not model name

4. **GPUStack**:
   - Self-hosted OpenAI-compatible inference
   - Requires `Endpoint` and optional `Dimensions`

5. **OpenAICompatible** / **Compatible**:
   - For Ollama, LM Studio, vLLM, etc.
   - Requires `Endpoint` configuration

### Cache Configuration

```json
{
  "FluxIndex": {
    "Cache": {
      "CacheProvider": "Redis",       // Redis, Memory, None
      "RedisConnectionString": "...", // Or use ConnectionStrings:Redis
      "EnableSearchCache": true,
      "EnableEmbeddingCache": true,
      "CacheTTL": "01:00:00",
      "MaxCacheSize": 10000
    }
  }
}
```

### Indexing Configuration

```json
{
  "FluxIndex": {
    "Indexing": {
      "MaxParallelDocuments": 8,
      "ChunkBatchSize": 100,
      "ValidateEmbeddings": true,
      "ChunkingDefaults": {
        "Strategy": "Auto",           // Auto, Fixed, Sentence, Paragraph, Semantic
        "MaxChunkSize": 512,
        "OverlapSize": 64
      }
    }
  }
}
```

### Search Configuration

```json
{
  "FluxIndex": {
    "Search": {
      "DefaultMaxResults": 10,
      "DefaultMinScore": 0.2,
      "DefaultVectorWeight": 0.7,
      "DefaultKeywordWeight": 0.3,
      "SearchTimeout": "00:00:10"
    }
  }
}
```

### Quality Monitoring

```json
{
  "FluxIndex": {
    "QualityMonitoring": {
      "EnableMonitoring": true,
      "EnableRealTimeAlerts": true,
      "MetricsInterval": "00:01:00",
      "AlertCheckInterval": "00:05:00"
    }
  }
}
```

## Dependency Injection Usage

The SDK registers the following services:

```csharp
// Main context (preferred)
public class MyService
{
    private readonly IFluxIndexContext _fluxIndex;

    public MyService(IFluxIndexContext fluxIndex)
    {
        _fluxIndex = fluxIndex;
    }

    public async Task SearchAsync(string query)
    {
        var results = await _fluxIndex.SearchAsync(query);
        return results;
    }
}

// Direct component access (advanced)
public class AdvancedService
{
    private readonly Retriever _retriever;
    private readonly Indexer _indexer;

    public AdvancedService(Retriever retriever, Indexer indexer)
    {
        _retriever = retriever;
        _indexer = indexer;
    }
}
```

## Common Scenarios

### 1. Development with No External Dependencies

```csharp
// appsettings.Development.json
{
  "FluxIndex": {
    "VectorStore": { "Provider": "InMemory" },
    "Embedding": { "Provider": "LocalEmbedder" }
  }
}

// Program.cs
builder.Services.AddFluxIndexSDKDevelopment();
```

### 2. Production with OpenAI

```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=prod-db;Database=fluxindex",
    "Redis": "prod-redis:6379"
  },
  "FluxIndex": {
    "VectorStore": { "Provider": "PostgreSQL" },
    "Embedding": {
      "Provider": "OpenAI",
      "ApiKey": "sk-proj-...",
      "ModelName": "text-embedding-3-small"
    },
    "Cache": { "CacheProvider": "Redis" }
  }
}
```

### 3. Cost-Optimized Production (Local Embeddings)

```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=prod-db;Database=fluxindex",
    "Redis": "prod-redis:6379"
  },
  "FluxIndex": {
    "VectorStore": { "Provider": "PostgreSQL" },
    "Embedding": {
      "Provider": "LocalEmbedder",
      "ModelName": "all-mpnet-base-v2"
    },
    "Cache": { "CacheProvider": "Redis" }
  }
}
```

**Benefits**:
- No API costs
- Better privacy (data stays on server)
- Predictable performance
- No rate limits

**Tradeoffs**:
- Requires CPU/GPU resources
- Slightly lower quality than OpenAI's latest models
- 768 dimensions vs 1536+ for cloud providers

### 4. Hybrid Search with Quality Monitoring

```json
{
  "FluxIndex": {
    "Search": {
      "DefaultVectorWeight": 0.7,
      "DefaultKeywordWeight": 0.3
    },
    "QualityMonitoring": {
      "EnableMonitoring": true,
      "EnableRealTimeAlerts": true
    }
  }
}
```

```csharp
// Usage
var results = await _fluxIndex.HybridSearchV2Async(query, new HybridSearchOptions
{
    TopK = 20,
    VectorWeight = 0.7f,
    KeywordWeight = 0.3f,
    RerankingStrategy = RerankingStrategy.ReciprocalRankFusion
});

// Monitor quality
var dashboard = await _fluxIndex.GetQualityDashboardAsync(TimeSpan.FromHours(1));
var alerts = await _fluxIndex.GetQualityAlertsAsync(AlertSeverity.Warning);
```

## Validation and Error Handling

The production preset validates configuration and provides detailed error messages:

```csharp
try
{
    services.AddFluxIndexSDKProduction(configuration);
}
catch (InvalidOperationException ex)
{
    // ex.Message contains all validation errors:
    // "Production configuration validation failed:
    //  - Vector store connection string is required
    //  - API key is required for openai embedding provider
    //  - Redis connection string is required when using Redis cache"
}
```

**Common Validation Errors**:

1. **Missing connection string**:
   - Solution: Configure `ConnectionStrings:PostgreSQL` or `FluxIndex:VectorStore:ConnectionString`

2. **Missing API key**:
   - Solution: Configure `FluxIndex:Embedding:ApiKey` for cloud providers

3. **Missing Azure endpoint**:
   - Solution: Configure `FluxIndex:Embedding:ProviderSpecificOptions:Endpoint`

4. **Missing Redis connection**:
   - Solution: Configure `ConnectionStrings:Redis` or `FluxIndex:Cache:RedisConnectionString`

## Performance Tuning

### Indexing Performance

```json
{
  "FluxIndex": {
    "Indexing": {
      "MaxParallelDocuments": 16,    // CPU core count
      "ChunkBatchSize": 200
    },
    "Embedding": {
      "BatchSize": 100
    }
  }
}
```

**Recommendations**:
- `MaxParallelDocuments`: Set to CPU core count
- `ChunkBatchSize`: 100-200 for optimal throughput
- `BatchSize`: Match embedding provider batch limits

### Search Performance

```json
{
  "FluxIndex": {
    "Cache": {
      "CacheProvider": "Redis",
      "EnableSearchCache": true,
      "CacheTTL": "06:00:00"
    },
    "Search": {
      "SearchTimeout": "00:00:05"
    }
  }
}
```

**Recommendations**:
- Always use Redis cache in production
- Set appropriate cache TTL based on data freshness requirements
- Use shorter timeout for user-facing search

## Migration from Manual Configuration

**Before** (manual FluxIndexContextBuilder):
```csharp
var builder = new FluxIndexContextBuilder();
builder.UseSQLite(dbPath);           // ❌ Old API
builder.UsePostgreSQLGraph();        // ❌ Removed (auto-maximize now)
builder.WithoutGraph();              // ❌ Removed (features can't be disabled)
var context = builder.Build();
services.AddSingleton(context);
```

**After** (new storage architecture v0.x):
```csharp
var builder = new FluxIndexContextBuilder();
builder.UseLocalStorage(dbPath);     // ✅ SQLite handles all storage
// Or: builder.UsePostgreSQL(connStr) for PostgreSQL mode
// Graph, SemanticCache auto-enabled based on provider capabilities
var context = builder.Build();
services.AddSingleton(context);

// Or use extension method with configuration:
services.AddFluxIndexSDK(configuration);
```

**New API Summary**:
- `UseSQLite()` → `UseLocalStorage()` (SQLite handles Vector + Graph + RDB + Cache)
- `UsePostgreSQLGraph()`, `UseSQLiteGraph()` → Removed (auto-determined by provider)
- `WithoutGraph()`, `WithoutSemanticCache()`, `VectorOnly()` → Removed (can't disable features)
- `UseNeo4jGraph()` → `UseNeo4j()` (specialized graph provider)
- New: `UseBestInClass()` preset for PostgreSQL + Qdrant + Neo4j

## Architecture

```
ServiceCollectionExtensions.AddInfrastructure()
    ↓
FluxIndexServiceExtensions.AddFluxIndexSDK()
    ↓
    ├── ConfigureVectorStore() → PostgreSQL/SQLite/InMemory
    ├── ConfigureEmbeddingService() → OpenAI/Azure/Local/GPUStack
    ├── ConfigureCacheService() → Redis/Memory/None
    ├── ConfigureChunking() → Strategy/Size/Overlap
    ├── ConfigureSearchOptions() → MaxResults/MinScore
    ├── ConfigureParallelProcessing() → Parallelism settings
    └── ConfigureQualityMonitoring() → Monitoring/Alerts
    ↓
FluxIndexContextBuilder.Build()
    ↓
Registered Services:
    - IFluxIndexContext (singleton)
    - FluxIndexContext (singleton)
    - Retriever (singleton)
    - Indexer (singleton)
    - FluxIndexOptions (singleton)
```

## Testing

### Unit Tests

```csharp
[Fact]
public async Task SearchAsync_WithConfiguration_ReturnsResults()
{
    // Arrange
    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string>
        {
            ["FluxIndex:VectorStore:Provider"] = "InMemory",
            ["FluxIndex:Embedding:Provider"] = "LocalEmbedder"
        })
        .Build();

    var services = new ServiceCollection();
    services.AddLogging();
    services.AddFluxIndexSDK(configuration);
    var provider = services.BuildServiceProvider();

    var context = provider.GetRequiredService<IFluxIndexContext>();

    // Act
    await context.IndexAsync(Document.Create("test content"));
    var results = await context.SearchAsync("test");

    // Assert
    Assert.NotEmpty(results);
}
```

### Integration Tests

```csharp
public class FluxIndexIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly IFluxIndexContext _fluxIndex;

    public FluxIndexIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _fluxIndex = factory.Services.GetRequiredService<IFluxIndexContext>();
    }

    [Fact]
    public async Task EndToEnd_IndexAndSearch_Works()
    {
        // Index
        var docId = await _fluxIndex.IndexAsync(Document.Create("integration test"));

        // Search
        var results = await _fluxIndex.SearchAsync("integration");

        Assert.Contains(results, r => r.DocumentId == docId);
    }
}
```

## Troubleshooting

### Issue: "FluxIndex SDK not registered"

**Solution**: Ensure `AddInfrastructure()` is called in `Program.cs`:
```csharp
builder.Services.AddInfrastructure(builder.Configuration);
```

### Issue: "Connection string not found"

**Solution**: Configure connection string in `appsettings.json`:
```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=localhost;Database=fluxindex"
  }
}
```

### Issue: "API key required"

**Solution**: Use LocalEmbedder for development or provide API key:
```json
{
  "FluxIndex": {
    "Embedding": {
      "Provider": "LocalEmbedder"  // No API key needed
    }
  }
}
```

### Issue: "Project reference not found"

**Solution**: Ensure all required FluxIndex packages are referenced in `.csproj`:
```xml
<ItemGroup>
  <ProjectReference Include="..\..\..\src\FluxIndex.SDK\FluxIndex.SDK.csproj" />
  <ProjectReference Include="..\..\..\src\FluxIndex.Cache.Redis\FluxIndex.Cache.Redis.csproj" />
  <ProjectReference Include="..\..\..\src\FluxIndex.Extensions.FileFlux\FluxIndex.Extensions.FileFlux.csproj" />
</ItemGroup>
```

## Best Practices

1. **Use LocalEmbedder for development**: No API keys, faster iteration
2. **Enable Redis cache in production**: Significant performance improvement
3. **Configure quality monitoring**: Catch issues early
4. **Use production preset for validation**: Fail fast on misconfiguration
5. **Tune parallelism based on workload**: Match CPU core count
6. **Monitor API costs**: Track embedding API usage if using cloud providers
7. **Consider hybrid approach**: Local embeddings + production infrastructure

## Additional Resources

- [FluxIndex SDK Documentation](../../src/FluxIndex.SDK/README.md)
- [Configuration Examples](./fluxindex-configuration-examples.md)
- [FluxIndex Core Documentation](../../src/FluxIndex.Core/README.md)
- [API Reference](./api-reference.md)
