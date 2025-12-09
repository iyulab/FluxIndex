# Advanced RAG Services Guide

FluxIndex provides advanced RAG (Retrieval-Augmented Generation) capabilities for enhanced search quality, intelligent result fusion, entity extraction, and community-based document organization.

## Overview

Advanced RAG features include:

| Feature | Description | Performance Gain |
|---------|-------------|------------------|
| **Dynamic Alpha Tuning (DAT)** | Query-adaptive fusion weights | ~6.6% improvement |
| **Listwise Reranking** | LLM-based global result ordering | ~8-12% improvement |
| **Entity Extraction** | Named entity recognition & linking | Semantic enrichment |
| **Community Detection** | Leiden-based document clustering | Hierarchical navigation |

---

## Quick Start

### Basic Setup

```csharp
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// Register core services
services.AddSingleton<IQueryComplexityAnalyzer, QueryComplexityAnalyzer>();
services.AddSingleton<IDynamicFusionService, DynamicFusionService>();
services.AddSingleton<IListwiseReranker, ListwiseReranker>();
services.AddSingleton<IAdvancedEntityExtractionService, EntityExtractionService>();
services.AddSingleton<ILeidenCommunityService, LeidenCommunityService>();
```

### With Dependency Injection (Stack)

```csharp
// In Startup.cs or Program.cs
services.AddAdvancedSearchServices(configuration);
```

---

## Dynamic Alpha Tuning (DAT)

DAT automatically adjusts the balance between keyword (BM25) and vector search based on query characteristics.

### How It Works

```
Simple Query: "machine learning"
→ Keyword-biased: α = 0.35 (35% keyword, 65% vector)

Complex Query: "How does transformer attention mechanism work in NLP?"
→ Semantic-biased: α = 0.65 (65% keyword, 35% vector)
```

### Usage

```csharp
// Get dynamic weights for a query
var fusionService = serviceProvider.GetRequiredService<IDynamicFusionService>();

var config = await fusionService.CalculateDynamicWeightsAsync(
    query: "What are the benefits of microservices architecture?",
    cancellationToken: ct);

Console.WriteLine($"Keyword Weight: {config.KeywordWeight:P1}");
Console.WriteLine($"Vector Weight: {config.VectorWeight:P1}");
Console.WriteLine($"Fusion Method: {config.Method}");
Console.WriteLine($"Reason: {config.TuningReason}");
```

### Configuration Options

```csharp
public class DynamicFusionOptions
{
    // Base alpha when no adjustments needed
    public float BaseAlpha { get; set; } = 0.5f;

    // Maximum alpha adjustment range
    public float MaxAdjustment { get; set; } = 0.2f;

    // Query length threshold for adjustment
    public int ShortQueryThreshold { get; set; } = 3;
    public int LongQueryThreshold { get; set; } = 10;
}
```

### Query Analysis Factors

| Factor | Effect on Alpha |
|--------|-----------------|
| Short query (1-3 words) | ↑ Keyword weight |
| Long query (10+ words) | ↑ Vector weight |
| Technical terms present | ↑ Keyword weight |
| Question format | ↑ Vector weight |
| Named entities detected | ↑ Keyword weight |

---

## Listwise Reranking

Unlike pointwise rerankers that score documents individually, listwise reranking considers the global context of all results simultaneously.

### Available Methods

| Method | Description | Best For |
|--------|-------------|----------|
| `AttentionBased` | Cross-attention between query and results | General purpose |
| `SlidingWindow` | Processes results in overlapping windows | Large result sets |
| `Tournament` | Pairwise comparison tournament | Precision-critical |
| `DirectLlm` | Direct LLM ranking prompt | Highest quality |
| `Hybrid` | Combines multiple methods | Best accuracy |

### Usage

```csharp
var reranker = serviceProvider.GetRequiredService<IListwiseReranker>();

var options = new ListwiseRerankingOptions
{
    Method = ListwiseMethod.AttentionBased,
    TopK = 10,
    WindowSize = 5,  // For SlidingWindow
    IncludeConfidence = true
};

var rerankedResults = await reranker.RerankAsync(
    query: "machine learning frameworks",
    candidates: searchResults,
    options: options,
    cancellationToken: ct);

foreach (var result in rerankedResults)
{
    Console.WriteLine($"[{result.Score:F3}] {result.Content}");
    Console.WriteLine($"  Original Rank: {result.OriginalRank}");
    Console.WriteLine($"  New Rank: {result.NewRank}");
    Console.WriteLine($"  Confidence: {result.Confidence:P1}");
}
```

