using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace FluxIndex.Stack.Api.Observability;

/// <summary>
/// Custom RAG metrics for FluxIndex Stack observability.
/// Provides metrics for document indexing, search, and retrieval operations.
/// </summary>
public sealed class RagMetrics : IDisposable
{
    public const string MeterName = "FluxIndex.Stack.RAG";
    public const string ActivitySourceName = "FluxIndex.Stack.RAG";

    private readonly Meter _meter;
    private bool _disposed;

    // Activity source for distributed tracing
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");

    // Indexing metrics
    public Counter<long> DocumentsIndexed { get; }
    public Counter<long> ChunksCreated { get; }
    public Counter<long> IndexingErrors { get; }
    public Histogram<double> IndexingDuration { get; }
    public Histogram<long> ChunksPerDocument { get; }

    // Search metrics
    public Counter<long> SearchRequests { get; }
    public Counter<long> SearchErrors { get; }
    public Histogram<double> SearchDuration { get; }
    public Histogram<long> ResultsReturned { get; }
    public Histogram<double> TopResultScore { get; }

    // Embedding metrics
    public Counter<long> EmbeddingRequests { get; }
    public Counter<long> EmbeddingErrors { get; }
    public Histogram<double> EmbeddingDuration { get; }
    public Histogram<long> TokensProcessed { get; }

    // Reranking metrics
    public Counter<long> RerankingRequests { get; }
    public Counter<long> RerankingErrors { get; }
    public Histogram<double> RerankingDuration { get; }

    // HyDE metrics
    public Counter<long> HyDERequests { get; }
    public Histogram<double> HyDEDuration { get; }
    public Histogram<long> HypotheticalDocumentsGenerated { get; }

    // Cache metrics
    public Counter<long> CacheHits { get; }
    public Counter<long> CacheMisses { get; }
    public UpDownCounter<long> CacheSize { get; }

    // Active operations gauge
    public UpDownCounter<long> ActiveIndexingJobs { get; }
    public UpDownCounter<long> ActiveSearchRequests { get; }

