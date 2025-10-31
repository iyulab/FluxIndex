# AI Metadata Extraction Sample

This sample demonstrates how to use FluxIndex's AI-powered metadata extraction feature.

## Basic Usage with OpenAI

```csharp
using FluxIndex.SDK;
using FluxIndex.Core.Models;

// Build FluxIndex context with AI metadata extraction
var context = new FluxIndexContextBuilder()
    .UseSQLite("myindex.db")
    .UseOpenAI(apiKey: "your-openai-api-key")
    .WithOpenAIMetadataExtractor(
        apiKey: "your-openai-api-key",
        schema: MetadataSchema.General,
        strategy: MetadataExtractionStrategy.Smart,
        minConfidence: 0.6f)
    .Build();

// Index a document - metadata will be automatically extracted
var documentId = await context.Indexer.IndexDocumentAsync(
    content: @"
        Artificial Intelligence in Healthcare

        Machine learning algorithms are revolutionizing medical diagnosis
        and treatment planning. Recent studies show that AI systems can
        detect certain diseases with accuracy rivaling expert physicians.

        Key applications include:
        - Medical imaging analysis
        - Drug discovery and development
        - Personalized treatment recommendations
        - Clinical decision support systems
    ",
    documentId: "healthcare-ai-doc",
    metadata: new Dictionary<string, object>
    {
        { "author", "Dr. Jane Smith" },
        { "publishedDate", "2024-01-15" }
    });

Console.WriteLine($"Document indexed: {documentId}");

// Retrieve the AI-extracted metadata
var extractedMetadata = await context.Indexer.GetExtractedMetadataAsync(documentId);

if (extractedMetadata != null)
{
    Console.WriteLine($"Topics: {string.Join(", ", extractedMetadata.Topics)}");
    Console.WriteLine($"Keywords: {string.Join(", ", extractedMetadata.Keywords)}");
    Console.WriteLine($"Description: {extractedMetadata.Description}");
    Console.WriteLine($"Document Type: {extractedMetadata.DocumentType}");
    Console.WriteLine($"Language: {extractedMetadata.Language}");
    Console.WriteLine($"Overall Confidence: {extractedMetadata.OverallConfidence:F2}");
    Console.WriteLine($"Extraction Method: {extractedMetadata.ExtractionMethod}");
}
```

## Using Azure OpenAI

```csharp
var context = new FluxIndexContextBuilder()
    .UseSQLite("myindex.db")
    .UseAzureOpenAI(
        endpoint: "https://your-resource.openai.azure.com",
        apiKey: "your-azure-api-key",
        deploymentName: "gpt-4")
    .WithOpenAIMetadataExtractor(
        apiKey: "your-azure-api-key",
        endpoint: "https://your-resource.openai.azure.com",
        schema: MetadataSchema.TechnicalDoc,
        strategy: MetadataExtractionStrategy.Deep)
    .Build();

var documentId = await context.Indexer.IndexDocumentAsync(
    content: technicalDocumentation,
    documentId: "tech-doc-001");
```

## Schema-Specific Extraction

### Product Manual Schema

```csharp
var context = new FluxIndexContextBuilder()
    .UseSQLite("products.db")
    .UseOpenAI("your-api-key")
    .WithOpenAIMetadataExtractor(
        apiKey: "your-api-key",
        schema: MetadataSchema.ProductManual)
    .Build();

var documentId = await context.Indexer.IndexDocumentAsync(
    content: productManualContent,
    documentId: "manual-xyz");

var metadata = await context.Indexer.GetExtractedMetadataAsync(documentId);

// Access product-specific fields
if (metadata?.SchemaSpecificData != null)
{
    var productName = metadata.SchemaSpecificData["productName"]?.ToString();
    var modelNumber = metadata.SchemaSpecificData["modelNumber"]?.ToString();
    var manufacturer = metadata.SchemaSpecificData["manufacturer"]?.ToString();

    Console.WriteLine($"Product: {productName} ({modelNumber})");
    Console.WriteLine($"Manufacturer: {manufacturer}");
}
```

### Technical Documentation Schema