### Performance Considerations

```csharp
// For high-throughput scenarios
var options = new ListwiseRerankingOptions
{
    Method = ListwiseMethod.SlidingWindow,
    WindowSize = 10,
    TopK = 20
};

// For maximum quality
var options = new ListwiseRerankingOptions
{
    Method = ListwiseMethod.Hybrid,
    TopK = 10,
    IncludeConfidence = true
};
```

---

## Entity Extraction

Extract named entities (persons, organizations, locations, technologies, etc.) from documents with optional LLM enhancement.

### Supported Entity Types

| Type | Example |
|------|---------|
| `Person` | "John Smith", "Dr. Jane Doe" |
| `Organization` | "Microsoft", "OpenAI Inc." |
| `Location` | "San Francisco", "United States" |
| `Technology` | "Python", "Kubernetes", "PostgreSQL" |
| `DateTime` | "January 2025", "2024-12-01" |
| `Money` | "$1,000", "50 euros" |
| `Email` | "user@example.com" |
| `Url` | "https://example.com" |
| `PhoneNumber` | "+1-555-123-4567" |
| `Percentage` | "25%", "50 percent" |

### Basic Usage

```csharp
var extractor = serviceProvider.GetRequiredService<IAdvancedEntityExtractionService>();

var entities = await extractor.ExtractEntitiesAsync(
    content: "Microsoft CEO Satya Nadella announced new AI features in Seattle.",
    options: new EntityExtractionOptions
    {
        MinConfidence = 0.7,
        MaxEntities = 50,
        IncludeContext = true,
        UseLlm = false  // Pattern-only, no LLM
    },
    cancellationToken: ct);

foreach (var entity in entities)
{
    Console.WriteLine($"{entity.Type}: {entity.Text} ({entity.Confidence:P0})");
    Console.WriteLine($"  Normalized: {entity.NormalizedText}");
    Console.WriteLine($"  Occurrences: {entity.OccurrenceCount}");
}
```

### Entity Graph Extraction

Extract entities with their relationships:

```csharp
var graph = await extractor.ExtractEntityGraphAsync(
    content: documentContent,
    options: new EntityExtractionOptions
    {
        ExtractRelations = true,
        UseLlm = true  // LLM for relation extraction
    },
    cancellationToken: ct);

Console.WriteLine($"Entities: {graph.Entities.Count}");
Console.WriteLine($"Relations: {graph.Relations.Count}");

foreach (var relation in graph.Relations)
{
    var source = graph.Entities.First(e => e.Id == relation.SourceEntityId);
    var target = graph.Entities.First(e => e.Id == relation.TargetEntityId);
    Console.WriteLine($"{source.Text} --[{relation.Type}]--> {target.Text}");
}
```

### Entity Linking

Link entities across multiple documents:

```csharp
var graphs = new List<EntityGraph>();
foreach (var doc in documents)
{
    var graph = await extractor.ExtractEntityGraphAsync(doc.Content);
    graphs.Add(graph);
}

var linkedGraph = await extractor.LinkEntitiesAsync(
    graphs,
    options: new EntityLinkingOptions
    {
        RequireSameType = true
    },
    cancellationToken: ct);

Console.WriteLine($"Original entities: {graphs.Sum(g => g.Entities.Count)}");
Console.WriteLine($"Linked entities: {linkedGraph.Entities.Count}");
Console.WriteLine($"Merge count: {linkedGraph.Stats.MergeCount}");
```

---

## Community Detection (Leiden Algorithm)

Organize documents into hierarchical communities based on semantic similarity.

### What is Leiden?

The Leiden algorithm is a graph-based community detection method that:
- Groups semantically similar chunks into communities
- Creates hierarchical structure (multiple levels)
- Optimizes for modularity (intra-community cohesion)

### Basic Usage

