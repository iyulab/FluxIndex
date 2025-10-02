# FluxIndex Tutorial

A comprehensive guide to using FluxIndex RAG library in your .NET applications.

## Table of Contents

1. [Basic Setup](#1-basic-setup)
2. [Indexing and Searching](#2-indexing-and-searching)
3. [AI Provider Integration](#3-ai-provider-integration)
4. [Hybrid Search](#4-hybrid-search)
5. [Document Processing](#5-document-processing)
6. [Performance Optimization](#6-performance-optimization)

---

## 1. Basic Setup

### Package Installation

```bash
# Required packages
dotnet add package FluxIndex.SDK

# Storage provider (choose one)
dotnet add package FluxIndex.Storage.SQLite      # For development
dotnet add package FluxIndex.Storage.PostgreSQL  # For production

# AI Provider (optional)
dotnet add package FluxIndex.AI.OpenAI
```

### Minimal Configuration

```csharp
using FluxIndex.SDK;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// Configure FluxIndex
services.AddFluxIndex()
    .AddSQLiteVectorStore()
    .UseInMemoryCache();

var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<FluxIndexClient>();
```

---

## 2. Indexing and Searching

### Indexing Documents

```csharp
// Index a single document
var docId = await client.Indexer.IndexDocumentAsync(
    content: "FluxIndex is a .NET RAG library.",
    documentId: "doc-001"
);

// Index with metadata
var docWithMeta = await client.Indexer.IndexDocumentAsync(
    content: "Detailed explanation about AI and machine learning...",
    documentId: "doc-002",
    metadata: new Dictionary<string, object>
    {
        ["category"] = "AI",
        ["author"] = "John Doe",
        ["created"] = DateTime.Now
    }
);
```

### Basic Search

```csharp
// Simple search
var results = await client.Retriever.SearchAsync(
    query: "RAG library",
    topK: 5
);

foreach (var result in results)
{
    Console.WriteLine($"Document: {result.DocumentId}");
    Console.WriteLine($"Content: {result.Content}");
    Console.WriteLine($"Score: {result.Score:F2}");
    Console.WriteLine("---");
}
```

---

## 3. AI Provider Integration

### OpenAI Configuration

```csharp
// appsettings.json
{
  "OpenAI": {
    "ApiKey": "your-api-key",
    "ModelName": "text-embedding-3-small",
    "Dimensions": 1536
  }
}

// Service registration
services.AddFluxIndex()
    .AddSQLiteVectorStore()
    .UseOpenAIEmbedding(configuration.GetSection("OpenAI"));
```

### Azure OpenAI

```csharp
services.AddFluxIndex()
    .AddSQLiteVectorStore()
    .UseAzureOpenAIEmbedding(options =>
    {
        options.Endpoint = "https://your-resource.openai.azure.com/";
        options.ApiKey = "your-api-key";
        options.DeploymentName = "text-embedding-ada-002";
    });
```

### Semantic Search with Embeddings

```csharp
// AI embedding-based search (semantic similarity)
var semanticResults = await client.Retriever.SearchAsync(
    query: "relationship between AI and natural language processing",
    topK: 10,
    options: new SearchOptions
    {
        UseEmbedding = true,
        MinScore = 0.7f
    }
);
```

---

## 4. Hybrid Search

### Combining Keyword and Semantic Search

```csharp
// Hybrid search configuration
var hybridResults = await client.Retriever.SearchAsync(
    query: "machine learning algorithms",
    topK: 10,
    options: new SearchOptions
    {
        SearchStrategy = SearchStrategy.Hybrid,
        VectorWeight = 0.7f,
        KeywordWeight = 0.3f
    }
);
```

### Adaptive Search (Recommended)

```csharp
// Automatically selects search strategy based on query complexity
var adaptiveResults = await client.Retriever.SearchAsync(
    query: "Explain the differences between deep learning and neural networks with real-world examples",
    topK: 10,
    options: new SearchOptions
    {
        SearchStrategy = SearchStrategy.Adaptive
    }
);
```

---

## 5. Document Processing

### FileFlux Extension

```bash
# Add document processing package
dotnet add package FluxIndex.Extensions.FileFlux
```

```csharp
using FluxIndex.Extensions.FileFlux;

// Configure FluxIndex
services.AddFluxIndex()
    .AddSQLiteVectorStore()
    .UseOpenAIEmbedding(config.GetSection("OpenAI"));

// Add FileFlux extension
services.AddFileFluxIntegration(options =>
{
    options.DefaultChunkingStrategy = "Semantic";
    options.DefaultMaxChunkSize = 1024;
    options.DefaultOverlapSize = 128;
});

var serviceProvider = services.BuildServiceProvider();
var client = serviceProvider.GetRequiredService<FluxIndexClient>();
var fileFlux = serviceProvider.GetRequiredService<FileFluxIntegration>();

// Index PDF, DOCX, TXT files
var documentId = await fileFlux.ProcessAndIndexAsync(
    filePath: "documents/manual.pdf",
    options: new ProcessingOptions
    {
        ChunkingStrategy = "Semantic",
        MaxChunkSize = 1024,
        OverlapSize = 128
    }
);

Console.WriteLine($"Indexed document ID: {documentId}");
```

### WebFlux Extension

```bash
dotnet add package FluxIndex.Extensions.WebFlux
```

```csharp
using FluxIndex.Extensions.WebFlux;

// Configure WebFlux
services.AddWebFluxIntegration(options =>
{
    options.DefaultMaxChunkSize = 512;
    options.DefaultChunkOverlap = 50;
});
```

---

## 6. Performance Optimization

### Caching

```bash
dotnet add package FluxIndex.Cache.Redis
```

```csharp
// Redis caching for performance
services.AddFluxIndex()
    .AddSQLiteVectorStore()
    .UseOpenAIEmbedding(config.GetSection("OpenAI"))
    .UseRedisCache("localhost:6379");

// Search with caching
var cachedResults = await client.Retriever.SearchAsync(
    query: "frequently searched content",
    topK: 5,
    options: new SearchOptions
    {
        UseCache = true,
        CacheTTL = TimeSpan.FromHours(1)
    }
);
```

### Batch Indexing

```csharp
// Efficient bulk document processing
var documents = new[]
{
    new IndexRequest("Document content 1", "doc-001"),
    new IndexRequest("Document content 2", "doc-002"),
    new IndexRequest("Document content 3", "doc-003")
};

var batchResults = await client.Indexer.IndexBatchAsync(
    documents: documents,
    options: new IndexingOptions
    {
        BatchSize = 100,
        MaxParallelism = 4
    }
);
```

### PostgreSQL Production Setup

```csharp
// Production environment configuration
services.AddFluxIndex()
    .UsePostgreSQLVectorStore(options =>
    {
        options.ConnectionString = "Host=localhost;Database=vectordb;Username=user;Password=pass";
        options.EmbeddingDimensions = 1536;
        options.AutoMigrate = true;
    })
    .UseOpenAIEmbedding(config.GetSection("OpenAI"))
    .UseRedisCache(config.GetConnectionString("Redis"));
```

---

## Complete RAG System Example

```csharp
using FluxIndex.SDK;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Load configuration
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables()
    .Build();

// Register services
var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole());

services.AddFluxIndex()
    .UsePostgreSQLVectorStore(config.GetSection("Database"))
    .UseOpenAIEmbedding(config.GetSection("OpenAI"))
    .UseRedisCache(config.GetConnectionString("Redis"));

services.AddFileFluxIntegration();

var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<FluxIndexClient>();
var fileFlux = provider.GetRequiredService<FileFluxIntegration>();
var logger = provider.GetRequiredService<ILogger<Program>>();

// 1. Index documents
logger.LogInformation("Starting document indexing...");
await fileFlux.ProcessAndIndexAsync("docs/manual.pdf");

// 2. Process user query
var userQuery = "How do I install the product?";
logger.LogInformation("User query: {Query}", userQuery);

// 3. Search with adaptive strategy
var searchResults = await client.Retriever.SearchAsync(
    query: userQuery,
    topK: 5,
    options: new SearchOptions
    {
        SearchStrategy = SearchStrategy.Adaptive,
        UseCache = true
    }
);

// 4. Display results
logger.LogInformation("Found {Count} documents", searchResults.Count());
foreach (var result in searchResults)
{
    logger.LogInformation("Document: {DocumentId}, Score: {Score:F2}",
        result.DocumentId, result.Score);
}
```

---

## Next Steps

1. **Advanced Features**: Learn about internal architecture in the [Architecture Guide](architecture.md)
2. **Performance Tuning**: Benchmarks and optimization strategies
3. **Real Examples**: Explore various use cases in the `samples/` directory
4. **API Documentation**: Detailed API reference for each package

Start building your RAG system with FluxIndex! 🚀
