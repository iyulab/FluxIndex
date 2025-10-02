# FluxIndex

[![CI/CD](https://github.com/iyulab/FluxIndex/actions/workflows/build-and-release.yml/badge.svg)](https://github.com/iyulab/FluxIndex/actions/workflows/build-and-release.yml)
[![NuGet](https://img.shields.io/nuget/v/FluxIndex.SDK.svg?label=FluxIndex.SDK)](https://www.nuget.org/packages/FluxIndex.SDK/)
[![License](https://img.shields.io/github/license/iyulab/FluxIndex)](LICENSE)

A .NET library for building RAG (Retrieval-Augmented Generation) systems with vector search and hybrid search capabilities.

## Overview

FluxIndex is a RAG infrastructure library that provides document indexing and retrieval functionality for .NET 9.0 applications. It combines vector search with keyword search to enable semantic and exact-match retrieval.

## Features

- **Vector Search**: Semantic similarity search using embeddings
- **Keyword Search**: BM25-based exact term matching
- **Hybrid Search**: Combines vector and keyword search with Reciprocal Rank Fusion (RRF)
- **Multiple Storage Backends**: SQLite (with sqlite-vec), PostgreSQL (with pgvector)
- **AI Provider Agnostic**: Use OpenAI, Azure OpenAI, or custom embedding services
- **Document Processing**: Integrate with FileFlux for PDF/DOCX/TXT processing
- **Web Content**: Integrate with WebFlux for web page crawling and extraction
- **Caching**: In-memory or Redis-based caching for performance
- **Clean Architecture**: Modular design with dependency injection

## Installation

### Required Packages

```bash
# Core SDK
dotnet add package FluxIndex.SDK

# Storage provider (choose one)
dotnet add package FluxIndex.Storage.SQLite      # For development
dotnet add package FluxIndex.Storage.PostgreSQL  # For production
```

### Optional Packages

```bash
# AI provider integration (or implement custom IEmbeddingService)
dotnet add package FluxIndex.AI.OpenAI

# Caching
dotnet add package FluxIndex.Cache.Redis

# Document processing extensions
dotnet add package FluxIndex.Extensions.FileFlux
dotnet add package FluxIndex.Extensions.WebFlux
```

## Quick Start

### Basic Setup

```csharp
using FluxIndex.SDK;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// Configure FluxIndex
services.AddFluxIndex()
    .AddSQLiteVectorStore()              // Storage
    .UseOpenAIEmbedding(apiKey: "...");  // AI service (optional)

var serviceProvider = services.BuildServiceProvider();
var client = serviceProvider.GetRequiredService<FluxIndexClient>();
```

### Indexing Documents

```csharp
// Index a text document
await client.Indexer.IndexDocumentAsync(
    content: "FluxIndex is a .NET RAG library for building retrieval systems.",
    documentId: "doc-001"
);

// Index with metadata
await client.Indexer.IndexDocumentAsync(
    content: "Document content...",
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
// Semantic search
var results = await client.Retriever.SearchAsync(
    query: "RAG library",
    topK: 5
);

foreach (var result in results)
{
    Console.WriteLine($"Score: {result.Score:F2}");
    Console.WriteLine($"Content: {result.Content}");
}
```

### Hybrid Search

```csharp
var results = await client.Retriever.SearchAsync(
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

## Document Processing

### FileFlux Integration

```bash
dotnet add package FluxIndex.Extensions.FileFlux
```

```csharp
using FluxIndex.Extensions.FileFlux;

// Configure FileFlux
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

// Process and index files
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

## Architecture

FluxIndex follows Clean Architecture principles with the following structure:

```
FluxIndex.Core          # Domain models and application interfaces
FluxIndex.SDK           # Client API and builder
FluxIndex.AI.*          # AI service adapters (OpenAI, etc.)
FluxIndex.Storage.*     # Storage implementations (SQLite, PostgreSQL)
FluxIndex.Cache.*       # Caching implementations (Redis, Memory)
FluxIndex.Extensions.*  # Document processing integrations
```

### Dependency Injection

All components are registered through dependency injection and can be replaced with custom implementations:

```csharp
services.AddScoped<IEmbeddingService, CustomEmbeddingService>();
services.AddScoped<IVectorStore, CustomVectorStore>();
services.AddScoped<ICacheService, CustomCacheService>();
```

## Configuration

### Using OpenAI

```csharp
var client = new FluxIndexClientBuilder()
    .UseOpenAI("your-api-key", "text-embedding-3-small")
    .UseSQLite("fluxindex.db")
    .UseMemoryCache()
    .Build();
```

### Using Azure OpenAI

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

### Using PostgreSQL

```csharp
var client = new FluxIndexClientBuilder()
    .UseOpenAI("your-api-key")
    .UsePostgreSQL("Host=localhost;Database=fluxindex;Username=user;Password=pass")
    .UseRedisCache("localhost:6379")
    .Build();
```

## Search Strategies

FluxIndex provides multiple search strategies:

- **Vector Search**: Semantic similarity using HNSW indexing
- **Keyword Search**: BM25 algorithm for exact term matching
- **Hybrid Search**: Combines vector and keyword results using RRF
- **Adaptive Search**: Automatically selects strategy based on query complexity

## Documentation

- **[Getting Started Guide](./docs/getting-started.md)** - Step-by-step setup instructions
- **[Tutorial](./docs/tutorial.md)** - Comprehensive usage examples
- **[Architecture Guide](./docs/architecture.md)** - Clean Architecture design principles
- **[RAG System Guide](./docs/FLUXINDEX_RAG_SYSTEM.md)** - Advanced RAG patterns
- **[Cheat Sheet](./docs/cheat-sheet.md)** - Quick reference

## Examples

See the [samples](./samples/) directory for complete working examples:

- **[RealWorldDemo](./samples/FluxIndex.RealWorldDemo/)** - OpenAI API integration with sqlite-vec
- **[FileFluxSample](./samples/FileFluxIndexSample/)** - Document processing integration
- **[WebFluxSample](./samples/WebFluxSample/)** - Web content extraction

## Requirements

- .NET 9.0 SDK or later
- SQLite or PostgreSQL database
- OpenAI API key (optional, for AI-powered embeddings)

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Contributing

Contributions are welcome! Please see the [development roadmap](./TASKS.md) for planned features and current status.