```csharp
var communityService = serviceProvider.GetRequiredService<ILeidenCommunityService>();

// Prepare chunks with embeddings
var chunks = documents.SelectMany(d => d.Chunks)
    .Select(c => new LeidenChunk
    {
        Id = c.Id.ToString(),
        Content = c.Content,
        Embedding = c.Embedding,
        DocumentId = c.DocumentId.ToString()
    })
    .ToList();

// Detect communities
var hierarchy = await communityService.DetectHierarchicalCommunitiesAsync(
    chunks,
    options: new LeidenOptions
    {
        Resolution = 1.0,           // Higher = more communities
        MaxHierarchyLevels = 3,     // Hierarchy depth
        MinCommunitySize = 2,       // Minimum chunks per community
        SimilarityThreshold = 0.5,  // Edge creation threshold
        MaxNeighbors = 15,          // k-NN graph parameter
        GenerateSummariesOnDetection = true  // LLM summaries
    },
    cancellationToken: ct);

Console.WriteLine($"Levels: {hierarchy.LevelCount}");
Console.WriteLine($"Total chunks: {hierarchy.TotalChunks}");
Console.WriteLine($"Final modularity: {hierarchy.Statistics.FinalModularity:F4}");
```

### Navigating the Hierarchy

```csharp
// Get communities at a specific level
foreach (var level in hierarchy.Levels)
{
    Console.WriteLine($"\n=== Level {level.LevelIndex} ===");
    Console.WriteLine($"Communities: {level.CommunityCount}");
    Console.WriteLine($"Modularity: {level.Modularity:F4}");

    foreach (var community in level.Communities)
    {
        Console.WriteLine($"\n  Community {community.Index}:");
        Console.WriteLine($"    Size: {community.Size} chunks");
        Console.WriteLine($"    Cohesion: {community.Cohesion:F3}");
        Console.WriteLine($"    Keywords: {string.Join(", ", community.Keywords.Take(5))}");
    }
}
```

### Community-Based Search

```csharp
// Find relevant communities for a query
var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(query);

var matches = await communityService.FindRelevantCommunitiesAsync(
    queryEmbedding: queryEmbedding,
    hierarchy: hierarchy,
    level: 0,      // Finest level (most specific)
    topK: 5,
    cancellationToken: ct);

foreach (var match in matches)
{
    Console.WriteLine($"Community {match.Community.Index}:");
    Console.WriteLine($"  Similarity: {match.Similarity:F3}");
    Console.WriteLine($"  Keywords: {string.Join(", ", match.Community.Keywords.Take(5))}");
    Console.WriteLine($"  Chunks: {match.Community.Size}");
}

// Search within relevant communities only
var relevantChunkIds = matches
    .SelectMany(m => m.Community.ChunkIds)
    .ToHashSet();

var filteredResults = searchResults
    .Where(r => relevantChunkIds.Contains(r.ChunkId.ToString()))
    .ToList();
```

### Community Summaries

```csharp
// Generate summaries for a level (requires ITextCompletionService)
var summaries = await communityService.GenerateSummariesAsync(
    hierarchy,
    level: 0,
    cancellationToken: ct);

foreach (var summary in summaries)
{
    Console.WriteLine($"Community {summary.CommunityId}:");
    Console.WriteLine($"  {summary.Summary}");
    Console.WriteLine($"  Confidence: {summary.Confidence:P0}");
}
```

---

## Integration with Stack API

The Stack API provides a unified endpoint for advanced search:

### API Endpoint

```
POST /api/v1/search/advanced
```

### Request

```json
{
  "query": "How do microservices communicate?",
  "collectionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "topK": 10,
  "enableDynamicFusion": true,
  "enableListwiseReranking": true,
  "listWiseMethod": "AttentionBased",
  "enableEntityExtraction": true,
  "enableCommunitySearch": false,
  "includeQueryAnalysis": true,
  "includeFusionDetails": true
}
```

### Response

```json
{
  "success": true,
  "data": {
    "query": "How do microservices communicate?",
    "results": [
      {
        "chunkId": "...",
        "documentId": "...",
        "documentTitle": "Microservices Architecture",
        "content": "...",
        "score": 0.92,
        "vectorScore": 0.88,
        "keywordScore": 0.75,
        "fusionScore": 0.85,
        "rerankScore": 0.92,
        "listwiseDetails": {
          "originalRank": 3,
          "newRank": 1,
          "listwiseScore": 0.92,
          "confidence": 0.87
        }
      }
    ],
    "queryAnalysis": {
      "queryType": "NaturalQuestion",
      "complexityLevel": "Moderate",
      "containsTechnicalTerms": true,
      "tokenCount": 5
    },
    "fusionDetails": {
      "fusionMethod": "RRF",
      "keywordWeight": 0.35,
      "vectorWeight": 0.65,
      "wasDynamicallyTuned": true,
      "tuningReason": "Question format detected, increased semantic weight"
    },
    "entities": [
      {
        "name": "microservices",
        "type": "Technology",
        "confidence": 0.95,
        "mentionCount": 3
      }
    ],
    "totalResults": 10,
    "executionTimeMs": 145
  }
}
```

