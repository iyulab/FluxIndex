using Microsoft.Extensions.Logging;

namespace FluxIndex.Storage.SQLite;

public partial class SQLiteVecVectorStore
{

    [LoggerMessage(Level = LogLevel.Debug, Message = "Vector stored: {Id}, Document: {DocumentId}")]
    private static partial void LogVectorStored(ILogger logger, string id, string documentId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Vector store failed: Document {DocumentId}")]
    private static partial void LogVectorStoreFailed(ILogger logger, Exception exception, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Large batch store started: {TotalCount} items total, {BatchCount} batches ({BatchSize} per batch)")]
    private static partial void LogLargeBatchStart(ILogger logger, int totalCount, int batchCount, int batchSize);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Batch store progress: {Processed}/{Total} ({Percent:P0}), elapsed: {Elapsed}, remaining: {Remaining}")]
    private static partial void LogBatchProgress(ILogger logger, int processed, int total, double percent, TimeSpan elapsed, TimeSpan remaining);

    [LoggerMessage(Level = LogLevel.Information, Message = "Batch vector store completed: {Count} items, elapsed: {Elapsed}, throughput: {Rate:F0} items/sec")]
    private static partial void LogBatchStoreCompleted(ILogger logger, int count, TimeSpan elapsed, double rate);

    [LoggerMessage(Level = LogLevel.Error, Message = "Batch vector store failed: {Count} items")]
    private static partial void LogBatchStoreFailed(ILogger logger, Exception exception, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Batch vector insert completed: {Count} items")]
    private static partial void LogBatchVectorInserted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Batch vector insert failed")]
    private static partial void LogBatchVectorInsertFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "vec0 table recovered — retry completed")]
    private static partial void LogVecTableRecovered(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cross-fingerprint orphan scan failed during init (non-fatal); unreachable vec tables will not be reported this run")]
    private static partial void LogCrossFingerprintScanFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Vector store health check passed")]
    private static partial void LogHealthCheckPassed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Vector store health check failed")]
    private static partial void LogHealthCheckFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "vec0 table recovery failed")]
    private static partial void LogVecTableRecoveryFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Vector get failed: {Id}")]
    private static partial void LogVectorGetFailed(ILogger logger, Exception exception, string id);

    [LoggerMessage(Level = LogLevel.Error, Message = "Vector search failed")]
    private static partial void LogVectorSearchFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Retrying search in fallback mode")]
    private static partial void LogFallbackRetry(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "sqlite-vec search completed: {Count} results")]
    private static partial void LogVecSearchCompleted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "sqlite-vec native search failed")]
    private static partial void LogVecNativeSearchFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hybrid search completed: vector={VectorCount}, FTS={FtsCount}, combined={CombinedCount}")]
    private static partial void LogHybridSearchCompleted(ILogger logger, int vectorCount, int ftsCount, int combinedCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Hybrid search failed")]
    private static partial void LogHybridSearchFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "FTS5 search completed: {Count} results, query: {Query}")]
    private static partial void LogFts5SearchCompleted(ILogger logger, int count, string query);

    [LoggerMessage(Level = LogLevel.Warning, Message = "FTS5 search failed, returning empty result")]
    private static partial void LogFts5SearchFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "FTS5 is disabled, text search cannot be performed")]
    private static partial void LogFts5Disabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Document chunk lookup failed: {DocumentId}")]
    private static partial void LogGetByDocumentFailed(ILogger logger, Exception exception, string documentId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Vector delete failed: {Id}")]
    private static partial void LogVectorDeleteFailed(ILogger logger, Exception exception, string id);

    [LoggerMessage(Level = LogLevel.Error, Message = "Document vector delete failed: {DocumentId}")]
    private static partial void LogDeleteByDocumentFailed(ILogger logger, Exception exception, string documentId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Vector update failed: {Id}")]
    private static partial void LogVectorUpdateFailed(ILogger logger, Exception exception, string id);

    [LoggerMessage(Level = LogLevel.Information, Message = "Vector store cleared")]
    private static partial void LogStoreClearCompleted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Vector store clear failed")]
    private static partial void LogStoreClearFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Collection deleted: {CollectionName}")]
    private static partial void LogCollectionDeleted(ILogger logger, string collectionName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "vec0 query plan JIT warmup completed during initialization")]
    private static partial void LogVecJitWarmupCompleted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "vec0 JIT warmup query failed (non-fatal); first batch will pay cold-start cost")]
    private static partial void LogVecJitWarmupFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "vector_chunks batch INSERT: {RowCount} rows in single SQL statement")]
    private static partial void LogVectorChunksBatchInserted(ILogger logger, int rowCount);

}