```csharp
var context = new FluxIndexContextBuilder()
    .UseSQLite("techdocs.db")
    .UseOpenAI("your-api-key")
    .WithOpenAIMetadataExtractor(
        apiKey: "your-api-key",
        schema: MetadataSchema.TechnicalDoc)
    .Build();

var documentId = await context.Indexer.IndexDocumentAsync(
    content: apiDocumentation,
    documentId: "api-docs-v2");

var metadata = await context.Indexer.GetExtractedMetadataAsync(documentId);

// Access tech doc specific fields
if (metadata?.SchemaSpecificData != null)
{
    var apiVersion = metadata.SchemaSpecificData["apiVersion"]?.ToString();
    var framework = metadata.SchemaSpecificData["framework"]?.ToString();
    var codeExamples = metadata.SchemaSpecificData["codeExamples"] as string[];

    Console.WriteLine($"API Version: {apiVersion}");
    Console.WriteLine($"Framework: {framework}");
}
```

### Article/Blog Post Schema

```csharp
var context = new FluxIndexContextBuilder()
    .UseSQLite("articles.db")
    .UseOpenAI("your-api-key")
    .WithOpenAIMetadataExtractor(
        apiKey: "your-api-key",
        schema: MetadataSchema.Article)
    .Build();

var documentId = await context.Indexer.IndexDocumentAsync(
    content: blogPostContent,
    documentId: "blog-post-123");

var metadata = await context.Indexer.GetExtractedMetadataAsync(documentId);

// Access article-specific fields
if (metadata?.SchemaSpecificData != null)
{
    var readingTime = metadata.SchemaSpecificData["estimatedReadingTime"]?.ToString();
    var targetAudience = metadata.SchemaSpecificData["targetAudience"]?.ToString();
    var tone = metadata.SchemaSpecificData["tone"]?.ToString();

    Console.WriteLine($"Reading Time: {readingTime}");
    Console.WriteLine($"Target Audience: {targetAudience}");
    Console.WriteLine($"Tone: {tone}");
}
```

## Custom Prompt Extraction

```csharp
var customPrompt = @"
Extract the following metadata from this software architecture document:

REQUIRED FIELDS:
- systemName: Name of the software system
- architectureStyle: Architecture pattern (microservices, monolith, etc.)
- components: List of major system components
- technologies: Technology stack used
- scalability: Scalability characteristics
- securityLevel: Security classification (public, internal, confidential)

Return as JSON with confidence scores.
";

var context = new FluxIndexContextBuilder()
    .UseSQLite("architecture.db")
    .UseOpenAI("your-api-key")
    .WithCustomMetadataExtractor(
        apiKey: "your-api-key",
        customPrompt: customPrompt,
        strategy: MetadataExtractionStrategy.Deep)
    .Build();

var documentId = await context.Indexer.IndexDocumentAsync(
    content: architectureDocument,
    documentId: "arch-doc-001");
```

## Extraction Strategies

### Fast Strategy (2K characters)
Best for: Quick metadata extraction, cost-sensitive scenarios
```csharp
.WithOpenAIMetadataExtractor(
    apiKey: "your-api-key",
    strategy: MetadataExtractionStrategy.Fast)
```

### Smart Strategy (4K characters) - Default
Best for: Balanced cost and quality, most use cases
```csharp
.WithOpenAIMetadataExtractor(
    apiKey: "your-api-key",
    strategy: MetadataExtractionStrategy.Smart)
```

### Deep Strategy (8K characters)
Best for: Complex documents, maximum quality
```csharp
.WithOpenAIMetadataExtractor(
    apiKey: "your-api-key",
    strategy: MetadataExtractionStrategy.Deep)
```

## User Metadata Correction

```csharp
// Get current AI-extracted metadata
var metadata = await context.Indexer.GetExtractedMetadataAsync(documentId);

if (metadata != null)
{
    // User reviews and corrects the metadata
    metadata.Topics = new[] { "Healthcare", "AI", "Medical Diagnosis" }; // Corrected
    metadata.Keywords = new[] { "machine learning", "medical imaging", "diagnosis", "AI" };
    metadata.Description = "Overview of AI applications in healthcare diagnosis"; // More accurate

    // Update field confidence for corrected fields
    metadata.FieldConfidence["topics"] = 1.0f; // User corrections are 100% confident
    metadata.FieldConfidence["keywords"] = 1.0f;
    metadata.FieldConfidence["description"] = 1.0f;

    // Save corrected metadata
    await context.Indexer.CorrectExtractedMetadataAsync(documentId, metadata);

    Console.WriteLine("Metadata corrections saved successfully");
}
```

## Alternative: Direct Metadata Update

```csharp
// Update specific metadata fields directly
await context.Indexer.UpdateDocumentMetadataAsync(
    documentId: documentId,
    metadata: new Dictionary<string, object>
    {
        { "correctedTopics", new[] { "AI", "Healthcare", "Diagnosis" } },
        { "reviewedBy", "Dr. John Doe" },
        { "reviewDate", DateTime.UtcNow }
    });
```