### Additional Endpoints

```bash
# Analyze query characteristics
GET /api/v1/search/advanced/analyze?query=your+query

# Extract entities from a collection
GET /api/v1/search/advanced/entities/{collectionId}?maxEntities=100

# Build community hierarchy
POST /api/v1/search/advanced/communities/{collectionId}/build?maxLevels=3

# Get existing communities
GET /api/v1/search/advanced/communities/{collectionId}?level=0
```

---

## Performance Tuning

### Batch Processing

```csharp
// Parallel entity extraction
var tasks = documents.Select(async doc =>
{
    return await extractor.ExtractEntityGraphAsync(doc.Content);
});
var graphs = await Task.WhenAll(tasks);
```

### Caching Recommendations

| Service | Cache Strategy |
|---------|---------------|
| Query Analysis | In-memory, short TTL (5 min) |
| Entity Extraction | Persistent, document-based |
| Community Hierarchy | Persistent, rebuild on document changes |
| Dynamic Fusion Config | No cache (fast computation) |

### Resource Usage

| Service | CPU | Memory | LLM Calls |
|---------|-----|--------|-----------|
| Dynamic Fusion | Low | Low | 0 |
| Listwise (Attention) | Medium | Medium | 0 |
| Listwise (DirectLLM) | Low | Low | 1 per batch |
| Entity Extraction | Medium | Medium | 0-1 per doc |
| Community Detection | High | High | 0-N per community |

---

## Best Practices

### 1. Start Simple

```csharp
// Start with DAT only (lowest overhead)
services.AddSingleton<IDynamicFusionService, DynamicFusionService>();
```

### 2. Add Reranking for Quality

```csharp
// Add listwise reranking for improved precision
services.AddSingleton<IListwiseReranker, ListwiseReranker>();

// Use AttentionBased for balance, DirectLLM for maximum quality
```

### 3. Entity Extraction for Semantic Enrichment

```csharp
// Extract entities during indexing
var entities = await extractor.ExtractEntitiesAsync(doc.Content);
doc.Metadata["entities"] = entities.Select(e => e.NormalizedText).ToList();

// Use for faceted search / filtering
```

### 4. Community Detection for Large Collections

```csharp
// Build communities for collections with 100+ documents
if (collection.DocumentCount >= 100)
{
    var hierarchy = await communityService.DetectHierarchicalCommunitiesAsync(chunks);
    // Store hierarchy for navigation
}
```

### 5. Monitor and Tune

```csharp
// Log fusion decisions
_logger.LogInformation(
    "Query: {Query}, Alpha: {Alpha}, Reason: {Reason}",
    query, config.KeywordWeight, config.TuningReason);

// Track reranking impact
var deltaSum = results.Sum(r => Math.Abs(r.OriginalRank - r.NewRank));
_logger.LogMetric("rerank_delta_avg", deltaSum / (double)results.Count);
```

---

## Troubleshooting

### Low Entity Extraction Quality

```csharp
// Enable LLM for better accuracy
var options = new EntityExtractionOptions
{
    UseLlm = true,
    MinConfidence = 0.6  // Lower threshold
};
```

### Community Detection Too Slow

```csharp
// Reduce complexity
var options = new LeidenOptions
{
    MaxNeighbors = 10,     // Reduce from 15
    MaxIterations = 50,    // Reduce from 100
    MaxHierarchyLevels = 2 // Reduce from 3
};
```

### Fusion Not Improving Results

```csharp
// Check query analysis
var analyzer = serviceProvider.GetRequiredService<IQueryComplexityAnalyzer>();
var analysis = await analyzer.AnalyzeAsync(query);
Console.WriteLine($"Type: {analysis.Type}, Complexity: {analysis.Complexity}");

// Verify base search quality first
// DAT works best when both keyword and vector search have reasonable quality
```

---

## See Also

- [GUIDE.md](./GUIDE.md) - Basic FluxIndex usage
- [REFERENCE.md](./REFERENCE.md) - API reference
- [RAG_ENHANCEMENT_ROADMAP.md](./archive/RAG_ENHANCEMENT_ROADMAP_COMPLETED.md) - Implementation details
