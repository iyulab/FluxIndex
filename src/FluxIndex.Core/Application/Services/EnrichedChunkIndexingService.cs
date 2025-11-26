using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Models;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// IEnrichedChunk를 처리하는 향상된 인덱싱 서비스
/// FileFlux/WebFlux의 청크를 Contextual Header와 함께 인덱싱
/// </summary>
public class EnrichedChunkIndexingService
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly IContextualHeaderGenerator _headerGenerator;
    private readonly ILogger<EnrichedChunkIndexingService> _logger;

    public EnrichedChunkIndexingService(
        IDocumentRepository documentRepository,
        IVectorStore vectorStore,
        IEmbeddingService embeddingService,
        IContextualHeaderGenerator headerGenerator,
        ILogger<EnrichedChunkIndexingService> logger)
    {
        _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _headerGenerator = headerGenerator ?? throw new ArgumentNullException(nameof(headerGenerator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// IEnrichedChunk 목록을 인덱싱
    /// </summary>
    /// <param name="chunks">FileFlux/WebFlux에서 받은 청크 목록</param>
    /// <param name="documentSummary">문서 요약 (LLM 헤더 생성에 사용)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>인덱싱된 문서</returns>
    public async Task<Document> IndexEnrichedChunksAsync(
        IEnumerable<IEnrichedChunk> chunks,
        string? documentSummary = null,
        CancellationToken cancellationToken = default)
    {
        var chunkList = chunks.ToList();
        if (chunkList.Count == 0)
        {
            throw new ArgumentException("At least one chunk is required", nameof(chunks));
        }

        var sourceId = chunkList[0].Source.SourceId;
        var sourceTitle = chunkList[0].Source.Title;

        _logger.LogInformation(
            "Starting enriched chunk indexing for {SourceId} ({Title}) with {ChunkCount} chunks",
            sourceId, sourceTitle, chunkList.Count);

        // Create document entity
        var document = Document.Create(sourceId);
        var metadata = CreateDocumentMetadata(chunkList[0].Source);
        document.UpdateMetadata(metadata);

        try
        {
            // Save document to repository
            await _documentRepository.AddAsync(document, cancellationToken);

            // Generate Contextual Headers in batch
            _logger.LogDebug("Generating contextual headers for {Count} chunks", chunkList.Count);
            var headers = await _headerGenerator.GenerateBatchAsync(
                chunkList, documentSummary, cancellationToken);

            // Process each chunk
            var augmentedChunks = new List<AugmentedChunk>();

            for (int i = 0; i < chunkList.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var enrichedChunk = chunkList[i];
                var contextualHeader = headers.GetValueOrDefault(enrichedChunk.ChunkId, string.Empty);

                // Convert to AugmentedChunk
                var augmented = EnrichedChunkAdapter.WithContextualHeader(enrichedChunk, contextualHeader);

                // Generate embedding for searchable content (header + content)
                var embedding = await _embeddingService.GenerateEmbeddingAsync(
                    augmented.SearchableContent, cancellationToken);
                augmented.ContextualEmbedding = embedding;

                // Also generate embedding for original content (for comparison)
                if (string.IsNullOrEmpty(contextualHeader))
                {
                    augmented.Embedding = embedding;
                }
                else
                {
                    augmented.Embedding = await _embeddingService.GenerateEmbeddingAsync(
                        augmented.Content, cancellationToken);
                }

                augmentedChunks.Add(augmented);

                // Convert to DocumentChunk for storage
                var documentChunk = ToDocumentChunk(augmented, sourceId);

                // Store in vector store
                await _vectorStore.StoreAsync(documentChunk, cancellationToken);

                // Add to document
                document.AddChunk(documentChunk);

                _logger.LogDebug(
                    "Indexed chunk {Index}/{Total} for {SourceId} (ContextDependency: {Dependency:F2})",
                    i + 1, chunkList.Count, sourceId, enrichedChunk.ContextDependency);
            }

            // Mark document as indexed
            document.MarkAsIndexed();
            await _documentRepository.UpdateAsync(document, cancellationToken);

            _logger.LogInformation(
                "Successfully indexed document {SourceId} with {ChunkCount} chunks",
                sourceId, chunkList.Count);

            return document;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index enriched chunks for {SourceId}", sourceId);
            document.MarkAsFailed();
            await _documentRepository.UpdateAsync(document, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// 단일 IEnrichedChunk 인덱싱
    /// </summary>
    public async Task<DocumentChunk> IndexSingleChunkAsync(
        IEnrichedChunk chunk,
        string? documentSummary = null,
        CancellationToken cancellationToken = default)
    {
        // Generate contextual header
        var header = await _headerGenerator.GenerateAsync(chunk, documentSummary, cancellationToken);

        // Convert to AugmentedChunk
        var augmented = EnrichedChunkAdapter.WithContextualHeader(chunk, header);

        // Generate embeddings
        augmented.ContextualEmbedding = await _embeddingService.GenerateEmbeddingAsync(
            augmented.SearchableContent, cancellationToken);
        augmented.Embedding = await _embeddingService.GenerateEmbeddingAsync(
            augmented.Content, cancellationToken);

        // Convert and store
        var documentChunk = ToDocumentChunk(augmented, chunk.Source.SourceId);
        await _vectorStore.StoreAsync(documentChunk, cancellationToken);

        return documentChunk;
    }

    /// <summary>
    /// ISourceMetadata를 DocumentMetadata로 변환
    /// </summary>
    private static DocumentMetadata CreateDocumentMetadata(ISourceMetadata source)
    {
        var metadata = new DocumentMetadata(
            brand: source.SourceType,
            model: source.Title,
            category: "document",
            language: source.Language,
            publishedDate: source.PublishedAt);

        // Add custom properties
        if (source.FilePath != null)
            metadata.Properties["filePath"] = source.FilePath;
        if (source.Url != null)
            metadata.Properties["url"] = source.Url;
        if (source.LanguageConfidence.HasValue)
            metadata.Properties["languageConfidence"] = source.LanguageConfidence.Value;
        if (source.PageCount.HasValue)
            metadata.Properties["pageCount"] = source.PageCount.Value;
        if (source.Author != null)
            metadata.Properties["author"] = source.Author;
        if (source.Keywords != null && source.Keywords.Count > 0)
            metadata.Properties["keywords"] = source.Keywords;

        metadata.Properties["sourceId"] = source.SourceId;
        metadata.Properties["wordCount"] = source.WordCount;
        metadata.Properties["chunkCount"] = source.ChunkCount;
        metadata.Properties["createdAt"] = source.CreatedAt;

        return metadata;
    }

    /// <summary>
    /// AugmentedChunk를 DocumentChunk로 변환
    /// </summary>
    private static DocumentChunk ToDocumentChunk(AugmentedChunk augmented, string documentId)
    {
        var chunkMetadata = new Dictionary<string, object>
        {
            ["contextualHeader"] = augmented.ContextualHeader,
            ["quality"] = augmented.Quality,
            ["contextDependency"] = augmented.ContextDependency,
            ["headingPath"] = augmented.HeadingPath
        };

        if (augmented.SectionTitle != null)
            chunkMetadata["sectionTitle"] = augmented.SectionTitle;
        if (augmented.StartPage.HasValue)
            chunkMetadata["startPage"] = augmented.StartPage.Value;
        if (augmented.EndPage.HasValue)
            chunkMetadata["endPage"] = augmented.EndPage.Value;
        if (augmented.TokenCount.HasValue)
            chunkMetadata["tokenCount"] = augmented.TokenCount.Value;
        if (augmented.Summary != null)
            chunkMetadata["summary"] = augmented.Summary;
        if (augmented.Topics != null)
            chunkMetadata["topics"] = augmented.Topics;
        if (augmented.RefinedKeywords != null)
            chunkMetadata["refinedKeywords"] = augmented.RefinedKeywords;
        if (augmented.PotentialQuestions != null)
            chunkMetadata["potentialQuestions"] = augmented.PotentialQuestions;

        // Use contextual embedding for search
        var embedding = augmented.ContextualEmbedding ?? augmented.Embedding ?? [];

        var chunk = DocumentChunk.Create(
            documentId: documentId,
            content: augmented.SearchableContent,  // Store searchable content (header + content)
            chunkIndex: augmented.ChunkIndex,
            totalChunks: 1);

        // Set embedding
        if (embedding.Length > 0)
        {
            chunk.SetEmbedding(new EmbeddingVector(embedding, "contextual-embedding"));
        }

        // Set metadata
        chunk.Metadata = chunkMetadata;
        chunk.TokenCount = augmented.TokenCount ?? 0;

        return chunk;
    }
}
