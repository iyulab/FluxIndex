namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Orchestrates AI-powered document enrichment for polyglot persistence.
/// Coordinates multi-representation embedding generation, entity extraction,
/// contextual enrichment, and graph building during document indexing.
/// </summary>
public interface IDocumentEnrichmentPipeline
{
    /// <summary>
    /// Enriches a single chunk with all configured enrichment strategies.
    /// </summary>
    Task<EnrichedChunk> EnrichChunkAsync(
        ChunkEnrichmentInput input,
        EnrichmentOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Enriches multiple chunks in batch, optimizing for shared context.
    /// </summary>
    Task<IReadOnlyList<EnrichedChunk>> EnrichChunksBatchAsync(
        IEnumerable<ChunkEnrichmentInput> inputs,
        EnrichmentOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Enriches an entire document, including cross-chunk analysis.
    /// </summary>
    Task<EnrichedDocument> EnrichDocumentAsync(
        DocumentEnrichmentInput input,
        EnrichmentOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Generates multi-representation embeddings for content.
    /// </summary>
    Task<MultiRepresentationEmbeddings> GenerateEmbeddingsAsync(
        string content,
        EmbeddingGenerationOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Extracts entities and relationships from content.
    /// </summary>
    Task<EntityExtractionResult> ExtractEntitiesAsync(
        string content,
        EnrichmentEntityOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Generates contextual summary for a chunk (Anthropic's contextual retrieval).
    /// </summary>
    Task<ContextualSummary> GenerateContextualSummaryAsync(
        string chunkContent,
        string? documentContext = null,
        string? precedingChunks = null,
        string? followingChunks = null,
        CancellationToken ct = default);

    /// <summary>
    /// Builds graph data (entities + relationships) for storage.
    /// </summary>
    Task<GraphBuildResult> BuildGraphDataAsync(
        IEnumerable<EnrichedChunk> chunks,
        GraphBuildOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the current pipeline configuration.
    /// </summary>
    EnrichmentPipelineConfig GetConfiguration();
}

#region Input Types

/// <summary>
/// Input for chunk enrichment.
/// </summary>
public record ChunkEnrichmentInput
{
    /// <summary>Chunk ID</summary>
    public required string ChunkId { get; init; }

    /// <summary>Chunk content</summary>
    public required string Content { get; init; }

    /// <summary>Parent document ID</summary>
    public required string DocumentId { get; init; }

    /// <summary>Chunk index within document</summary>
    public int ChunkIndex { get; init; }

    /// <summary>Total chunks in document</summary>
    public int TotalChunks { get; init; }

    /// <summary>Document title for context</summary>
    public string? DocumentTitle { get; init; }

    /// <summary>Document summary for context</summary>
    public string? DocumentSummary { get; init; }

    /// <summary>Preceding chunk content (for contextual enrichment)</summary>
    public string? PrecedingContent { get; init; }

    /// <summary>Following chunk content (for contextual enrichment)</summary>
    public string? FollowingContent { get; init; }

    /// <summary>Existing metadata</summary>
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();
}

/// <summary>
/// Input for document-level enrichment.
/// </summary>
public record DocumentEnrichmentInput
{
    /// <summary>Document ID</summary>
    public required string DocumentId { get; init; }

    /// <summary>Document title</summary>
    public string? Title { get; init; }

    /// <summary>Full document content (for summary generation)</summary>
    public string? FullContent { get; init; }

    /// <summary>Chunks to enrich</summary>
    public required IReadOnlyList<ChunkEnrichmentInput> Chunks { get; init; }

    /// <summary>Existing document metadata</summary>
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();
}

#endregion

#region Options Types

/// <summary>
/// Options for document/chunk enrichment.
/// </summary>
public record EnrichmentOptions
{
    /// <summary>Generate content embeddings</summary>
    public bool GenerateContentEmbedding { get; init; } = true;

    /// <summary>Generate contextual embeddings (Anthropic's approach)</summary>
    public bool GenerateContextualEmbedding { get; init; } = true;

    /// <summary>Generate hypothetical document embeddings (HyDE)</summary>
    public bool GenerateHypotheticalEmbedding { get; init; } = false;

    /// <summary>Generate entity-based embeddings</summary>
    public bool GenerateEntityEmbedding { get; init; } = false;

    /// <summary>Generate summary embeddings</summary>
    public bool GenerateSummaryEmbedding { get; init; } = false;

    /// <summary>Extract named entities</summary>
    public bool ExtractEntities { get; init; } = true;

    /// <summary>Extract relationships between entities</summary>
    public bool ExtractRelationships { get; init; } = true;

    /// <summary>Generate contextual summary</summary>
    public bool GenerateContextualSummary { get; init; } = true;

    /// <summary>Extract keywords</summary>
    public bool ExtractKeywords { get; init; } = true;

    /// <summary>Analyze chunk quality</summary>
    public bool AnalyzeQuality { get; init; } = false;

    /// <summary>Generate hypothetical questions for Q&amp;A retrieval</summary>
    public bool GenerateHypotheticalQuestions { get; init; } = false;

    /// <summary>Maximum concurrent operations</summary>
    public int MaxConcurrency { get; init; } = 4;

    /// <summary>Cache embeddings</summary>
    public bool CacheEmbeddings { get; init; } = true;

    /// <summary>Minimum confidence for entity extraction</summary>
    public double MinEntityConfidence { get; init; } = 0.7;

    /// <summary>Entity extraction options</summary>
    public EnrichmentEntityOptions? EntityExtractionOptions { get; init; }

    /// <summary>Graph build options</summary>
    public GraphBuildOptions? GraphBuildOptions { get; init; }
}

/// <summary>
/// Options for embedding generation.
/// </summary>
public record EmbeddingGenerationOptions
{
    /// <summary>Generate content embeddings</summary>
    public bool GenerateContentEmbedding { get; init; } = true;

    /// <summary>Generate summary embeddings</summary>
    public bool GenerateSummaryEmbedding { get; init; } = false;

    /// <summary>Generate hypothetical embeddings (HyDE)</summary>
    public bool GenerateHypotheticalEmbedding { get; init; } = false;

    /// <summary>Generate question embeddings for Q&amp;A retrieval</summary>
    public bool GenerateQuestionEmbeddings { get; init; } = false;

    /// <summary>Maximum questions for question embedding</summary>
    public int MaxQuestions { get; init; } = 5;

    /// <summary>Embedding types to generate</summary>
    public IReadOnlyList<EmbeddingType> Types { get; init; } = [EmbeddingType.Content];

    /// <summary>Number of hypothetical documents for HyDE</summary>
    public int HyDEDocumentCount { get; init; } = 3;

    /// <summary>Use cached embeddings if available</summary>
    public bool UseCache { get; init; } = true;

    /// <summary>Custom model ID (null for default)</summary>
    public string? ModelId { get; init; }
}

/// <summary>
/// Options for entity extraction in the enrichment pipeline.
/// </summary>
public record EnrichmentEntityOptions
{
    /// <summary>Entity types to extract (empty = all)</summary>
    public IReadOnlyList<NamedEntityType> EntityTypes { get; init; } = [];

    /// <summary>Minimum confidence threshold</summary>
    public double MinConfidence { get; init; } = 0.7;

    /// <summary>Extract relationships</summary>
    public bool ExtractRelationships { get; init; } = true;

    /// <summary>Resolve coreferences</summary>
    public bool ResolveCoreferences { get; init; } = true;

    /// <summary>Link to external knowledge bases</summary>
    public bool LinkExternalKnowledge { get; init; } = false;

    /// <summary>Maximum entities to extract per chunk</summary>
    public int MaxEntitiesPerChunk { get; init; } = 50;
}

/// <summary>
/// Options for graph building.
/// </summary>
public record GraphBuildOptions
{
    /// <summary>Merge duplicate entities</summary>
    public bool MergeEntities { get; init; } = true;

    /// <summary>Similarity threshold for entity merging (0-1)</summary>
    public double MergeThreshold { get; init; } = 0.85;

    /// <summary>Generate entity embeddings</summary>
    public bool GenerateEntityEmbeddings { get; init; } = true;

    /// <summary>Calculate importance scores</summary>
    public bool CalculateImportanceScores { get; init; } = true;

    /// <summary>Detect communities</summary>
    public bool DetectCommunities { get; init; } = false;

    /// <summary>Maximum community detection iterations</summary>
    public int MaxCommunityIterations { get; init; } = 100;

    /// <summary>Minimum community size</summary>
    public int MinCommunitySize { get; init; } = 3;

    /// <summary>Maximum number of communities to return</summary>
    public int MaxCommunities { get; init; } = 50;
}

#endregion

#region Output Types

/// <summary>
/// Enriched chunk with all generated data.
/// </summary>
public record EnrichedChunk
{
    /// <summary>Original chunk ID</summary>
    public required string ChunkId { get; init; }

    /// <summary>Document ID</summary>
    public required string DocumentId { get; init; }

    /// <summary>Original content</summary>
    public required string Content { get; init; }

    /// <summary>Multi-representation embeddings</summary>
    public MultiRepresentationEmbeddings Embeddings { get; init; } = new();

    /// <summary>Contextual summary (Anthropic's approach)</summary>
    public ContextualSummary? ContextualSummary { get; init; }

    /// <summary>Extracted entities</summary>
    public IReadOnlyList<EnrichmentEntity> Entities { get; init; } = [];

    /// <summary>Extracted relationships</summary>
    public IReadOnlyList<ExtractedRelationship> Relationships { get; init; } = [];

    /// <summary>Extracted keywords</summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>Quality metrics</summary>
    public ChunkQualityMetrics? Quality { get; init; }

    /// <summary>Enriched metadata</summary>
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();

    /// <summary>Enrichment timestamp</summary>
    public DateTimeOffset EnrichedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Enriched document with cross-chunk analysis.
/// </summary>
public record EnrichedDocument
{
    /// <summary>Document ID</summary>
    public required string DocumentId { get; init; }

    /// <summary>Document title</summary>
    public string? Title { get; init; }

    /// <summary>Document-level summary</summary>
    public string? Summary { get; init; }

    /// <summary>Document-level embedding</summary>
    public float[]? DocumentEmbedding { get; init; }

    /// <summary>Enriched chunks</summary>
    public IReadOnlyList<EnrichedChunk> Chunks { get; init; } = [];

    /// <summary>Document-level entities (merged from chunks)</summary>
    public IReadOnlyList<EnrichmentEntity> Entities { get; init; } = [];

    /// <summary>Document-level relationships</summary>
    public IReadOnlyList<ExtractedRelationship> Relationships { get; init; } = [];

    /// <summary>Document topics/themes</summary>
    public IReadOnlyList<string> Topics { get; init; } = [];

    /// <summary>Enrichment statistics</summary>
    public EnrichmentStatistics Statistics { get; init; } = new();
}

/// <summary>
/// Multi-representation embeddings for a single piece of content.
/// </summary>
public record MultiRepresentationEmbeddings
{
    /// <summary>Direct content embedding</summary>
    public float[]? Content { get; init; }

    /// <summary>Contextual embedding (with surrounding context)</summary>
    public float[]? Contextual { get; init; }

    /// <summary>Hypothetical document embedding (HyDE)</summary>
    public float[]? Hypothetical { get; init; }

    /// <summary>Entity-based embedding</summary>
    public float[]? Entity { get; init; }

    /// <summary>Summary embedding</summary>
    public float[]? Summary { get; init; }

    /// <summary>Gets embedding by type</summary>
    public float[]? GetByType(EmbeddingType type) => type switch
    {
        EmbeddingType.Content => Content,
        EmbeddingType.Contextual => Contextual,
        EmbeddingType.Hypothetical => Hypothetical,
        EmbeddingType.Entity => Entity,
        EmbeddingType.Summary => Summary,
        _ => Content
    };

    /// <summary>Gets all non-null embeddings</summary>
    public IReadOnlyDictionary<EmbeddingType, float[]> GetAll()
    {
        var result = new Dictionary<EmbeddingType, float[]>();
        if (Content is not null) result[EmbeddingType.Content] = Content;
        if (Contextual is not null) result[EmbeddingType.Contextual] = Contextual;
        if (Hypothetical is not null) result[EmbeddingType.Hypothetical] = Hypothetical;
        if (Entity is not null) result[EmbeddingType.Entity] = Entity;
        if (Summary is not null) result[EmbeddingType.Summary] = Summary;
        return result;
    }
}

/// <summary>
/// Contextual summary for a chunk (Anthropic's contextual retrieval).
/// </summary>
public record ContextualSummary
{
    /// <summary>Generated contextual summary text</summary>
    public required string Summary { get; init; }

    /// <summary>Combined text (context + original content) for embedding</summary>
    public required string CombinedText { get; init; }

    /// <summary>Document context snippet used</summary>
    public string? DocumentContext { get; init; }

    /// <summary>Confidence score</summary>
    public double Confidence { get; init; }
}

/// <summary>
/// Entity extracted during enrichment pipeline processing.
/// </summary>
public record EnrichmentEntity
{
    /// <summary>Temporary ID (before persistence)</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>Entity name</summary>
    public required string Name { get; init; }

    /// <summary>Normalized name for deduplication</summary>
    public string NormalizedName { get; init; } = string.Empty;

    /// <summary>Entity type</summary>
    public NamedEntityType Type { get; init; }

    /// <summary>Confidence score</summary>
    public double Confidence { get; init; }

    /// <summary>Surface forms (variations found in text)</summary>
    public IReadOnlyList<string> SurfaceForms { get; init; } = [];

    /// <summary>Text spans where entity was found</summary>
    public IReadOnlyList<TextSpan> Spans { get; init; } = [];

    /// <summary>Source chunk IDs</summary>
    public IReadOnlyList<string> ChunkIds { get; init; } = [];

    /// <summary>External knowledge links</summary>
    public IReadOnlyDictionary<string, string> ExternalLinks { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// Text span location.
/// </summary>
public record TextSpan(int Start, int End, string Text);

/// <summary>
/// Extracted relationship between entities.
/// </summary>
public record ExtractedRelationship
{
    /// <summary>Temporary ID</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>Source entity name/ID</summary>
    public required string SourceEntity { get; init; }

    /// <summary>Target entity name/ID</summary>
    public required string TargetEntity { get; init; }

    /// <summary>Relationship type</summary>
    public RelationType Type { get; init; }

    /// <summary>Relationship label</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Confidence score</summary>
    public double Confidence { get; init; }

    /// <summary>Evidence text</summary>
    public string? Evidence { get; init; }

    /// <summary>Source chunk IDs</summary>
    public IReadOnlyList<string> ChunkIds { get; init; } = [];
}

/// <summary>
/// Relationship with entity IDs (used during graph building).
/// </summary>
public record EnrichmentRelationship
{
    /// <summary>Temporary ID</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>Source entity ID</summary>
    public required string SourceEntityId { get; init; }

    /// <summary>Target entity ID</summary>
    public required string TargetEntityId { get; init; }

    /// <summary>Relationship type</summary>
    public RelationType Type { get; init; }

    /// <summary>Relationship label</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Confidence score</summary>
    public double Confidence { get; init; }

    /// <summary>Evidence text</summary>
    public string? Evidence { get; init; }

    /// <summary>Source chunk IDs</summary>
    public IReadOnlyList<string> ChunkIds { get; init; } = [];
}

/// <summary>
/// Detected community from graph analysis (intermediate type before GraphCommunity).
/// </summary>
public record DetectedCommunity
{
    /// <summary>Temporary community ID</summary>
    public required string Id { get; init; }

    /// <summary>Community name/label</summary>
    public required string Name { get; init; }

    /// <summary>Entity IDs in this community</summary>
    public IReadOnlyList<string> EntityIds { get; init; } = [];

    /// <summary>Number of entities</summary>
    public int Size { get; init; }

    /// <summary>Hierarchy level (0 = top)</summary>
    public int Level { get; init; }

    /// <summary>Importance score</summary>
    public double ImportanceScore { get; init; }
}

/// <summary>
/// Result of entity extraction.
/// </summary>
public record EntityExtractionResult
{
    /// <summary>Extracted entities</summary>
    public IReadOnlyList<EnrichmentEntity> Entities { get; init; } = [];

    /// <summary>Extracted relationships</summary>
    public IReadOnlyList<ExtractedRelationship> Relationships { get; init; } = [];

    /// <summary>Processing time in milliseconds</summary>
    public double ProcessingTimeMs { get; init; }
}

/// <summary>
/// Result of graph building.
/// </summary>
public record GraphBuildResult
{
    /// <summary>Entities ready for graph storage</summary>
    public IReadOnlyList<GraphEntity> Entities { get; init; } = [];

    /// <summary>Relationships ready for graph storage</summary>
    public IReadOnlyList<GraphRelationship> Relationships { get; init; } = [];

    /// <summary>Detected communities</summary>
    public IReadOnlyList<GraphCommunity> Communities { get; init; } = [];

    /// <summary>Entity merge statistics</summary>
    public EntityMergeStatistics MergeStats { get; init; } = new();
}

/// <summary>
/// Statistics about entity merging.
/// </summary>
public record EntityMergeStatistics
{
    /// <summary>Original entity count</summary>
    public int OriginalCount { get; init; }

    /// <summary>Merged entity count</summary>
    public int MergedCount { get; init; }

    /// <summary>Merge pairs</summary>
    public int MergePairs { get; init; }
}

/// <summary>
/// Quality metrics for a chunk.
/// </summary>
public record ChunkQualityMetrics
{
    /// <summary>Coherence score (0-1)</summary>
    public double Coherence { get; init; }

    /// <summary>Completeness score (0-1)</summary>
    public double Completeness { get; init; }

    /// <summary>Information density score (0-1)</summary>
    public double InformationDensity { get; init; }

    /// <summary>Readability score (0-1)</summary>
    public double Readability { get; init; }

    /// <summary>Overall quality score (0-1)</summary>
    public double Overall => (Coherence + Completeness + InformationDensity + Readability) / 4.0;
}

/// <summary>
/// Enrichment statistics.
/// </summary>
public record EnrichmentStatistics
{
    /// <summary>Total chunks processed</summary>
    public int ChunksProcessed { get; init; }

    /// <summary>Entities extracted</summary>
    public int EntitiesExtracted { get; init; }

    /// <summary>Relationships extracted</summary>
    public int RelationshipsExtracted { get; init; }

    /// <summary>Embeddings generated</summary>
    public int EmbeddingsGenerated { get; init; }

    /// <summary>Total processing time in milliseconds</summary>
    public double TotalProcessingTimeMs { get; init; }

    /// <summary>Cache hits</summary>
    public int CacheHits { get; init; }

    /// <summary>Errors encountered</summary>
    public int Errors { get; init; }
}

/// <summary>
/// Pipeline configuration.
/// </summary>
public record EnrichmentPipelineConfig
{
    /// <summary>Default enrichment options</summary>
    public EnrichmentOptions DefaultOptions { get; init; } = new();

    /// <summary>Embedding model ID</summary>
    public string EmbeddingModelId { get; init; } = "default";

    /// <summary>Text completion model ID (for HyDE, summaries)</summary>
    public string TextCompletionModelId { get; init; } = "default";

    /// <summary>Maximum batch size for embeddings</summary>
    public int MaxEmbeddingBatchSize { get; init; } = 32;

    /// <summary>Enable caching</summary>
    public bool EnableCaching { get; init; } = true;

    /// <summary>Supported embedding types</summary>
    public IReadOnlyList<EmbeddingType> SupportedEmbeddingTypes { get; init; } = [
        EmbeddingType.Content,
        EmbeddingType.Contextual
    ];
}

#endregion
