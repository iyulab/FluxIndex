# Getting Started with FluxIndex

Quick guide to setting up FluxIndex RAG library in your .NET application.

## Prerequisites

- .NET 9.0 SDK or later
- OpenAI API key (optional)
- Basic C# knowledge

## Installation

### Option 1: Minimal Setup (Development)

```bash
dotnet new console -n MyRAGApp
cd MyRAGApp
dotnet add package FluxIndex.SDK
dotnet add package FluxIndex.Storage.SQLite
```

### Option 2: Production Setup

```bash
dotnet add package FluxIndex.SDK
dotnet add package FluxIndex.AI.OpenAI
dotnet add package FluxIndex.Storage.PostgreSQL
dotnet add package FluxIndex.Cache.Redis
```

## 5-Minute Quick Start

### 1. Basic Setup

```csharp
using FluxIndex.SDK;

var client = new FluxIndexClientBuilder()
    .UseSQLite("fluxindex.db")
    .Build();
```

### 2. Index Documents

```csharp
await client.Indexer.IndexDocumentAsync(
    content: "FluxIndex is a .NET RAG library.",
    documentId: "doc-001"
);

await client.Indexer.IndexDocumentAsync(
    content: "It supports vector and keyword search.",
    documentId: "doc-002"
);
```

### 3. Search

```csharp
var results = await client.Retriever.SearchAsync(
    query: "RAG library",
    topK: 5
);

foreach (var result in results)
{
    Console.WriteLine($"{result.Score:F2}: {result.Content}");
}
```

## Configuration Options

### Using OpenAI Embeddings

```csharp
var client = new FluxIndexClientBuilder()
    .UseSQLite("fluxindex.db")
    .UseOpenAI("your-api-key", "text-embedding-3-small")
    .Build();
```

### Using Azure OpenAI

```csharp
var client = new FluxIndexClientBuilder()
    .UseSQLite("fluxindex.db")
    .UseAzureOpenAI(
        endpoint: "https://your-resource.openai.azure.com/",
        apiKey: "your-api-key",
        deploymentName: "text-embedding-ada-002"
    )
    .Build();
```

### Production Configuration

```csharp
var client = new FluxIndexClientBuilder()
    .UsePostgreSQL("Host=localhost;Database=fluxindex;Username=user;Password=pass")
    .UseOpenAI("your-api-key")
    .UseRedisCache("localhost:6379")
    .Build();
```

## Using Dependency Injection

```csharp
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

services.AddFluxIndex()
    .AddSQLiteVectorStore()
    .UseOpenAIEmbedding(apiKey: "your-api-key");

var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<FluxIndexClient>();
```

## Document Processing

### PDF, DOCX, TXT Files

```bash
dotnet add package FluxIndex.Extensions.FileFlux
```

```csharp
using FluxIndex.Extensions.FileFlux;

services.AddFileFluxIntegration(options =>
{
    options.DefaultChunkingStrategy = "Semantic";
    options.DefaultMaxChunkSize = 1024;
    options.DefaultOverlapSize = 128;
});

var fileFlux = provider.GetRequiredService<FileFluxIntegration>();
var docId = await fileFlux.ProcessAndIndexAsync("document.pdf");
```

### Web Content

```bash
dotnet add package FluxIndex.Extensions.WebFlux
```

```csharp
using FluxIndex.Extensions.WebFlux;

services.AddWebFluxIntegration();
```

## Search Strategies

### Keyword Search (BM25)

```csharp
var results = await client.Retriever.SearchAsync(
    query: "exact terms",
    topK: 10,
    options: new SearchOptions
    {
        SearchStrategy = SearchStrategy.KeywordOnly
    }
);
```

### Vector Search (Semantic)

```csharp
var results = await client.Retriever.SearchAsync(
    query: "similar meaning",
    topK: 10,
    options: new SearchOptions
    {
        SearchStrategy = SearchStrategy.DirectVector
    }
);
```

### Hybrid Search (Recommended)

```csharp
var results = await client.Retriever.SearchAsync(
    query: "machine learning",
    topK: 10,
    options: new SearchOptions
    {
        SearchStrategy = SearchStrategy.Hybrid,
        VectorWeight = 0.7f,
        KeywordWeight = 0.3f
    }
);
```

### Adaptive Search (Auto-select)

```csharp
var results = await client.Retriever.SearchAsync(
    query: "complex query here",
    topK: 10,
    options: new SearchOptions
    {
        SearchStrategy = SearchStrategy.Adaptive
    }
);
```

## Environment Variables

```bash
# Linux/macOS
export OPENAI_API_KEY="your-api-key"

# Windows
set OPENAI_API_KEY=your-api-key

# PowerShell
$env:OPENAI_API_KEY="your-api-key"
```

```csharp
var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
var client = new FluxIndexClientBuilder()
    .UseSQLite("fluxindex.db")
    .UseOpenAI(apiKey)
    .Build();
```

## Configuration File (appsettings.json)

```json
{
  "FluxIndex": {
    "Storage": "SQLite",
    "ConnectionString": "Data Source=fluxindex.db",
    "OpenAI": {
      "ApiKey": "your-api-key",
      "Model": "text-embedding-3-small"
    }
  }
}
```

```csharp
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var client = new FluxIndexClientBuilder()
    .UseSQLite(config["FluxIndex:ConnectionString"])
    .UseOpenAI(config["FluxIndex:OpenAI:ApiKey"])
    .Build();
```

## Troubleshooting

### SQLite Database Locked

```csharp
var client = new FluxIndexClientBuilder()
    .UseSQLite("fluxindex.db", options =>
    {
        options.EnableWAL = true;  // Write-Ahead Logging
        options.BusyTimeout = 5000;
    })
    .Build();
```

### OpenAI Rate Limits

```csharp
var client = new FluxIndexClientBuilder()
    .UseSQLite("fluxindex.db")
    .UseOpenAI(apiKey, options =>
    {
        options.MaxRetries = 3;
        options.RetryDelay = TimeSpan.FromSeconds(1);
    })
    .Build();
```

### Memory Issues with Large Batches

```csharp
// Process in smaller batches
var documents = GetLargeDocumentList();
var batchSize = 100;

for (int i = 0; i < documents.Count; i += batchSize)
{
    var batch = documents.Skip(i).Take(batchSize);
    await client.Indexer.IndexBatchAsync(batch);
}
```

## Next Steps

- [Tutorial](./tutorial.md) - Learn advanced features
- [Architecture](./architecture.md) - Understand the design
- [Examples](../samples/) - See working code
- [Benchmarks](../benchmarks/FluxIndex.Benchmarks/BENCHMARK_RESULTS.md) - Performance metrics

## Quick Tips

1. **Use SQLite for development**, PostgreSQL for production
2. **Enable caching** for frequently searched queries
3. **Use adaptive search** when query complexity varies
4. **Batch operations** for better performance
5. **Monitor performance** with built-in metrics

Start building your RAG system now! For questions or issues, visit the [GitHub repository](https://github.com/iyulab/FluxIndex).