## Confidence Filtering

```csharp
var metadata = await context.Indexer.GetExtractedMetadataAsync(documentId);

if (metadata != null)
{
    // Only use high-confidence extractions
    if (metadata.OverallConfidence >= 0.8f)
    {
        Console.WriteLine("High confidence metadata - safe to use");
    }
    else if (metadata.OverallConfidence >= 0.6f)
    {
        Console.WriteLine("Medium confidence - review recommended");
    }
    else
    {
        Console.WriteLine("Low confidence - manual review required");
    }

    // Check individual field confidence
    foreach (var (field, confidence) in metadata.FieldConfidence)
    {
        if (confidence < 0.6f)
        {
            Console.WriteLine($"Low confidence field: {field} ({confidence:F2})");
        }
    }
}
```

## Batch Processing with Metadata Extraction

```csharp
var documents = new[]
{
    ("doc1", "Content of document 1..."),
    ("doc2", "Content of document 2..."),
    ("doc3", "Content of document 3...")
};

// Index multiple documents with automatic metadata extraction
var tasks = documents.Select(async (doc) =>
{
    var (id, content) = doc;
    return await context.Indexer.IndexDocumentAsync(content, id);
});

var documentIds = await Task.WhenAll(tasks);

Console.WriteLine($"Indexed {documentIds.Length} documents with AI metadata extraction");

// Retrieve and display extracted metadata for all documents
foreach (var docId in documentIds)
{
    var metadata = await context.Indexer.GetExtractedMetadataAsync(docId);
    if (metadata != null)
    {
        Console.WriteLine($"\n{docId}:");
        Console.WriteLine($"  Topics: {string.Join(", ", metadata.Topics)}");
        Console.WriteLine($"  Confidence: {metadata.OverallConfidence:F2}");
    }
}
```

## Error Handling

```csharp
try
{
    var documentId = await context.Indexer.IndexDocumentAsync(
        content: documentContent,
        documentId: "doc-001");

    var metadata = await context.Indexer.GetExtractedMetadataAsync(documentId);

    if (metadata == null)
    {
        Console.WriteLine("No metadata extracted - using fallback");
    }
    else if (metadata.ExtractionMethod == "RuleBased")
    {
        Console.WriteLine("AI extraction failed, used rule-based fallback");
    }
    else if (metadata.Source == MetadataSource.Merged)
    {
        Console.WriteLine("Hybrid extraction: AI + RuleBased");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Indexing failed: {ex.Message}");
    // Indexing continues even if metadata extraction fails
}
```

## Cost Optimization

```csharp
// Use Fast strategy for large document batches
var context = new FluxIndexContextBuilder()
    .UseSQLite("large-corpus.db")
    .UseOpenAI("your-api-key")
    .WithOpenAIMetadataExtractor(
        apiKey: "your-api-key",
        schema: MetadataSchema.General,
        strategy: MetadataExtractionStrategy.Fast, // Cheaper, faster
        minConfidence: 0.5f) // Lower threshold for cost savings
    .Build();

// Metadata extraction is cached automatically
// Repeated indexing of same content won't incur additional API costs
```

## Integration with Search

```csharp
// Index with metadata extraction
var documentId = await context.Indexer.IndexDocumentAsync(
    content: articleContent,
    documentId: "article-123");

// Search using extracted metadata (topics, keywords)
var searchResults = await context.Retriever.SearchAsync(
    query: "machine learning healthcare",
    maxResults: 10);

foreach (var result in searchResults.Results)
{
    var metadata = await context.Indexer.GetExtractedMetadataAsync(result.DocumentId);
    if (metadata != null)
    {
        Console.WriteLine($"Title: {result.DocumentId}");
        Console.WriteLine($"Topics: {string.Join(", ", metadata.Topics)}");
        Console.WriteLine($"Relevance: {result.Score:F3}");
        Console.WriteLine();
    }
}
```

## Summary

The AI metadata extraction feature provides:
- **Automatic extraction** during document indexing
- **Multiple schemas** for different document types
- **Configurable strategies** to balance cost and quality
- **Confidence scoring** for quality assessment
- **User correction APIs** for improving accuracy
- **Caching** to minimize API costs
- **Graceful fallback** to rule-based extraction when AI fails

Configure it once during FluxIndex context building, and metadata will be automatically extracted for all indexed documents.
