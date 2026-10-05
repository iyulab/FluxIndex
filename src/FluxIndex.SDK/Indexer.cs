using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.ValueObjects;
using FluxIndex.Core.Interfaces;
using FluxIndex.Core.Models;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using DocumentChunkEntity = FluxIndex.Core.Domain.Entities.DocumentChunk;
using DocumentChunkModel = FluxIndex.Core.Domain.Models.CacheDocumentChunk;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using FluxIndex.SDK.Services;
using FluxIndex.Core.Application.Services;

namespace FluxIndex.SDK;

/// <summary>
/// Indexer - 문서 인덱싱 및 저장 담당 (Phase 3: DX 개선으로 이벤트 및 진행률 모니터링 지원)
/// </summary>
public partial class Indexer
{
    private readonly IVectorStore _vectorStore;
    private readonly IDocumentRepository _documentRepository;
    private readonly IEmbeddingService _embeddingService;
    private readonly IChunkingService _chunkingService;
    private readonly IMetadataExtractor? _metadataExtractor;
    private readonly IGraphRAGService? _graphRAGService;
    private readonly IHybridSearchService? _hybridSearchService;
    private readonly IKeywordSearchService? _keywordSearchService;
    private readonly ILogger<Indexer> _logger;
    private readonly IndexerOptions _options;
    private readonly ICacheService? _cacheService;

    /// <summary>
    /// GraphRAG 서비스 사용 가능 여부
    /// </summary>
    public bool SupportsGraphRAG => _graphRAGService != null;

    /// <summary>
    /// 하이브리드 검색 서비스 사용 가능 여부
    /// </summary>
    public bool SupportsHybridSearch => _hybridSearchService != null;

    /// <summary>
    /// Whether an <see cref="IMetadataExtractor"/> is configured, so indexing can extract AI metadata
    /// (<c>IndexingOptions.WithAIMetadataExtraction</c>). Indexing that asks for it without one throws.
    /// </summary>
    public bool SupportsAIMetadata => _metadataExtractor != null;

    /// <summary>
    /// Whether this indexer adds new documents to the keyword (sparse) index. When false, nothing
    /// populates the keyword leg and hybrid search degrades to vector-only.
    /// Removal is deliberately not gated on this — see <see cref="HasKeywordIndex"/>.
    /// </summary>
    public bool MaintainsKeywordIndex => HasKeywordIndex && _options.IndexKeyword;

    /// <summary>
    /// Whether a keyword index is registered at all. Deletions follow this rather than
    /// <see cref="MaintainsKeywordIndex"/>: turning off keyword indexing means "stop adding", not
    /// "stop removing". Gating removal too would leave postings for deleted documents in a
    /// persistent index forever, and they would keep matching queries.
    /// </summary>
    private bool HasKeywordIndex => _keywordSearchService != null;

    // Phase 3: 이벤트 기반 모니터링
    /// <summary>
    /// 인덱싱 시작 시 발생하는 이벤트
    /// </summary>
    public event EventHandler<IndexingStartedEventArgs>? IndexingStarted;

    /// <summary>
    /// 인덱싱 완료 시 발생하는 이벤트
    /// </summary>
    public event EventHandler<IndexingCompletedEventArgs>? IndexingCompleted;

    /// <summary>
    /// 인덱싱 실패 시 발생하는 이벤트
    /// </summary>
    public event EventHandler<IndexingFailedEventArgs>? IndexingFailed;

    /// <summary>
    /// 배치 작업 시작 시 발생하는 이벤트
    /// </summary>
    public event EventHandler<BatchStartedEventArgs>? BatchStarted;

    /// <summary>
    /// 배치 작업 완료 시 발생하는 이벤트
    /// </summary>
    public event EventHandler<BatchCompletedEventArgs>? BatchCompleted;

    public Indexer(
        IVectorStore vectorStore,
        IDocumentRepository documentRepository,
        IEmbeddingService embeddingService,
        IChunkingService chunkingService,
        IndexerOptions options,
        ILogger<Indexer>? logger = null,
        IMetadataExtractor? metadataExtractor = null,
        IGraphRAGService? graphRAGService = null,
        IHybridSearchService? hybridSearchService = null,
        IKeywordSearchService? keywordSearchService = null,
        ICacheService? cacheService = null)
    {
        _vectorStore = vectorStore;
        _documentRepository = documentRepository;
        _embeddingService = embeddingService;
        _chunkingService = chunkingService;
        _metadataExtractor = metadataExtractor;
        _graphRAGService = graphRAGService;
        _hybridSearchService = hybridSearchService;
        _keywordSearchService = keywordSearchService;
        _cacheService = cacheService;
        _options = options;
        _logger = logger ?? NullLogger<Indexer>.Instance;

        // Bind embedding identity to vector store for correct collection resolution. A keyword-only context has no
        // vector space, so nothing is bound: the store keeps chunks and writes no vector table.
        if (!IsKeywordOnly)
            _vectorStore.BindIdentity(_embeddingService.GetIdentity());
    }

    /// <summary>
    /// Whether this indexer runs without an embedding service (<see cref="NoEmbeddingService"/>): chunks are stored
    /// without vectors and the keyword index is what makes them searchable.
    /// </summary>
    public bool IsKeywordOnly => NoEmbeddingService.IsKeywordOnly(_embeddingService);

    /// <summary>
    /// 간편 API: 문자열 콘텐츠로 직접 문서 인덱싱
    /// README 예제 코드와 호환되는 간단한 인터페이스 제공
    /// </summary>
    /// <remarks>
    /// 이 오버로드만 SDK 자체가 청킹한다 — <c>content</c> 를 <see cref="IndexerOptions.ChunkSize"/> /
    /// <see cref="IndexerOptions.ChunkOverlap"/>(문자 수, 단어 경계) 기준으로 나눠 청크마다 한 행을 만든다.
    /// <see cref="Document"/> 를 받는 오버로드는 호출자가 이미 나눈 <see cref="Document.Chunks"/> 를 그대로 쓴다.
    /// 나뉜 청크는 전부 같은 <paramref name="metadata"/> 를 실으므로 필터는 어느 청크에나 매치된다.
    /// <para>
    /// 같은 <paramref name="documentId"/> 로 다시 부르면 그 문서를 <b>교체</b>한다 — 이전 판의 청크는 벡터 저장소와
    /// 키워드 색인에서 지워지고(청크 수가 줄면 남는 꼬리도), 문서 레코드도 새 판이 된다. 청크를 덧붙이려면
    /// <see cref="AddChunksAsync"/> 를 쓴다. 저장소마다 가능한 곳에서는 원자적이다(sqlite-vec 저장소와 관계형 키워드 색인은
    /// 각각 한 트랜잭션); 둘 사이가 끊기면 같은 호출을 다시 하면 복구된다.
    /// </para>
    /// </remarks>
    /// <param name="content">인덱싱할 문서 내용</param>
    /// <param name="documentId">문서 ID</param>
    /// <param name="metadata">
    /// 메타데이터 (선택). 문서에 저장되는 동시에 이 문서의 청크에도 복사되어
    /// <see cref="Retriever.SearchAsync(string, int?, float?, Dictionary{string, object}?, CancellationToken)"/> 의
    /// <c>filter</c> 및 <see cref="Core.Application.Interfaces.IVectorStore.DeleteByFilterAsync"/> 에서 매치된다
    /// (두 계약 모두 청크 메타데이터를 읽는다).
    /// NOTE: 문서 레벨 메타데이터 자체는 영속되지 않는다 — <c>IDocumentRepository</c> 는 현재 인메모리 구현뿐이라
    /// 프로세스 재시작 후에는 청크에 복사된 값만 남는다.
    /// </param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>인덱싱된 문서 ID</returns>
    public async Task<string> IndexDocumentAsync(
        string content,
        string documentId,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        return await IndexDocumentAsync(CreateChunkedDocument(content, documentId, metadata), cancellationToken);
    }

    /// <summary>
    /// The document the string overloads index: <paramref name="content"/> split by the configured chunk size, every
    /// chunk carrying <paramref name="metadata"/>.
    /// </summary>
    private Document CreateChunkedDocument(string content, string documentId, Dictionary<string, object>? metadata)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content cannot be empty", nameof(content));
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be empty", nameof(documentId));

        var document = Document.Create(documentId);
        document.Content = content;

        if (metadata != null)
        {
            foreach (var (key, value) in metadata)
            {
                document.SetMetadata(key, value);
            }
        }

        // Split by the configured chunk size. The chunking service is the one the builder registered
        // from IndexerOptions.ChunkSize/ChunkOverlap; before 0.38.0 it was injected and never called,
        // so this overload stored the whole content as one chunk regardless of the option.
        var pieces = _chunkingService.ChunkText(content, _options.ChunkSize, _options.ChunkOverlap).ToList();
        if (pieces.Count == 0)
            pieces.Add(content);