    public RagMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName, "1.0.0");

        // Indexing metrics
        DocumentsIndexed = _meter.CreateCounter<long>(
            "rag.documents.indexed",
            unit: "{documents}",
            description: "Total number of documents indexed");

        ChunksCreated = _meter.CreateCounter<long>(
            "rag.chunks.created",
            unit: "{chunks}",
            description: "Total number of chunks created during indexing");

        IndexingErrors = _meter.CreateCounter<long>(
            "rag.indexing.errors",
            unit: "{errors}",
            description: "Total number of indexing errors");

        IndexingDuration = _meter.CreateHistogram<double>(
            "rag.indexing.duration",
            unit: "ms",
            description: "Duration of document indexing operations");

        ChunksPerDocument = _meter.CreateHistogram<long>(
            "rag.chunks.per_document",
            unit: "{chunks}",
            description: "Number of chunks created per document");

        // Search metrics
        SearchRequests = _meter.CreateCounter<long>(
            "rag.search.requests",
            unit: "{requests}",
            description: "Total number of search requests");

        SearchErrors = _meter.CreateCounter<long>(
            "rag.search.errors",
            unit: "{errors}",
            description: "Total number of search errors");

        SearchDuration = _meter.CreateHistogram<double>(
            "rag.search.duration",
            unit: "ms",
            description: "Duration of search operations");

        ResultsReturned = _meter.CreateHistogram<long>(
            "rag.search.results_returned",
            unit: "{results}",
            description: "Number of results returned per search");

        TopResultScore = _meter.CreateHistogram<double>(
            "rag.search.top_score",
            unit: "{score}",
            description: "Score of the top search result");

        // Embedding metrics
        EmbeddingRequests = _meter.CreateCounter<long>(
            "rag.embedding.requests",
            unit: "{requests}",
            description: "Total number of embedding requests");

        EmbeddingErrors = _meter.CreateCounter<long>(
            "rag.embedding.errors",
            unit: "{errors}",
            description: "Total number of embedding errors");

        EmbeddingDuration = _meter.CreateHistogram<double>(
            "rag.embedding.duration",
            unit: "ms",
            description: "Duration of embedding generation");

        TokensProcessed = _meter.CreateHistogram<long>(
            "rag.embedding.tokens",
            unit: "{tokens}",
            description: "Number of tokens processed for embedding");

        // Reranking metrics
        RerankingRequests = _meter.CreateCounter<long>(
            "rag.reranking.requests",
            unit: "{requests}",
            description: "Total number of reranking requests");

        RerankingErrors = _meter.CreateCounter<long>(
            "rag.reranking.errors",
            unit: "{errors}",
            description: "Total number of reranking errors");

        RerankingDuration = _meter.CreateHistogram<double>(
            "rag.reranking.duration",
            unit: "ms",
            description: "Duration of reranking operations");

        // HyDE metrics
        HyDERequests = _meter.CreateCounter<long>(
            "rag.hyde.requests",
            unit: "{requests}",
            description: "Total number of HyDE (Hypothetical Document Embedding) requests");

        HyDEDuration = _meter.CreateHistogram<double>(
            "rag.hyde.duration",
            unit: "ms",
            description: "Duration of HyDE generation");

        HypotheticalDocumentsGenerated = _meter.CreateHistogram<long>(
            "rag.hyde.documents_generated",
            unit: "{documents}",
            description: "Number of hypothetical documents generated per HyDE request");

        // Cache metrics
        CacheHits = _meter.CreateCounter<long>(
            "rag.cache.hits",
            unit: "{hits}",
            description: "Total number of cache hits");

        CacheMisses = _meter.CreateCounter<long>(
            "rag.cache.misses",
            unit: "{misses}",
            description: "Total number of cache misses");

        CacheSize = _meter.CreateUpDownCounter<long>(
            "rag.cache.size",
            unit: "{entries}",
            description: "Current number of entries in the cache");

        // Active operations
        ActiveIndexingJobs = _meter.CreateUpDownCounter<long>(
            "rag.indexing.active_jobs",
            unit: "{jobs}",
            description: "Number of currently active indexing jobs");

        ActiveSearchRequests = _meter.CreateUpDownCounter<long>(
            "rag.search.active_requests",
            unit: "{requests}",
            description: "Number of currently active search requests");
    }

    /// <summary>
    /// Starts a new activity for tracing a RAG operation.
    /// </summary>
    public static Activity? StartActivity(string operationName, ActivityKind kind = ActivityKind.Internal)
    {
        return ActivitySource.StartActivity(operationName, kind);
    }

    /// <summary>
    /// Records indexing metrics for a completed indexing operation.
    /// </summary>
    public void RecordIndexing(
        long chunkCount,
        double durationMs,
        bool success,
        string? documentType = null,
        string? strategy = null)
    {
        var tags = new TagList
        {
            { "success", success.ToString().ToLowerInvariant() }
        };

        if (documentType != null)
            tags.Add("document_type", documentType);
        if (strategy != null)
            tags.Add("strategy", strategy);

        if (success)
        {
            DocumentsIndexed.Add(1, tags);
            ChunksCreated.Add(chunkCount, tags);
            ChunksPerDocument.Record(chunkCount, tags);
        }
        else
        {
            IndexingErrors.Add(1, tags);
        }

        IndexingDuration.Record(durationMs, tags);
    }

    /// <summary>
    /// Records search metrics for a completed search operation.
    /// </summary>
    public void RecordSearch(
        int resultCount,
        double durationMs,
        bool success,
        string? searchMode = null,
        double? topScore = null)
    {
        var tags = new TagList
        {
            { "success", success.ToString().ToLowerInvariant() }
        };

        if (searchMode != null)
            tags.Add("search_mode", searchMode);

        SearchRequests.Add(1, tags);

        if (success)
        {
            ResultsReturned.Record(resultCount, tags);
            if (topScore.HasValue)
                TopResultScore.Record(topScore.Value, tags);
        }
        else
        {
            SearchErrors.Add(1, tags);
        }

        SearchDuration.Record(durationMs, tags);
    }

    /// <summary>
    /// Records embedding generation metrics.
    /// </summary>
    public void RecordEmbedding(
        long tokenCount,
        double durationMs,
        bool success,
        string? provider = null)
    {
        var tags = new TagList
        {
            { "success", success.ToString().ToLowerInvariant() }
        };

        if (provider != null)
            tags.Add("provider", provider);

        EmbeddingRequests.Add(1, tags);

        if (success)
        {
            TokensProcessed.Record(tokenCount, tags);
        }
        else
        {
            EmbeddingErrors.Add(1, tags);
        }

        EmbeddingDuration.Record(durationMs, tags);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _meter.Dispose();
    }
}
