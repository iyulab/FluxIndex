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

var context = FluxIndexContext.CreateBuilder()
    .UseSQLite("fluxindex.db")
    .Build();
```

### 2. Index Documents

```csharp
await context.Indexer.IndexDocumentAsync(
    content: "FluxIndex is a .NET RAG library for semantic search.",
    documentId: "doc-001"
);

await context.Indexer.IndexDocumentAsync(
    content: "It supports vector, keyword, and hybrid search strategies.",
    documentId: "doc-002"
);
```

### 3. Search

```csharp
var results = await context.Retriever.SearchAsync(
    query: "RAG library for .NET",
    maxResults: 5
);

foreach (var result in results)
{
    Console.WriteLine($"Score: {result.Score:F2}");
    Console.WriteLine($"Content: {result.DocumentChunk.Content}");
}
```

## Configuration Options

### Using OpenAI Embeddings

```csharp
var context = FluxIndexContext.CreateBuilder()
    .UseSQLite("fluxindex.db")
    .UseOpenAI("your-api-key", "text-embedding-3-small")
    .Build();
```

### Using Azure OpenAI

```csharp
var context = FluxIndexContext.CreateBuilder()
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
var context = FluxIndexContext.CreateBuilder()
    .UsePostgreSQL("Host=localhost;Database=fluxindex;Username=user;Password=pass")
    .UseOpenAI("your-api-key", "text-embedding-3-small")
    .UseRedisCache("localhost:6379")
    .Build();
```

## Using Dependency Injection

```csharp
using Microsoft.Extensions.DependencyInjection;
using FluxIndex.SDK;

// Register FluxIndexContext as singleton
var services = new ServiceCollection();

services.AddSingleton<IFluxIndexContext>(provider =>
{
    return FluxIndexContext.CreateBuilder()
        .UseSQLite("fluxindex.db")
        .UseOpenAI("your-api-key", "text-embedding-3-small")
        .Build();
});

var serviceProvider = services.BuildServiceProvider();
var context = serviceProvider.GetRequiredService<IFluxIndexContext>();
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

FluxIndex supports multiple search strategies. The default is **Adaptive**, which automatically selects the best approach.

### Basic Search (Adaptive - Recommended)

```csharp
// Automatically selects best strategy based on query complexity
var results = await context.Retriever.SearchAsync(
    query: "How does machine learning work?",
    maxResults: 10
);
```

For more advanced search strategies (Keyword, Vector, Hybrid), see the [Tutorial](./TUTORIAL.md#3-search-strategies).

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
var context = FluxIndexContext.CreateBuilder()
    .UseSQLite("fluxindex.db")
    .UseOpenAI(apiKey, "text-embedding-3-small")
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

var context = FluxIndexContext.CreateBuilder()
    .UseSQLite(config["FluxIndex:ConnectionString"])
    .UseOpenAI(
        config["FluxIndex:OpenAI:ApiKey"],
        config["FluxIndex:OpenAI:Model"]
    )
    .Build();
```

## Troubleshooting

### SQLite Database Locked

SQLite uses WAL (Write-Ahead Logging) mode by default in FluxIndex. If you still encounter locks:

```csharp
// Ensure only one FluxIndexContext instance per database file
// Or use PostgreSQL for multi-instance scenarios
var context = FluxIndexContext.CreateBuilder()
    .UsePostgreSQL(connectionString)  // Better for production
    .Build();
```

### Memory Issues with Large Batches

```csharp
// Process in smaller batches
var documents = GetLargeDocumentList();
var batchSize = 1000;  // Optimal batch size based on benchmarks

for (int i = 0; i < documents.Count; i += batchSize)
{
    var batch = documents.Skip(i).Take(batchSize).ToList();
    await context.Indexer.IndexBatchAsync(batch, parallelism: 8);
}
```

### OpenAI API Rate Limits

Embedding cache in FluxIndex automatically reduces API calls for repeated queries. For additional rate limiting:

```csharp
// Add delays between batches if needed
await Task.Delay(TimeSpan.FromSeconds(1));
```

## Next Steps

- [Tutorial](./TUTORIAL.md) - Learn advanced features
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