        // Every chunk carries the caller's metadata so filters can see it on any of them
        for (var i = 0; i < pieces.Count; i++)
        {
            var chunk = DocumentChunkEntity.Create(documentId, pieces[i], i, pieces.Count);
            MergeDocumentMetadataIntoChunk(chunk, metadata);
            document.AddChunk(chunk);
        }

        return document;
    }

    /// <summary>
    /// 문서 인덱싱. 이미 색인된 <see cref="Document.Id"/> 면 그 문서를 교체한다
    /// (<see cref="IndexDocumentAsync(string, string, Dictionary{string, object}?, CancellationToken)"/> 참조).
    /// </summary>
    public async Task<string> IndexDocumentAsync(
        Document document,
        CancellationToken cancellationToken = default)
    {
        return await IndexDocumentAsync(document, null, null, cancellationToken);
    }

    /// <summary>
    /// 문서 인덱싱 (진행률 모니터링 지원). 이미 색인된 <see cref="Document.Id"/> 면 그 문서를 교체한다.
    /// </summary>
    /// <param name="document">인덱싱할 문서</param>
    /// <param name="progress">진행률 보고 객체</param>
    /// <param name="cancellationToken">취소 토큰</param>
    public async Task<string> IndexDocumentAsync(
        Document document,
        IProgress<IndexingProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        return await IndexDocumentAsync(document, null, progress, cancellationToken);
    }

    /// <summary>
    /// 문서 인덱싱 (IndexingOptions 지원). 이미 색인된 <see cref="Document.Id"/> 면 그 문서를 교체한다.
    /// </summary>
    /// <param name="document">인덱싱할 문서</param>
    /// <param name="options">인덱싱 옵션. null이면 등록된 서비스에 따라 자동 설정</param>
    /// <param name="cancellationToken">취소 토큰</param>
    public async Task<string> IndexDocumentAsync(
        Document document,
        IndexingOptions? options,
        CancellationToken cancellationToken = default)
    {
        return await IndexDocumentAsync(document, options, null, cancellationToken);
    }

    /// <summary>
    /// 문서 인덱싱 (진행률 모니터링 지원). 이미 색인된 <see cref="Document.Id"/> 면 그 문서를 교체한다 — 이전 판의 청크는
    /// 벡터 저장소와 키워드 색인에서 지워지고 문서 레코드도 새 판이 된다. 청크의 <c>DocumentId</c> 가 아니라 문서의 id 로
    /// 교체하므로, 다른 문서 id 의 청크를 싣는 문서(예: FileFlux 스트리밍의 부분 문서)는 그 id 의 청크를 지우지 않는다.
    /// </summary>
    /// <param name="document">인덱싱할 문서</param>
    /// <param name="options">인덱싱 옵션. null이면 등록된 서비스에 따라 자동 설정</param>
    /// <param name="progress">진행률 보고 객체 (선택)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    public Task<string> IndexDocumentAsync(
        Document document,
        IndexingOptions? options,
        IProgress<IndexingProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        return IndexDocumentCoreAsync(document, options, progress, [document.Id], cancellationToken);
    }

    /// <summary>
    /// Indexes <paramref name="document"/> as the new version of <paramref name="replacedDocumentIds"/>: whatever the
    /// store and the keyword index hold for those ids is replaced by the document's chunks.
    /// </summary>
    private async Task<string> IndexDocumentCoreAsync(
        Document document,
        IndexingOptions? options,
        IProgress<IndexingProgress>? progress,
        IReadOnlyCollection<string> replacedDocumentIds,
        CancellationToken cancellationToken)
    {
        var jobId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        LogIndexingDocument(_logger, document.Id, jobId);

        try
        {
            var prepared = await PrepareDocumentAsync(document, options, progress, jobId, startTime, cancellationToken);
            await WriteDocumentsAsync([prepared], replacedDocumentIds, options, progress, cancellationToken);
            CompleteDocument(prepared, progress);
            return document.Id;
        }
        catch (Exception ex)
        {
            LogFailedToIndexDocument(_logger, ex, document.Id);

            // Phase 3: 이벤트 발생 - 인덱싱 실패
            IndexingFailed?.Invoke(this, new IndexingFailedEventArgs
            {
                JobId = jobId,
                DocumentId = document.Id,
                ErrorMessage = ex.Message,
                Exception = ex
            });

            throw;
        }
    }

    /// <summary>
    /// A document ready to be written: metadata extracted, oversized chunks split, chunks embedded.
    /// </summary>
    private sealed class PreparedDocument
    {
        public required Document Document { get; init; }
        public required string JobId { get; init; }
        public required DateTime StartTime { get; init; }
        public required List<DocumentChunkEntity> Chunks { get; init; }
    }

    /// <summary>
    /// Everything indexing does to one document before anything is written. Nothing here touches a store, so a batch
    /// prepares each document on its own (a failure is that document's) and then writes them together.
    /// </summary>
    private async Task<PreparedDocument> PrepareDocumentAsync(
        Document document,
        IndexingOptions? options,
        IProgress<IndexingProgress>? progress,
        string jobId,
        DateTime startTime,
        CancellationToken cancellationToken)
    {
        // Phase 3: 이벤트 발생 - 인덱싱 시작
        var chunks = document.Chunks.ToList();
        CarryFileNameIntoChunks(document, chunks);
        IndexingStarted?.Invoke(this, new IndexingStartedEventArgs
        {
            JobId = jobId,
            DocumentId = document.Id,
            TotalChunks = chunks.Count,
            StartedAt = startTime
        });

        // Phase 3: 진행률 보고 - 초기화
        progress?.Report(new IndexingProgress
        {
            JobId = jobId,
            DocumentId = document.Id,
            CurrentChunk = 0,
            TotalChunks = chunks.Count,
            ProgressPercentage = 0,
            Status = "Starting",
            Message = "Saving document metadata"
        });

        // A configuration error is found before anything is written, not after the chunks are stored.
        if (options?.EnableGraphRAG == true && _graphRAGService == null)
        {
            throw new InvalidOperationException(
                "GraphRAG is enabled but IGraphRAGService is not registered. " +
                "Register it with ConfigureServices(s => s.AddFullGraphRAG()), or register your own IGraphRAGService.");
        }

        // Phase 3: AI 메타데이터 추출 (선택적)
        // Builder-level defaults (IndexerOptions.CustomOptions) overlaid by this call's
        // IndexingOptions.CustomOptions — the caller's keys win. Before 0.38.0 the per-call
        // options were discarded here, so `IndexingOptions.WithAIMetadataExtraction(...)`
        // passed to this method had no effect.
        var indexingOptions = ResolveMetadataOptions(options);

        // Extraction asked for with nothing to extract with is a configuration error, not an optional step to skip.
        if (_metadataExtractor == null && indexingOptions.ShouldExtractAIMetadata())
        {
            throw new InvalidOperationException(
                "AI metadata extraction was requested, but no metadata extractor is configured. " +
                "Register an IMetadataExtractor via ConfigureServices(...) on the builder.");
        }

        if (_metadataExtractor != null && !string.IsNullOrEmpty(document.Content))
        {
            try
            {
                LogExtractingAIMetadata(_logger, document.Id);

                if (indexingOptions.ShouldExtractAIMetadata())
                {
                    var schema = indexingOptions.GetMetadataSchema();
                    var strategy = indexingOptions.GetMetadataExtractionStrategy();
                    var minConfidence = indexingOptions.GetMinMetadataConfidence();
                    var customPrompt = indexingOptions.GetCustomMetadataPrompt();

                    var extractionOptions = new AIMetadataExtractionOptions
                    {
                        Strategy = strategy,
                        MinConfidence = minConfidence,
                        CustomPrompt = customPrompt
                    };

                    // 캐시 키 생성
                    var cacheKey = _metadataExtractor.GenerateCacheKey(document.Content, schema);

                    // AI 메타데이터 추출 (캐싱 지원)
                    var extractedMetadata = await _metadataExtractor.ExtractWithCacheAsync(
                        document.Content,
                        cacheKey,
                        schema,
                        extractionOptions,
                        cancellationToken);

                    // IndexingResult에 메타데이터 저장 (Document.Metadata에 포함)
                    document.SetMetadata("AIExtractedMetadata", extractedMetadata);
                    document.SetMetadata("MetadataExtractionMethod", extractedMetadata.ExtractionMethod);
                    document.SetMetadata("MetadataConfidence", extractedMetadata.OverallConfidence);

                    LogAIMetadataExtracted(_logger, extractedMetadata.OverallConfidence, extractedMetadata.Topics.Length);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                LogFailedToExtractAIMetadata(_logger, ex, document.Id);
                // Continue indexing without AI metadata
            }
        }

        if (chunks.Count == 0)
        {
            LogDocumentHasNoChunks(_logger, document.Id);
            return new PreparedDocument { Document = document, JobId = jobId, StartTime = startTime, Chunks = [] };
        }

        // Phase 3: 진행률 보고 - 청크 처리 시작
        progress?.Report(new IndexingProgress
        {
            JobId = jobId,
            DocumentId = document.Id,
            CurrentChunk = 0,
            TotalChunks = chunks.Count,
            ProgressPercentage = 10,
            Status = "Processing",
            Message = $"Processing {chunks.Count} chunks"
        });

        // Convert to Entity chunks first with automatic oversized chunk splitting
        LogConvertingChunksToEntities(_logger, chunks.Count);

        var entityChunks = new List<DocumentChunkEntity>();
        var chunkIndex = 0;

        foreach (var chunk in chunks)
        {
            var estimatedTokens = chunk.Content.Length / 4;
            LogChunkDetails(_logger, chunk.ChunkIndex, chunks.Count, chunk.Content.Length, estimatedTokens);

            // SAFETY: Split oversized chunks automatically (WebFlux chunking bug workaround)
            if (estimatedTokens > 8000)
            {
                LogChunkExceedsTokenLimit(_logger, chunk.ChunkIndex, estimatedTokens);

                // Split into chunks of ~2000 tokens (8000 chars) to be safe
                const int maxChunkChars = 8000; // ~2000 tokens
                var content = chunk.Content;
                var subChunkCount = (int)Math.Ceiling((double)content.Length / maxChunkChars);

                for (int i = 0; i < subChunkCount; i++)
                {
                    var startPos = i * maxChunkChars;
                    var length = Math.Min(maxChunkChars, content.Length - startPos);
                    var subContent = content.Substring(startPos, length);

                    // Positions are renumbered after the loop once the final count is known; the
                    // factory only needs a total the running index cannot exceed.
                    var subChunk = DocumentChunkEntity.Create(
                        chunk.DocumentId,
                        subContent,
                        chunkIndex,
                        chunkIndex + 1
                    );
                    chunkIndex++;
                    // Copy metadata if exists
                    if (chunk.Metadata != null)
                    {
                        subChunk.Metadata = chunk.Metadata;
                    }

                    entityChunks.Add(subChunk);
                    LogSubChunkCreated(_logger, i + 1, subChunkCount, subContent.Length, subContent.Length / 4);
                }

                LogSplitOversizedChunk(_logger, chunk.ChunkIndex, subChunkCount);
            }
            else
            {
                // Normal sized chunk - add directly
                entityChunks.Add(chunk);
                chunkIndex++;
            }
        }

        LogTotalEntityChunksAfterSplitting(_logger, entityChunks.Count, chunks.Count);

        // A split changed the positions of everything after it: renumber so ChunkIndex/TotalChunks
        // describe the chunks that are actually stored (the sub-chunks were created with a
        // provisional total, and the caller's untouched chunks still carry the pre-split count).
        if (entityChunks.Count != chunks.Count)
        {
            for (var i = 0; i < entityChunks.Count; i++)
            {
                entityChunks[i].ChunkIndex = i;
                entityChunks[i].TotalChunks = entityChunks.Count;
            }
        }

        // Phase 3: 진행률 보고 - 임베딩 생성
        progress?.Report(new IndexingProgress
        {
            JobId = jobId,
            DocumentId = document.Id,
            CurrentChunk = 0,
            TotalChunks = chunks.Count,
            ProgressPercentage = 30,
            Status = "Embedding",
            Message = "Generating embeddings"
        });

        LogCallingGenerateEmbeddings(_logger, entityChunks.Count);

        // Generate embeddings for entity chunks
        var embeddedEntityChunks = await GenerateEmbeddingsAsync(entityChunks, cancellationToken);
        return new PreparedDocument { Document = document, JobId = jobId, StartTime = startTime, Chunks = embeddedEntityChunks };
    }

    /// <summary>
    /// Writes prepared documents as the new versions of <paramref name="replacedDocumentIds"/>: the records, then every
    /// chunk through one replacement per store, then GraphRAG per document. One call is one transaction per store
    /// (where the store has transactions), however many documents it carries.
    /// </summary>
    private async Task WriteDocumentsAsync(
        IReadOnlyList<PreparedDocument> documents,
        IReadOnlyCollection<string> replacedDocumentIds,
        IndexingOptions? options,
        IProgress<IndexingProgress>? progress,
        CancellationToken cancellationToken)
    {
        var chunks = documents.SelectMany(d => d.Chunks).ToList();
        if (progress != null && documents.Count == 1 && chunks.Count > 0)
        {
            progress.Report(new IndexingProgress
            {
                JobId = documents[0].JobId,
                DocumentId = documents[0].Document.Id,
                CurrentChunk = chunks.Count,
                TotalChunks = chunks.Count,
                ProgressPercentage = 80,
                Status = "Storing",
                Message = "Storing in vector store"
            });
        }

        // Records are upserts: indexing an id again stores the new version of the record too.
        foreach (var prepared in documents)
            await _documentRepository.UpdateAsync(prepared.Document, cancellationToken);

        // Store in vector store, replacing the previous versions. The keyword index follows in step: without it the
        // hybrid keyword leg is only ever populated by whatever searched in this process, so it is empty after a
        // restart and hybrid silently degrades to vector-only.
        await ReplaceChunksAsync(replacedDocumentIds, chunks, cancellationToken);

        foreach (var documentId in replacedDocumentIds)
            await InvalidateReadCachesAsync(documentId, cancellationToken);

        // GraphRAG 인덱싱 (자동 감지)
        // - options?.EnableGraphRAG == null: 서비스가 등록되어 있으면 자동 활성화
        // - options?.EnableGraphRAG == true: 강제 활성화 (미등록은 준비 단계에서 거부)
        // - options?.EnableGraphRAG == false: 강제 비활성화
        var enableGraphRAG = options?.EnableGraphRAG ?? (_graphRAGService != null);
        if (!enableGraphRAG || _graphRAGService == null)
            return;

        foreach (var prepared in documents.Where(d => d.Chunks.Count > 0))
        {
            progress?.Report(new IndexingProgress
            {
                JobId = prepared.JobId,
                DocumentId = prepared.Document.Id,
                CurrentChunk = prepared.Chunks.Count,
                TotalChunks = prepared.Chunks.Count,
                ProgressPercentage = 90,
                Status = "GraphRAG",
                Message = "Building GraphRAG index"
            });

            LogBuildingGraphRAGIndex(_logger, prepared.Document.Id);
            await _graphRAGService.BuildIndexAsync(prepared.Chunks, options?.GraphRAGOptions, cancellationToken);
            LogGraphRAGIndexBuilt(_logger, prepared.Document.Id);
        }
    }

    /// <summary>Reports a written document as completed.</summary>
    private void CompleteDocument(PreparedDocument prepared, IProgress<IndexingProgress>? progress)
    {
        var count = prepared.Chunks.Count;

        // Phase 3: 진행률 보고 - 완료
        progress?.Report(new IndexingProgress
        {
            JobId = prepared.JobId,
            DocumentId = prepared.Document.Id,
            CurrentChunk = count,
            TotalChunks = count,
            ProgressPercentage = 100,
            Status = "Completed",
            Message = "Indexing completed successfully"
        });

        LogSuccessfullyIndexedDocument(_logger, prepared.Document.Id, count);

        // Phase 3: 이벤트 발생 - 인덱싱 완료
        IndexingCompleted?.Invoke(this, new IndexingCompletedEventArgs
        {
            JobId = prepared.JobId,
            DocumentId = prepared.Document.Id,
            ChunksIndexed = count,
            TotalChunks = count,
            Success = true,
            ProcessingTime = DateTime.UtcNow - prepared.StartTime
        });
    }

    /// <summary>
    /// Prepares every document on its own (up to <paramref name="parallelism"/> at a time), then writes all prepared
    /// documents at once. A document that fails preparation is reported through <paramref name="onDocumentDone"/> and
    /// left out; the write is one replacement per store (see <see cref="WriteDocumentsAsync"/>) and throws to the caller.
    /// When one id appears more than once, the last occurrence is the version written.
    /// </summary>
    /// <returns>The ids written, in input order.</returns>
    private async Task<List<string>> IndexDocumentsCoreAsync(
        List<Document> documents,
        IndexingOptions? options,
        int parallelism,
        Action<Document, Exception?>? onDocumentDone,
        CancellationToken cancellationToken)
    {
        var prepared = new PreparedDocument?[documents.Count];
        using var gate = new SemaphoreSlim(Math.Max(1, parallelism));

        await Task.WhenAll(documents.Select(async (document, index) =>
        {
            await gate.WaitAsync(cancellationToken);
            var jobId = Guid.NewGuid().ToString();
            try
            {
                LogIndexingDocument(_logger, document.Id, jobId);
                prepared[index] = await PrepareDocumentAsync(document, options, null, jobId, DateTime.UtcNow, cancellationToken);
                onDocumentDone?.Invoke(document, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                LogFailedToIndexDocument(_logger, ex, document.Id);
                IndexingFailed?.Invoke(this, new IndexingFailedEventArgs
                {
                    JobId = jobId,
                    DocumentId = document.Id,
                    ErrorMessage = ex.Message,
                    Exception = ex
                });
                onDocumentDone?.Invoke(document, ex);
            }
            finally
            {
                gate.Release();
            }
        }));

        // The last occurrence of an id is its version; earlier ones would otherwise be stored beside it.
        var lastIndexById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < prepared.Length; i++)
        {
            if (prepared[i] is { } p)
                lastIndexById[p.Document.Id] = i;
        }

        var toWrite = prepared
            .Where((p, i) => p != null && lastIndexById[p.Document.Id] == i)
            .Select(p => p!)
            .ToList();
        if (toWrite.Count == 0)
            return [];

        try
        {
            await WriteDocumentsAsync(toWrite, lastIndexById.Keys.ToList(), options, null, cancellationToken);
        }
        catch (Exception ex)
        {
            foreach (var p in toWrite)
            {
                LogFailedToIndexDocument(_logger, ex, p.Document.Id);
                IndexingFailed?.Invoke(this, new IndexingFailedEventArgs
                {
                    JobId = p.JobId,
                    DocumentId = p.Document.Id,
                    ErrorMessage = ex.Message,
                    Exception = ex
                });
            }
            throw;
        }

        foreach (var p in toWrite)
            CompleteDocument(p, null);

        return prepared.Where(p => p != null).Select(p => p!.Document.Id).ToList();
    }

    /// <summary>
    /// 청크 리스트에서 문서 생성 및 인덱싱
    /// </summary>
    /// <param name="chunks">인덱싱할 청크. 각 청크의 <c>Metadata</c> 는 그대로 보존된다.</param>
    /// <param name="documentId">문서 ID (생략 시 자동 생성)</param>
    /// <param name="metadata">
    /// 문서 레벨 메타데이터 (선택). 각 청크에 base 레이어로 복사되어 filter 계약에서 매치된다 —
    /// 키가 겹치면 청크 자신의 값이 승리한다. <see cref="IndexDocumentAsync(string, string, Dictionary{string, object}?, CancellationToken)"/> 의
    /// <c>metadata</c> 설명 참조.
    /// </param>
    /// <param name="cancellationToken">취소 토큰</param>
    public async Task<string> IndexChunksAsync(
        IEnumerable<DocumentChunkModel> chunks,
        string? documentId = null,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        documentId ??= Guid.NewGuid().ToString();
        LogIndexingChunksAsDocument(_logger, documentId);

        // Materialize chunks for multiple iterations
        var chunkList = chunks.ToList();

        // Create document with combined content from all chunks
        var combinedContent = string.Join("\n", chunkList.Select(c => c.Content));
        var document = new Document { Id = documentId, Content = combinedContent, CreatedAt = DateTime.UtcNow };

        // Add metadata if provided
        if (metadata != null)
        {
            foreach (var (key, value) in metadata)
            {
                document.SetMetadata(key, value);
            }
        }

        // Add chunks to document, layering document metadata under each chunk's own
        foreach (var chunk in chunkList)
        {
            var entityChunk = ConvertToEntityChunk(chunk);
            MergeDocumentMetadataIntoChunk(entityChunk, metadata);
            document.AddChunk(entityChunk);
        }

        // Index the document
        return await IndexDocumentAsync(document, cancellationToken);
    }


    /// <summary>
    /// 배치 인덱싱
    /// </summary>
    public async Task<IEnumerable<string>> IndexBatchAsync(
        IEnumerable<Document> documents,
        int parallelism = 4,
        CancellationToken cancellationToken = default)
    {
        return await IndexBatchAsync(documents, null, parallelism, cancellationToken);
    }

    /// <summary>
    /// 배치 인덱싱 (Phase 3: 진행률 모니터링 지원)
    /// </summary>
    /// <param name="documents">인덱싱할 문서 목록</param>
    /// <param name="progress">배치 진행률 보고 객체 (선택)</param>
    /// <param name="parallelism">병렬 처리 수준</param>
    /// <param name="cancellationToken">취소 토큰</param>
    public async Task<IEnumerable<string>> IndexBatchAsync(
        IEnumerable<Document> documents,
        IProgress<BatchProgress>? progress,
        int parallelism = 4,
        CancellationToken cancellationToken = default)
    {
        var batchId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        var documentList = documents.ToList();
        var totalDocuments = documentList.Count;

        LogBatchIndexing(_logger, totalDocuments, batchId);

        // Phase 3: 이벤트 발생 - 배치 시작
        BatchStarted?.Invoke(this, new BatchStartedEventArgs
        {
            BatchId = batchId,
            TotalItems = totalDocuments,
            BatchType = "IndexBatch",
            StartedAt = startTime
        });

        // Phase 3: 진행률 보고 - 초기화
        progress?.Report(new BatchProgress
        {
            BatchId = batchId,
            CurrentItem = 0,
            TotalItems = totalDocuments,
            ProgressPercentage = 0,
            Status = "Starting",
            Message = $"Starting batch indexing of {totalDocuments} documents",
            SuccessfulItems = 0,
            FailedItems = 0
        });

        var completedCount = 0;
        var successCount = 0;
        var failedCount = 0;
        var lockObject = new object();

        var results = await IndexDocumentsCoreAsync(documentList, null, parallelism, (doc, error) =>
        {
            if (error != null)
                LogFailedToIndexDocumentInBatch(_logger, error, doc.Id, batchId);

            lock (lockObject)
            {
                completedCount++;
                if (error == null) successCount++; else failedCount++;

                progress?.Report(new BatchProgress
                {
                    BatchId = batchId,
                    CurrentItem = completedCount,
                    TotalItems = totalDocuments,
                    ProgressPercentage = (float)completedCount / totalDocuments * 100,
                    Status = "Processing",
                    Message = error == null
                        ? $"Prepared document {doc.Id} ({completedCount}/{totalDocuments})"
                        : $"Failed to index document {doc.Id} ({completedCount}/{totalDocuments})",
                    SuccessfulItems = successCount,
                    FailedItems = failedCount
                });
            }
        }, cancellationToken);

        // Phase 3: 진행률 보고 - 완료
        progress?.Report(new BatchProgress
        {
            BatchId = batchId,
            CurrentItem = totalDocuments,
            TotalItems = totalDocuments,
            ProgressPercentage = 100,
            Status = "Completed",
            Message = $"Batch indexing completed: {successCount} succeeded, {failedCount} failed",
            SuccessfulItems = successCount,
            FailedItems = failedCount
        });

        LogBatchIndexingCompleted(_logger, successCount, totalDocuments, batchId);

        // Phase 3: 이벤트 발생 - 배치 완료
        BatchCompleted?.Invoke(this, new BatchCompletedEventArgs
        {
            BatchId = batchId,
            TotalItems = totalDocuments,
            SuccessfulItems = successCount,
            FailedItems = failedCount,
            TotalProcessingTime = DateTime.UtcNow - startTime
        });

        return results;
    }

    /// <summary>
    /// Replaces document <paramref name="documentId"/> with <paramref name="updatedDocument"/>. The same as indexing
    /// <paramref name="updatedDocument"/> (see <see cref="IndexDocumentAsync(Document, IndexingOptions?, IProgress{IndexingProgress}?, CancellationToken)"/>),
    /// except that the chunks of <paramref name="documentId"/> are replaced too when the updated document carries a
    /// different id — the old id's record is then removed.
    /// </summary>
    public async Task UpdateDocumentAsync(
        string documentId,
        Document updatedDocument,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentNullException.ThrowIfNull(updatedDocument);
        LogUpdatingDocument(_logger, documentId);

        await IndexDocumentCoreAsync(updatedDocument, null, null, [documentId, updatedDocument.Id], cancellationToken);

        if (!string.Equals(documentId, updatedDocument.Id, StringComparison.Ordinal))
        {
            await _documentRepository.DeleteAsync(documentId, cancellationToken);
            await InvalidateReadCachesAsync(documentId, cancellationToken);
        }

        LogSuccessfullyUpdatedDocument(_logger, documentId);
    }

    /// <summary>
    /// 청크 추가
    /// </summary>
    public async Task AddChunksAsync(
        string documentId,
        IEnumerable<string> chunkTexts,
        CancellationToken cancellationToken = default)
    {
        var chunkCount = chunkTexts.Count();
        LogAddingChunks(_logger, chunkCount, documentId);

        // Get existing document
        var document = await _documentRepository.GetByIdAsync(documentId, cancellationToken);
        if (document == null)
            throw new InvalidOperationException($"Document {documentId} not found");

        // Get current max chunk index
        var existingChunks = await _vectorStore.GetByDocumentIdAsync(documentId, cancellationToken);
        var maxIndex = existingChunks.Any() ? existingChunks.Max(c => c.ChunkIndex) : -1;

        // Create new chunks
        var newChunks = new List<DocumentChunkEntity>();
        foreach (var text in chunkTexts)
        {
            var chunk = DocumentChunkEntity.Create(
                documentId,
                text,
                ++maxIndex,
                existingChunks.Count() + chunkTexts.Count());
            newChunks.Add(chunk);
        }

        // Generate embeddings and store
        newChunks = await GenerateEmbeddingsAsync(newChunks, cancellationToken);
        await _vectorStore.StoreBatchAsync(newChunks, cancellationToken);
        await IndexKeywordAsync(newChunks, cancellationToken);
        await InvalidateReadCachesAsync(documentId, cancellationToken);

        LogSuccessfullyAddedChunks(_logger, newChunks.Count, documentId);
    }

    /// <summary>
    /// Fills the vectors of a document's chunks that were stored without one: chunks indexed keyword-only, before an
    /// embedding service was registered. Only those chunks are embedded (one batch), and each is written back under its
    /// own id, which adds its vector. Content, metadata and the keyword index are left as they are.
    /// </summary>
    /// <returns>The number of chunks that got a vector (0 when every chunk already had one, or the document has none).</returns>
    /// <exception cref="InvalidOperationException">This indexer has no embedding service (<see cref="IsKeywordOnly"/>).</exception>
    public async Task<int> BackfillEmbeddingsAsync(string documentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        if (IsKeywordOnly)
            throw new InvalidOperationException(NoEmbeddingService.NotConfiguredMessage);

        var missing = (await _vectorStore.GetByDocumentIdAsync(documentId, cancellationToken))
            .Where(c => c.Embedding is not { Length: > 0 })
            .ToList();
        if (missing.Count == 0)
            return 0;

        var embedded = await GenerateEmbeddingsAsync(missing, cancellationToken);
        await _vectorStore.StoreBatchAsync(embedded, cancellationToken);
        await InvalidateReadCachesAsync(documentId, cancellationToken);
        return embedded.Count;
    }

    /// <summary>
    /// Deletes a document: its chunks from the vector store and the keyword index, and its record.
    /// </summary>
    /// <returns>Whether anything of the document was removed — its chunks or its record.</returns>
    public async Task<bool> DeleteByDocumentIdAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        LogDeletingDocument(_logger, documentId);

        try
        {
            // Delete chunks from vector store. The keyword index is dropped first because its own
            // record of which chunks belong to the document is what makes the removal possible.
            await DeleteKeywordByDocumentIdAsync(documentId, cancellationToken);
            var removedChunks = await _vectorStore.DeleteByDocumentIdAsync(documentId, cancellationToken);

            // Delete document from repository. The repository is process-local, so after a restart it no longer knows
            // a document the store still held: the answer is whether either of them removed something.
            var removedRecord = await _documentRepository.DeleteAsync(documentId, cancellationToken);
            var deleted = removedChunks || removedRecord;
            await InvalidateReadCachesAsync(documentId, cancellationToken);

            if (deleted)
            {
                LogSuccessfullyDeletedDocument(_logger, documentId);
            }
            else
            {
                LogDocumentNotFound(_logger, documentId);
            }

            return deleted;
        }
        catch (Exception ex)
        {
            LogFailedToDeleteDocument(_logger, ex, documentId);
            throw;
        }
    }

    /// <summary>
    /// 청크 삭제
    /// </summary>
    public async Task<bool> DeleteChunkAsync(
        string chunkId,
        CancellationToken cancellationToken = default)
    {
        LogDeletingChunk(_logger, chunkId);

        if (HasKeywordIndex)
            await _keywordSearchService!.DeleteChunkAsync(chunkId, cancellationToken);

        var deleted = await _vectorStore.DeleteAsync(chunkId, cancellationToken);
        // The chunk's document id is not known here, so no single cached document can be dropped; the generation
        // change covers search results, and a cached document is only a copy of what GetDocumentAsync assembled.
        await InvalidateReadCachesAsync(documentId: null, cancellationToken);
        return deleted;
    }

    /// <summary>
    /// 인덱스 재구성
    /// </summary>
    public async Task ReindexDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        LogReindexingDocument(_logger, documentId);

        // Get document
        var document = await _documentRepository.GetByIdAsync(documentId, cancellationToken);
        if (document == null)
            throw new InvalidOperationException($"Document {documentId} not found");

        // Get existing chunks
        var chunks = await _vectorStore.GetByDocumentIdAsync(documentId, cancellationToken);
        
        // Regenerate embeddings
        var chunksList = chunks.ToList();
        chunksList = await GenerateEmbeddingsAsync(chunksList, cancellationToken);

        // Re-store the chunks as the document's new version
        await ReplaceChunksAsync([documentId], chunksList, cancellationToken);
        await InvalidateReadCachesAsync(documentId, cancellationToken);

        LogSuccessfullyReindexedDocument(_logger, documentId, chunksList.Count);
    }

    /// <summary>
    /// Makes the retriever's cached reads unable to outlive this write: every cached search result (they are keyed
    /// under a generation this replaces) and the cached copy of <paramref name="documentId"/>. Runs after the write
    /// has completed, so a search that starts afterwards cannot be answered from before it.
    /// </summary>
    private async Task InvalidateReadCachesAsync(string? documentId, CancellationToken cancellationToken)
    {
        if (_cacheService != null)
            await SearchCacheKeys.InvalidateAsync(_cacheService, documentId, cancellationToken);
    }

    /// <summary>
    /// 인덱싱 통계
    /// </summary>
    public async Task<IndexingStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var docCount = await _documentRepository.GetCountAsync(cancellationToken);
        var chunkCount = await _vectorStore.CountAsync(cancellationToken);

        return new IndexingStatistics
        {
            TotalDocuments = docCount,
            TotalChunks = chunkCount,
            AverageChunksPerDocument = docCount > 0 ? (double)chunkCount / docCount : 0,
            DefaultChunkSize = _options.ChunkSize,
            DefaultChunkOverlap = _options.ChunkOverlap,
            EmbeddingModel = IsKeywordOnly ? "none (keyword-only)" : _embeddingService.GetType().Name
        };
    }

    /// <summary>
    /// Attaches an embedding to every chunk. The chunks are mutated in place rather than rebuilt: three
    /// hand-written copies of the entity used to live here (batch, parallel fallback, sequential fallback),
    /// each with a different field list, and all of them dropped <see cref="DocumentChunkEntity.TotalChunks"/>
    /// — so every chunk the SDK ever stored had TotalChunks = 0 regardless of what the caller built.
    /// </summary>
    private async Task<List<DocumentChunkEntity>> GenerateEmbeddingsAsync(
        List<DocumentChunkEntity> chunks,
        CancellationToken cancellationToken)
    {
        // Keyword-only: the chunks are stored without vectors; nothing writes a placeholder.
        if (chunks.Count == 0 || IsKeywordOnly) return chunks;

        // 배치 임베딩 API 사용 (성능 최적화)
        try
        {
            var texts = chunks.Select(c => c.Content).ToList();
            LogExtractedTextsFromChunks(_logger, texts.Count);

            for (int i = 0; i < texts.Count; i++)
            {
                var tokens = texts[i].Length / 4;
                LogTextDetails(_logger, i, texts.Count, texts[i].Length, tokens);

                if (tokens > 8000)
                {
                    LogTextExceedsLimit(_logger, i, tokens);
                }
            }

            LogCallingGenerateEmbeddingsBatch(_logger, texts.Count);
            var embeddings = await _embeddingService.GenerateEmbeddingsBatchAsync(texts, cancellationToken);
            var embeddingArray = embeddings.ToArray();

            for (int i = 0; i < chunks.Count && i < embeddingArray.Length; i++)
            {
                chunks[i].Embedding = embeddingArray[i];
            }

            LogBatchEmbeddingCompleted(_logger, chunks.Count);
            return chunks;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogBatchEmbeddingFailed(_logger, ex);

            // Fallback: 개별 임베딩 생성
            if (_options.ParallelEmbedding && chunks.Count > 1)
            {
                var semaphore = new SemaphoreSlim(_options.MaxParallelEmbedding);
                var tasks = chunks.Select(async chunk =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        chunk.Embedding = await _embeddingService.GenerateEmbeddingAsync(chunk.Content, cancellationToken);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);
                return chunks;
            }

            foreach (var chunk in chunks)
            {
                chunk.Embedding = await _embeddingService.GenerateEmbeddingAsync(chunk.Content, cancellationToken);
            }
            return chunks;
        }
    }

    /// <summary>
    /// Adds chunks to the keyword index when one is registered and <see cref="IndexerOptions.IndexKeyword"/>
    /// is on. A keyword-index failure does not fail the indexing call — the vector store write has
    /// already succeeded and rolling it back would lose the document — but it is logged as an error
    /// because the document is now searchable by vector only.
    /// </summary>
    /// <summary>
    /// Replaces the chunks of <paramref name="documentIds"/> with <paramref name="chunks"/> in the vector store and then
    /// in the keyword index. Each store replaces atomically where it can; if the keyword write fails after the vector
    /// write succeeded (keyword-only, where it throws), calling the same operation again replaces both.
    /// </summary>
    private async Task ReplaceChunksAsync(
        IReadOnlyCollection<string> documentIds,
        List<DocumentChunkEntity> chunks,
        CancellationToken cancellationToken)
    {
        await _vectorStore.ReplaceDocumentsAsync(documentIds, chunks, cancellationToken);
        await ReplaceKeywordAsync(documentIds, chunks, cancellationToken);
    }

    private async Task ReplaceKeywordAsync(
        IReadOnlyCollection<string> documentIds,
        List<DocumentChunkEntity> chunks,
        CancellationToken cancellationToken)
    {
        if (!MaintainsKeywordIndex)
        {
            if (chunks.Count > 0 && IsKeywordOnly)
                throw new InvalidOperationException(NoKeywordIndexMessage);

            // An index kept while IndexKeyword is off still holds the previous version, which would go on matching.
            foreach (var documentId in documentIds)
                await DeleteKeywordByDocumentIdAsync(documentId, cancellationToken);
            return;
        }

        try
        {
            await _keywordSearchService!.ReplaceDocumentsAsync(documentIds, chunks, cancellationToken);
            LogKeywordIndexUpdated(_logger, chunks.Count);
        }
        catch (Exception ex) when ((ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested) && (!IsKeywordOnly))
        {
            // Same policy as IndexKeywordAsync: with vectors the keyword leg is a best effort.
            LogKeywordIndexUpdateFailed(_logger, ex, chunks.Count);
        }
    }

    private const string NoKeywordIndexMessage =
        "This FluxIndex context has no embedding service and no keyword index to write (none registered, or " +
        "IndexerOptions.IndexKeyword is false), so the indexed chunks could not be found by any search. " +
        "Register an embedding service or keep keyword indexing on.";

    private async Task IndexKeywordAsync(
        List<DocumentChunkEntity> chunks,
        CancellationToken cancellationToken)
    {
        if (chunks.Count == 0)
            return;

        if (!MaintainsKeywordIndex)
        {
            // Without vectors the keyword index is the only search index: skipping it would store chunks nothing finds.
            if (IsKeywordOnly)
            {
                throw new InvalidOperationException(NoKeywordIndexMessage);
            }
            return;
        }

        try
        {
            await _keywordSearchService!.IndexChunksAsync(chunks, cancellationToken);
            LogKeywordIndexUpdated(_logger, chunks.Count);
        }
        catch (Exception ex) when ((ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested) && (!IsKeywordOnly))
        {
            // With vectors the chunk is already searchable through the vector store; the keyword leg is a best effort.
            // Keyword-only, this index is the document's only way to be found, so its failure is the caller's to see.
            LogKeywordIndexUpdateFailed(_logger, ex, chunks.Count);
        }
    }

    /// <summary>
    /// Removes a document's chunks from the keyword index. Skipping this leaves postings behind that
    /// still match queries — a deleted document reappearing through the keyword leg alone. This runs
    /// whenever a keyword index exists, including when <see cref="IndexerOptions.IndexKeyword"/> is
    /// off, because a persistent index outlives the option.
    /// </summary>
    private async Task DeleteKeywordByDocumentIdAsync(
        string documentId,
        CancellationToken cancellationToken)
    {
        if (!HasKeywordIndex)
            return;

        try
        {
            await _keywordSearchService!.DeleteByDocumentIdAsync(documentId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogKeywordIndexDeleteFailed(_logger, ex, documentId);
        }
    }

    private static DocumentChunkEntity ConvertToEntityChunk(DocumentChunkModel modelChunk)
    {
        var chunk = DocumentChunkEntity.Create(
            modelChunk.DocumentId,
            modelChunk.Content,
            modelChunk.ChunkIndex,
            modelChunk.TotalChunks
        );

        if (modelChunk.Metadata.Count != 0)
            chunk.Metadata = new Dictionary<string, object>(modelChunk.Metadata);

        return chunk;
    }

    /// <summary>
    /// Copies caller-supplied document-level metadata onto a chunk as a base layer.
    /// The search and delete filter contracts match against chunk metadata, so metadata that only
    /// reaches the <see cref="Document"/> entity is invisible to every filter. The chunk's own
    /// metadata is the more specific scope and wins on key collision.
    /// </summary>
    /// <summary>
    /// Metadata-extraction settings for one call: the builder-level <see cref="IndexerOptions.CustomOptions"/>
    /// as defaults, with the caller's <see cref="IndexingOptions.CustomOptions"/> overlaid on top.
    /// </summary>
    internal IndexingOptions ResolveMetadataOptions(IndexingOptions? perCall)
    {
        var resolved = new IndexingOptions();
        if (_options.CustomOptions != null)
        {
            foreach (var (key, value) in _options.CustomOptions)
                resolved.CustomOptions[key] = value;
        }
        if (perCall?.CustomOptions != null)
        {
            foreach (var (key, value) in perCall.CustomOptions)
                resolved.CustomOptions[key] = value;
        }
        return resolved;
    }

    /// <summary>
    /// Writes <see cref="Document.FileName"/> into each chunk's <c>file_name</c> metadata when the
    /// chunk does not already carry one. The relational keyword index scores that key as a field
    /// (<see cref="Core.Application.Services.KeywordSearch.KeywordFieldOptions"/>), so a document
    /// indexed through the SDK is retrievable by its file name without the caller tagging every
    /// chunk; a value the caller set is never overwritten.
    /// </summary>
    private static void CarryFileNameIntoChunks(Document document, List<DocumentChunkEntity> chunks)
    {
        if (string.IsNullOrWhiteSpace(document.FileName))
            return;

        foreach (var chunk in chunks)
        {
            chunk.Metadata ??= new Dictionary<string, object>();
            chunk.Metadata.TryAdd(Core.Application.Services.KeywordSearch.KeywordFieldOptions.FileNameKey, document.FileName);
        }
    }

    private static void MergeDocumentMetadataIntoChunk(
        DocumentChunkEntity chunk,
        Dictionary<string, object>? documentMetadata)
    {
        if (documentMetadata is null || documentMetadata.Count == 0)
            return;

        chunk.Metadata ??= new Dictionary<string, object>();

        foreach (var (key, value) in documentMetadata)
        {
            if (!chunk.Metadata.ContainsKey(key))
                chunk.Metadata[key] = value;
        }
    }

    private static DocumentChunkModel ConvertToModelChunk(DocumentChunkEntity entityChunk)
    {
        return DocumentChunkModel.Create(
            entityChunk.DocumentId,
            entityChunk.Content,
            entityChunk.ChunkIndex,
            entityChunk.TotalChunks,
            entityChunk.Embedding,
            0f, // score
            entityChunk.TokenCount,
            entityChunk.Metadata
        );
    }

    private static int EstimateTokenCount(string text)
    {
        // Simple estimation: ~4 characters per token
        return text.Length / 4;
    }

    /// <summary>
    /// Update document metadata (supports user corrections to AI-extracted metadata)
    /// </summary>
    /// <param name="documentId">Document ID</param>
    /// <param name="metadata">Updated metadata dictionary</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task UpdateDocumentMetadataAsync(
        string documentId,
        Dictionary<string, object> metadata,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be empty", nameof(documentId));
        if (metadata == null || metadata.Count == 0)
            throw new ArgumentException("Metadata cannot be null or empty", nameof(metadata));

        LogUpdatingMetadata(_logger, documentId);

        // Get existing document
        var document = await _documentRepository.GetByIdAsync(documentId, cancellationToken);
        if (document == null)
        {
            throw new InvalidOperationException($"Document with ID '{documentId}' not found");
        }

        // Update metadata
        foreach (var (key, value) in metadata)
        {
            document.SetMetadata(key, value);
        }

        // Mark metadata source as User-corrected
        document.SetMetadata("MetadataSource", "UserCorrected");
        document.SetMetadata("MetadataLastUpdated", DateTime.UtcNow);

        // Save updated document
        await _documentRepository.UpdateAsync(document, cancellationToken);
        await InvalidateReadCachesAsync(documentId, cancellationToken);

        LogMetadataUpdated(_logger, documentId);
    }

    /// <summary>
    /// Correct AI-extracted metadata for a document
    /// </summary>
    /// <param name="documentId">Document ID</param>
    /// <param name="correctedMetadata">Corrected ExtractedMetadata instance</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task CorrectExtractedMetadataAsync(
        string documentId,
        ExtractedMetadata correctedMetadata,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be empty", nameof(documentId));
        ArgumentNullException.ThrowIfNull(correctedMetadata);

        LogCorrectingExtractedMetadata(_logger, documentId);

        // Get existing document
        var document = await _documentRepository.GetByIdAsync(documentId, cancellationToken);
        if (document == null)
        {
            throw new InvalidOperationException($"Document with ID '{documentId}' not found");
        }

        // Update ExtractedMetadata
        correctedMetadata.Source = MetadataSource.User;
        correctedMetadata.ExtractionMethod = "User-Corrected";
        correctedMetadata.OverallConfidence = 1.0f; // User corrections are 100% confident

        document.SetMetadata("AIExtractedMetadata", correctedMetadata);
        document.SetMetadata("MetadataExtractionMethod", "User-Corrected");
        document.SetMetadata("MetadataConfidence", 1.0f);
        document.SetMetadata("MetadataLastUpdated", DateTime.UtcNow);

        // Save updated document
        await _documentRepository.UpdateAsync(document, cancellationToken);
        await InvalidateReadCachesAsync(documentId, cancellationToken);

        LogExtractedMetadataCorrected(_logger, documentId);
    }

    /// <summary>
    /// Get current extracted metadata for a document
    /// </summary>
    /// <param name="documentId">Document ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>ExtractedMetadata if available, null otherwise</returns>
    public async Task<ExtractedMetadata?> GetExtractedMetadataAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be empty", nameof(documentId));

        var document = await _documentRepository.GetByIdAsync(documentId, cancellationToken);
        if (document == null)
        {
            return null;
        }

        if (document.Metadata.TryGetValue("AIExtractedMetadata", out var metadataObj) &&
            metadataObj is ExtractedMetadata extractedMetadata)
        {
            return extractedMetadata;
        }

        return null;
    }

    /// <summary>
    /// 여러 문서의 메타데이터를 배치로 추출
    /// </summary>
    /// <param name="documents">문서 목록 (DocumentId, Content)</param>
    /// <param name="schema">메타데이터 스키마</param>
    /// <param name="strategy">추출 전략</param>
    /// <param name="maxConcurrency">최대 병렬 처리 수</param>
    /// <param name="progressCallback">진행 상황 콜백</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>배치 추출 결과</returns>
    public async Task<BatchMetadataExtractionResult> ExtractMetadataBatchAsync(
        IEnumerable<(string DocumentId, string Content)> documents,
        MetadataSchema schema = MetadataSchema.General,
        MetadataExtractionStrategy strategy = MetadataExtractionStrategy.Smart,
        int maxConcurrency = 4,
        IProgress<BatchMetadataExtractionProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (_metadataExtractor == null)
        {
            throw new InvalidOperationException(
                "Metadata extractor is not configured. Register an IMetadataExtractor via ConfigureServices(...) on the builder.");
        }

        var docList = documents.ToList();
        if (docList.Count == 0)
        {
            throw new ArgumentException("Document list cannot be empty", nameof(documents));
        }

        LogStartingBatchMetadataExtraction(_logger, docList.Count);

        // Create batch request
        var request = new BatchMetadataExtractionRequest
        {
            MaxConcurrency = maxConcurrency,
            ContinueOnError = true,
            Items = docList.Select(doc => new MetadataExtractionItem
            {
                DocumentId = doc.DocumentId,
                Content = doc.Content,
                Schema = schema,
                Strategy = strategy
            }).ToList()
        };

        // Extract metadata options from IndexerOptions
        var indexingOptions = ResolveMetadataOptions(null);

        var minConfidence = indexingOptions.GetMinMetadataConfidence();
        var customPrompt = indexingOptions.GetCustomMetadataPrompt();

        var extractionOptions = new AIMetadataExtractionOptions
        {
            Strategy = strategy,
            MinConfidence = minConfidence,
            CustomPrompt = customPrompt
        };

        // Call batch extraction with progress reporting
        var result = await _metadataExtractor.ExtractBatchWithProgressAsync(
            request,
            extractionOptions,
            progressCallback,
            cancellationToken);

        LogBatchMetadataExtractionCompleted(_logger, result.SuccessfulItems, result.TotalItems);

        return result;
    }

    /// <summary>
    /// Indexes many documents given as text, writing them together: each document is split, its metadata extracted (when
    /// <paramref name="options"/> asks for it) and its chunks embedded on its own, then every document is written through
    /// one replacement per store — one transaction each on the sqlite-vec store and the relational keyword indexes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A document that cannot be prepared (empty content or id, an extraction or embedding failure) is counted in
    /// <see cref="BatchIndexingResult.FailedDocuments"/> with its error in <see cref="BatchIndexingResult.Results"/>, and
    /// the rest are written. The write is one replacement per store: the sqlite-vec store and a relational keyword index
    /// each hold either the whole batch or none of it. When it throws, calling again with the same documents is safe — it
    /// replaces them again rather than adding a second copy.
    /// </para>
    /// <para>
    /// As with <see cref="IndexDocumentAsync(string, string, Dictionary{string, object}?, CancellationToken)"/>, an id that
    /// is already indexed is replaced. When an id appears more than once in the batch, its last occurrence is written.
    /// No AI service is needed — without one this is the bulk path of a keyword-only index.
    /// </para>
    /// </remarks>
    /// <param name="documents">문서 목록 (DocumentId, Content, Metadata)</param>
    /// <param name="options">인덱싱 옵션 — 문서마다 같은 옵션이 적용된다</param>
    /// <param name="progressCallback">진행 상황 콜백 (문서 준비마다 한 번, 끝에 한 번)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>배치 인덱싱 결과</returns>
    public async Task<BatchIndexingResult> IndexDocumentsBatchAsync(
        IEnumerable<(string DocumentId, string Content, Dictionary<string, object>? Metadata)> documents,
        IndexingOptions? options = null,
        IProgress<BatchProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        var docList = documents.ToList();
        if (docList.Count == 0)
        {
            throw new ArgumentException("Document list cannot be empty", nameof(documents));
        }

        options ??= new IndexingOptions();

        LogStartingBatchDocumentIndexing(_logger, docList.Count);

        var result = new BatchIndexingResult
        {
            TotalDocuments = docList.Count
        };

        var startTime = DateTime.UtcNow;

        // Report initial progress
        progressCallback?.Report(new BatchProgress
        {
            BatchId = result.BatchId,
            TotalItems = docList.Count,
            Status = "Processing",
            Message = "Starting batch document indexing..."
        });

        var prepared = new List<Document>(docList.Count);
        var completed = 0;
        void Report(string documentId, Exception? error)
        {
            completed++;
            if (error == null)
            {
                result.SuccessfulDocuments++;
            }
            else
            {
                LogFailedToIndexDocumentWarning(_logger, error, documentId);
                result.FailedDocuments++;
                result.Results.Add(new IndexingResult
                {
                    DocumentId = documentId,
                    Success = false,
                    Errors = [new IndexingError { Message = error.Message, ErrorCode = "INDEXING_FAILED" }]
                });
            }

            progressCallback?.Report(new BatchProgress
            {
                BatchId = result.BatchId,
                CurrentItem = completed,
                TotalItems = docList.Count,
                SuccessfulItems = result.SuccessfulDocuments,
                FailedItems = result.FailedDocuments,
                Status = "Processing",
                Message = error == null
                    ? $"Prepared document {completed}/{docList.Count}: {documentId}"
                    : $"Failed to index document {completed}/{docList.Count}: {documentId}"
            });
        }

        foreach (var (documentId, content, metadata) in docList)
        {
            try
            {
                prepared.Add(CreateChunkedDocument(content, documentId, metadata));
            }
            catch (ArgumentException ex)
            {
                Report(documentId, ex);
            }
        }

        // Preparation (metadata extraction, embedding) runs one document at a time, as the per-document loop did.
        await IndexDocumentsCoreAsync(prepared, options, parallelism: 1, (doc, error) => Report(doc.Id, error), cancellationToken);

        result.TotalProcessingTime = DateTime.UtcNow - startTime;

        // Report final progress
        progressCallback?.Report(new BatchProgress
        {
            BatchId = result.BatchId,
            CurrentItem = docList.Count,
            TotalItems = docList.Count,
            SuccessfulItems = result.SuccessfulDocuments,
            FailedItems = result.FailedDocuments,
            Status = "Completed",
            Message = $"Batch indexing completed: {result.SuccessfulDocuments} succeeded, {result.FailedDocuments} failed"
        });

        LogBatchDocumentIndexingCompleted(_logger, result.SuccessfulDocuments, result.TotalDocuments, result.TotalProcessingTime.TotalMilliseconds);

        return result;
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Indexing document {DocumentId}, JobId: {JobId}")]
    private static partial void LogIndexingDocument(ILogger logger, string documentId, string jobId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Extracting AI metadata for document {DocumentId}")]
    private static partial void LogExtractingAIMetadata(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "AI metadata extracted: confidence={Confidence}, topics={TopicCount}")]
    private static partial void LogAIMetadataExtracted(ILogger logger, float confidence, int topicCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to extract AI metadata for document {DocumentId}, continuing without it")]
    private static partial void LogFailedToExtractAIMetadata(ILogger logger, Exception exception, string documentId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Document {DocumentId} has no chunks")]
    private static partial void LogDocumentHasNoChunks(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Converting {Count} DocumentChunks to entities")]
    private static partial void LogConvertingChunksToEntities(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Chunk {Index}/{Total}: {Length} chars (~{Tokens} tokens)")]
    private static partial void LogChunkDetails(ILogger logger, int index, int total, int length, int tokens);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Chunk {Index} exceeds token limit (~{Tokens} tokens) - splitting into smaller chunks")]
    private static partial void LogChunkExceedsTokenLimit(ILogger logger, int index, int tokens);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Created sub-chunk {SubIndex}/{SubTotal}: {Length} chars (~{Tokens} tokens)")]
    private static partial void LogSubChunkCreated(ILogger logger, int subIndex, int subTotal, int length, int tokens);

    [LoggerMessage(Level = LogLevel.Information, Message = "Split oversized chunk {Index} into {SubChunks} smaller chunks")]
    private static partial void LogSplitOversizedChunk(ILogger logger, int index, int subChunks);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Total entity chunks after splitting: {EntityCount} (original: {OriginalCount})")]
    private static partial void LogTotalEntityChunksAfterSplitting(ILogger logger, int entityCount, int originalCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Converted {Count} entities, calling GenerateEmbeddingsAsync")]
    private static partial void LogCallingGenerateEmbeddings(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Building GraphRAG index for document {DocumentId}")]
    private static partial void LogBuildingGraphRAGIndex(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "GraphRAG index built successfully for document {DocumentId}")]
    private static partial void LogGraphRAGIndexBuilt(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully indexed document {DocumentId} with {ChunkCount} chunks")]
    private static partial void LogSuccessfullyIndexedDocument(ILogger logger, string documentId, int chunkCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to index document {DocumentId}")]
    private static partial void LogFailedToIndexDocument(ILogger logger, Exception exception, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Indexing chunks as document: {DocumentId}")]
    private static partial void LogIndexingChunksAsDocument(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Batch indexing {Count} documents, BatchId: {BatchId}")]
    private static partial void LogBatchIndexing(ILogger logger, int count, string batchId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to index document {DocumentId} in batch {BatchId}")]
    private static partial void LogFailedToIndexDocumentInBatch(ILogger logger, Exception exception, string documentId, string batchId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Batch indexing completed. Indexed {Success}/{Total} documents (BatchId: {BatchId})")]
    private static partial void LogBatchIndexingCompleted(ILogger logger, int success, int total, string batchId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Updating document: {DocumentId}")]
    private static partial void LogUpdatingDocument(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully updated document {DocumentId}")]
    private static partial void LogSuccessfullyUpdatedDocument(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Adding {Count} chunks to document: {DocumentId}")]
    private static partial void LogAddingChunks(ILogger logger, int count, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully added {Count} chunks to document {DocumentId}")]
    private static partial void LogSuccessfullyAddedChunks(ILogger logger, int count, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleting document: {DocumentId}")]
    private static partial void LogDeletingDocument(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully deleted document {DocumentId}")]
    private static partial void LogSuccessfullyDeletedDocument(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Document {DocumentId} not found")]
    private static partial void LogDocumentNotFound(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to delete document {DocumentId}")]
    private static partial void LogFailedToDeleteDocument(ILogger logger, Exception exception, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleting chunk: {ChunkId}")]
    private static partial void LogDeletingChunk(ILogger logger, string chunkId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reindexing document: {DocumentId}")]
    private static partial void LogReindexingDocument(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully reindexed document {DocumentId} with {ChunkCount} chunks")]
    private static partial void LogSuccessfullyReindexedDocument(ILogger logger, string documentId, int chunkCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Extracted {Count} texts from chunks for embedding")]
    private static partial void LogExtractedTextsFromChunks(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Text {Index}/{Total}: {Length} chars (~{Tokens} tokens)")]
    private static partial void LogTextDetails(ILogger logger, int index, int total, int length, int tokens);

    [LoggerMessage(Level = LogLevel.Error, Message = "Text {Index} exceeds limit: ~{Tokens} tokens")]
    private static partial void LogTextExceedsLimit(ILogger logger, int index, int tokens);

    [LoggerMessage(Level = LogLevel.Information, Message = "Calling GenerateEmbeddingsBatchAsync with {Count} texts")]
    private static partial void LogCallingGenerateEmbeddingsBatch(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Batch embedding completed: {Count} chunks")]
    private static partial void LogBatchEmbeddingCompleted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Batch embedding failed, falling back to individual processing")]
    private static partial void LogBatchEmbeddingFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Updating metadata for document {DocumentId}")]
    private static partial void LogUpdatingMetadata(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Metadata updated successfully for document {DocumentId}")]
    private static partial void LogMetadataUpdated(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Correcting extracted metadata for document {DocumentId}")]
    private static partial void LogCorrectingExtractedMetadata(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Extracted metadata corrected successfully for document {DocumentId}")]
    private static partial void LogExtractedMetadataCorrected(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting batch metadata extraction for {Count} documents")]
    private static partial void LogStartingBatchMetadataExtraction(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Batch metadata extraction completed: {Successful}/{Total} documents succeeded")]
    private static partial void LogBatchMetadataExtractionCompleted(ILogger logger, int successful, int total);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting batch document indexing for {Count} documents")]
    private static partial void LogStartingBatchDocumentIndexing(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to index document: {DocumentId}")]
    private static partial void LogFailedToIndexDocumentWarning(ILogger logger, Exception exception, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Batch document indexing completed: {Successful}/{Total} documents succeeded, Time={Time}ms")]
    private static partial void LogBatchDocumentIndexingCompleted(ILogger logger, int successful, int total, double time);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Keyword index updated with {ChunkCount} chunks")]
    private static partial void LogKeywordIndexUpdated(ILogger logger, int chunkCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to add {ChunkCount} chunks to the keyword index; these chunks are searchable by vector similarity only and hybrid search will under-report keyword matches for them")]
    private static partial void LogKeywordIndexUpdateFailed(ILogger logger, Exception exception, int chunkCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to remove document {DocumentId} from the keyword index; stale postings may still match queries")]
    private static partial void LogKeywordIndexDeleteFailed(ILogger logger, Exception exception, string documentId);

    #endregion
}

/// <summary>
/// Indexer 옵션 — <see cref="FluxIndexContextBuilder.WithIndexerOptions"/> / <see cref="FluxIndexContextBuilder.WithChunking"/> 로 설정한다.
/// </summary>
public class IndexerOptions
{
    /// <summary>
    /// <see cref="Indexer.IndexDocumentAsync(string, string, Dictionary{string, object}?, CancellationToken)"/> 가 문자열을 나누는
    /// 최대 청크 길이(문자 수 — 토큰이 아니다). 문장 → 문단 → 단어 경계 순으로 자연 경계를 찾아 자른다.
    /// 이미 나뉜 <c>Document</c> 를 받는 오버로드에는 영향이 없다. <see cref="IndexingStatistics"/> 에도 보고된다.
    /// </summary>
    public int ChunkSize { get; set; } = 512;
    /// <summary>
    /// 연속한 청크가 공유하는 문자 수. <see cref="ChunkSize"/> 보다 작아야 한다 — 같거나 크면 첫 분할에서
    /// <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    public int ChunkOverlap { get; set; } = 64;
    public bool ParallelEmbedding { get; set; } = true;
    public int MaxParallelEmbedding { get; set; } = 4;

    /// <summary>
    /// Whether indexing adds documents to the keyword (sparse) index used by the hybrid keyword leg.
    /// Default <c>true</c>.
    /// <para>
    /// Setting this to <c>false</c> means keyword and hybrid search have nothing to match against:
    /// keyword search returns no results and hybrid search ranks by vector similarity alone. It is
    /// not a compatibility switch — before 0.22.0 the keyword leg scanned chunk content instead, so
    /// it did return results. Turn this off only when you do not use keyword or hybrid search.
    /// </para>
    /// <para>
    /// Deletions are still propagated to the keyword index while it exists, so turning this off
    /// cannot leave postings for deleted documents behind.
    /// </para>
    /// Has no effect when no <c>IKeywordSearchService</c> is registered.
    /// </summary>
    public bool IndexKeyword { get; set; } = true;

    public Dictionary<string, object>? CustomOptions { get; set; }
}

/// <summary>
/// 인덱싱 통계
/// </summary>
public class IndexingStatistics
{
    public int TotalDocuments { get; set; }
    public int TotalChunks { get; set; }
    public double AverageChunksPerDocument { get; set; }
    public int DefaultChunkSize { get; set; }
    public int DefaultChunkOverlap { get; set; }
    public string EmbeddingModel { get; set; } = string.Empty;
}