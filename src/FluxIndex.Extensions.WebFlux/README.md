# FluxIndex.Extensions.WebFlux

WebFlux integration for FluxIndex - Web content processing and indexing for RAG systems.

## Overview

FluxIndex.Extensions.WebFlux provides seamless integration between [WebFlux](https://github.com/iyulab/WebFlux) and FluxIndex, enabling intelligent web content crawling, processing, and indexing for RAG (Retrieval-Augmented Generation) systems.

## Features

- **🕷️ Intelligent Web Crawling**: WebFlux-powered crawling with respect for robots.txt and sitemap.xml
- **🧠 AI-Driven Chunking**: 7 chunking strategies including Auto strategy with AI optimization
- **📄 15+ Content Formats**: HTML, Markdown, JSON, XML, RSS, PDF support
- **⚡ High Performance**: Parallel processing with memory optimization
- **🔄 Progress Tracking**: Real-time indexing progress with detailed reporting
- **🎯 Clean Architecture**: Follows FluxIndex's provider pattern

## Installation

```bash
dotnet add package FluxIndex.Extensions.WebFlux
```

## Quick Start

### Basic Usage

```csharp
using FluxIndex.SDK;
using FluxIndex.Extensions.WebFlux;

// Create FluxIndex client with WebFlux integration
var client = new FluxIndexClientBuilder()
    .UsePostgreSQL("Host=localhost;Database=fluxindex;Username=user;Password=pass")
    .UseOpenAI("your-openai-api-key")
    .UseWebFluxWithOpenAI()  // Add WebFlux with OpenAI services
    .WithChunking("Auto", 512, 64)
    .Build();

// Index a website
string documentId = await client.IndexWebsiteAsync(
    "https://docs.example.com",
    new CrawlOptions
    {
        MaxDepth = 3,
        MaxPages = 100,
        RespectRobotsTxt = true
    });

Console.WriteLine($"Indexed website: {documentId}\");
```

### Advanced Configuration

```csharp
var client = new FluxIndexClientBuilder()
    .UsePostgreSQL("connection-string")
    .UseAzureOpenAI("endpoint", "api-key", "deployment")
    .UseWebFlux()  // Add WebFlux (requires AI services configured separately)
    .WithWebCrawlingOptions(options =>
    {
        options.MaxDepth = 5;
        options.MaxPages = 500;
        options.DelayBetweenRequests = TimeSpan.FromMilliseconds(200);
        options.RespectRobotsTxt = true;
        options.UserAgent = "MyRAGBot/1.0";
        options.MaxConcurrentRequests = 10;
    })
    .WithWebChunkingOptions(options =>
    {
        options.Strategy = "Auto";  // AI-driven optimization
        options.MaxChunkSize = 1024;
        options.OverlapSize = 128;
    })
    .Build();
```

### Progress Tracking

```csharp
await foreach (var progress in client.IndexWebsiteWithProgressAsync("https://docs.example.com"))
{
    Console.WriteLine($"Status: {progress.Status}");
    Console.WriteLine($"Message: {progress.Message}");

    if (progress.Status == IndexingStatus.Completed)
    {
        Console.WriteLine($"Completed in {progress.Duration?.TotalSeconds:F1}s");
        Console.WriteLine($"Document ID: {progress.DocumentId}");
        Console.WriteLine($"Chunks processed: {progress.ChunksProcessed}");
    }
}
```

### Bulk Website Indexing

```csharp
var urls = new[]
{
    "https://docs.example.com",
    "https://help.example.com",
    "https://api.example.com/docs"
};

var documentIds = await client.IndexWebsitesAsync(
    urls,
    maxConcurrency: 3);

Console.WriteLine($"Indexed {documentIds.Count()} websites");
```

## Advanced Usage

### Custom Content Extractor

```csharp
public class CustomApiExtractor : IContentExtractor
{
    public string ExtractorType => "CustomAPI";
    public IEnumerable<string> SupportedContentTypes => ["application/json"];

    public bool CanExtract(string contentType, string url) =>
        url.Contains("/api/") && contentType.Contains("json");

    public async Task<RawWebContent> ExtractAsync(
        string url,
        HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        // Custom parsing logic here
        return new RawWebContent { /* ... */ };
    }
}

// Register custom extractor
var client = new FluxIndexClientBuilder()
    .UsePostgreSQL("connection-string")
    .UseOpenAI("api-key")
    .UseWebFlux()
    .AddWebContentExtractor<CustomApiExtractor>()
    .Build();
```

### Direct Document Processing

```csharp
// Get WebFlux document processor
var processor = client.GetWebDocumentProcessor();

// Process single URL
var document = await processor.ProcessUrlAsync(
    "https://example.com/page",
    new CrawlOptions { MaxDepth = 1 },
    new ChunkingOptions { Strategy = "Semantic" });

// Process multiple URLs
var documents = await processor.ProcessUrlsAsync(
    urls,
    maxConcurrency: 5);
```

### Testing with Mock AI Services

```csharp
var client = new FluxIndexClientBuilder()
    .UseSQLiteInMemory()
    .UseWebFluxWithMockAI()  // Use mock AI services for testing
    .Build();

// Test indexing without real AI API calls
var documentId = await client.IndexWebsiteAsync("https://example.com");
```

## WebFlux Features Integration

### Chunking Strategies

All WebFlux chunking strategies are supported:

| Strategy | Description | Best For |
|----------|-------------|----------|
| **Auto** 🤖 | AI-driven optimization | All content types |
| **Smart** 🧠 | HTML structure-aware | API docs, structured HTML |
| **Semantic** 🔍 | Semantic coherence | Articles, blogs |
| **Intelligent** 💡 | LLM-enhanced | Knowledge bases |
| **MemoryOptimized** ⚡ | Low memory usage | Large-scale crawling |
| **Paragraph** 📄 | Paragraph boundaries | Markdown, wikis |
| **FixedSize** 📏 | Fixed chunk sizes | Testing, uniform processing |

### Web Intelligence Engine

Supports WebFlux's 15 metadata standards:

- **AI Standards**: llms.txt, ai.txt, robots.txt
- **Structure**: sitemap.xml, README.md, package.json
- **Security**: security.txt, .well-known
- **PWA**: manifest.json
- **And more...**

## Architecture

FluxIndex.Extensions.WebFlux follows FluxIndex's Clean Architecture:

```
┌─────────────────────────────────────┐
│         FluxIndex.SDK               │
│    (FluxIndexClient + Builder)      │
└─────────────────┬───────────────────┘
                  │
┌─────────────────▼───────────────────┐
│   FluxIndex.Extensions.WebFlux      │
│  • WebFluxDocumentProcessor         │
│  • WebFluxIndexer                   │
│  • ServiceExtensions                │
└─────────────────┬───────────────────┘
                  │
┌─────────────────▼───────────────────┐
│            WebFlux                  │
│   (Web Intelligence Engine)        │
└─────────────────────────────────────┘
```

## Dependencies

- **FluxIndex**: Core RAG infrastructure
- **WebFlux**: Web content processing engine
- **Microsoft.Extensions.DependencyInjection**: Service registration
- **Microsoft.Extensions.Logging**: Logging support

## License

This project is licensed under the MIT License - see the [LICENSE](../../LICENSE) file for details.