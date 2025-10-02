# Getting Started with FluxIndex

A step-by-step guide to setting up FluxIndex for building RAG (Retrieval-Augmented Generation) systems.

## Prerequisites

- .NET 9.0 SDK or later
- OpenAI API key (optional - only required if using AI embeddings)
- SQLite or PostgreSQL database

## Installation

### Minimal Setup (Recommended)

```bash
# Create new console project
dotnet new console -n MyRAGApp
cd MyRAGApp

# Install required packages
dotnet add package FluxIndex.SDK
dotnet add package FluxIndex.Storage.SQLite
```

### With AI Provider

```bash
# Add OpenAI integration
dotnet add package FluxIndex.AI.OpenAI
```

### Full Setup

```bash
# All features included
dotnet add package FluxIndex.SDK
dotnet add package FluxIndex.AI.OpenAI
dotnet add package FluxIndex.Storage.PostgreSQL
dotnet add package FluxIndex.Cache.Redis
dotnet add package FluxIndex.Extensions.FileFlux
dotnet add package FluxIndex.Extensions.WebFlux
```

## Configuration

### Basic Configuration (appsettings.json)

```json
{
  "OpenAI": {
    "ApiKey": "your-api-key",
    "EmbeddingModel": "text-embedding-3-small"
  },
  "FluxIndex": {
    "Storage": "SQLite",
    "ConnectionString": "Data Source=fluxindex.db",
    "Cache": "Memory"
  }
}
```

### Production Configuration (PostgreSQL + Redis)

```json
{
  "OpenAI": {
    "ApiKey": "your-api-key",
    "EmbeddingModel": "text-embedding-3-small"
  },
  "FluxIndex": {
    "Storage": "PostgreSQL",
    "ConnectionString": "Host=localhost;Database=fluxindex;Username=user;Password=pass",
    "Cache": "Redis",
    "RedisConnection": "localhost:6379"
  }
}
```

## Basic Usage

### Setting Up the Client

```csharp
using FluxIndex.SDK;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// Configure FluxIndex
services.AddFluxIndex()
    .AddSQLiteVectorStore()
    .UseOpenAIEmbedding(apiKey: "your-api-key");

var serviceProvider = services.BuildServiceProvider();
var client = serviceProvider.GetRequiredService<FluxIndexClient>();
```

### Indexing Documents

```csharp
// Index a single document
await client.Indexer.IndexDocumentAsync(
    content: "FluxIndex is a .NET RAG library.",
    documentId: "doc-001"
);

// Index with metadata
await client.Indexer.IndexDocumentAsync(
    content: "Document content here...",
    documentId: "doc-002",
    metadata: new Dictionary<string, object>
    {
        ["category"] = "technical",
        ["author"] = "John Doe"
    }
);
```

### Searching

```csharp
// Perform a search
var results = await client.Retriever.SearchAsync(
    query: "RAG library",
    topK: 5
);

// Display results
foreach (var result in results)
{
    Console.WriteLine($"Score: {result.Score:F2}");
    Console.WriteLine($"Content: {result.Content}");
    Console.WriteLine("---");
}
```

## Using the Builder Pattern

```csharp
using FluxIndex.SDK;

// Simple setup
var client = new FluxIndexClientBuilder()
    .UseOpenAI("your-api-key", "text-embedding-3-small")
    .UseSQLite("fluxindex.db")
    .UseMemoryCache()
    .Build();

// Production setup
var client = new FluxIndexClientBuilder()
    .UseOpenAI("your-api-key")
    .UsePostgreSQL("Host=localhost;Database=fluxindex;...")
    .UseRedisCache("localhost:6379")
    .Build();
```

## Document Processing

### FileFlux Integration

```bash
dotnet add package FluxIndex.Extensions.FileFlux
```

```csharp
using FluxIndex.Extensions.FileFlux;

services.AddFluxIndex()
    .AddSQLiteVectorStore()
    .UseOpenAIEmbedding(apiKey);

services.AddFileFluxIntegration(options =>
{
    options.DefaultChunkingStrategy = "Auto";
    options.DefaultMaxChunkSize = 1024;
    options.DefaultOverlapSize = 128;
});

var serviceProvider = services.BuildServiceProvider();
var fileFlux = serviceProvider.GetRequiredService<FileFluxIntegration>();

// Process PDF, DOCX, TXT files
var documentId = await fileFlux.ProcessAndIndexAsync("document.pdf");
```

### WebFlux Integration

```bash
dotnet add package FluxIndex.Extensions.WebFlux
```

```csharp
using FluxIndex.Extensions.WebFlux;

services.AddWebFluxIntegration(options =>
{
    options.DefaultMaxChunkSize = 512;
    options.DefaultChunkOverlap = 50;
});
```

## Advanced Configuration

### Azure OpenAI

```csharp
var client = new FluxIndexClientBuilder()
    .UseAzureOpenAI(
        endpoint: "https://your-resource.openai.azure.com/",
        apiKey: "your-api-key",
        deploymentName: "text-embedding-ada-002"
    )
    .UseSQLite("fluxindex.db")
    .Build();
```

### Redis Caching

```csharp
var client = new FluxIndexClientBuilder()
    .UseOpenAI(apiKey)
    .UseSQLite("fluxindex.db")
    .UseRedisCache("localhost:6379")
    .Build();
```

### Hybrid Search

```csharp
var results = await client.Retriever.SearchAsync(
    query: "machine learning",
    topK: 10,
    options: new SearchOptions
    {
        SearchStrategy = SearchStrategy.Hybrid,
        VectorWeight = 0.7f,
        KeywordWeight = 0.3f,
        UseReranking = true,
        MinimumScore = 0.7f
    }
);
```

## Running the Examples

### Clone and Run

```bash
# Clone repository
git clone https://github.com/iyulab/FluxIndex.git
cd FluxIndex/samples/FluxIndex.RealWorldDemo

# Set environment variables
export OPENAI_API_KEY="your-api-key"
export OPENAI_MODEL="gpt-3.5-turbo"
export OPENAI_EMBEDDING_MODEL="text-embedding-3-small"

# Run the demo
dotnet run
```

## Troubleshooting

### API Key Not Found

```csharp
var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrEmpty(apiKey))
{
    Console.WriteLine("Please set OPENAI_API_KEY environment variable.");
    return;
}
```

### Performance Optimization

```csharp
// Use batch processing
var options = new IndexingOptions
{
    BatchSize = 5,
    UseCache = true
};

await client.Indexer.IndexDocumentsAsync(documents, options);
```

## Next Steps

- **[Tutorial](./tutorial.md)** - Comprehensive usage guide
- **[Architecture Guide](./architecture.md)** - Clean Architecture design
- **[RAG System Guide](./FLUXINDEX_RAG_SYSTEM.md)** - Advanced patterns
- **[Samples](../samples/)** - Working examples

For more information and support, visit the [GitHub repository](https://github.com/iyulab/FluxIndex).
