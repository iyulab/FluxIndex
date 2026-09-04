using FluxGuard.Remote.RAG;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Models;
using FluxIndex.Core.Application.Services.Base;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using DocumentChunkEntity = FluxIndex.Core.Domain.Entities.DocumentChunk;
using DocumentChunkModel = FluxIndex.Core.Domain.Models.CacheDocumentChunk;
using RankedResultCore = FluxIndex.Core.Domain.Models.RankedResult;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.SDK;

public partial class Retriever
{

    [LoggerMessage(Level = LogLevel.Information, Message = "Searching for: {Query}")]
    private static partial void LogSearching(ILogger logger, string query);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache hit for query: {Query}")]
    private static partial void LogCacheHit(ILogger logger, string query);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Embedding cache hit for query: {Query}")]
    private static partial void LogEmbeddingCacheHit(ILogger logger, string query);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cached embedding for query: {Query}")]
    private static partial void LogCachedEmbedding(ILogger logger, string query);

    [LoggerMessage(Level = LogLevel.Information, Message = "Found {Count} results for query: {Query}")]
    private static partial void LogFoundResults(ILogger logger, int count, string query);

    /// <summary>
    /// Surface the documented silent degradation: hybrid search returns results, but every one of
    /// them came from the vector leg because the sparse (BM25) index holds nothing. The index is
    /// process-local and no indexing API populates it, so this is the normal state after a restart
    /// or in a process that did not itself index.
    /// </summary>
    private void WarnIfSparseLegContributedNothing(
        IReadOnlyList<Core.Domain.Models.HybridSearchResult> results,
        string query)
    {
        if (results.Count == 0 || results.Any(result => result.SparseScore > 0))
        {
            return;
        }

        LogHybridDegradedToVectorOnly(_logger, query);
    }

    /// <summary>
    /// Same degradation on the v1 path, where the keyword leg reads the in-memory document
    /// repository: the vector leg matched but the keyword leg saw nothing to match against.
    /// </summary>
    private void WarnIfKeywordLegContributedNothing(
        IEnumerable<VectorSearchResult> keywordResults,
        IEnumerable<VectorSearchResult> vectorResults,
        string keyword)
    {
        if (keywordResults.Any() || !vectorResults.Any())
        {
            return;
        }

        LogHybridDegradedToVectorOnly(_logger, keyword);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hybrid search degraded to vector-only for query '{Query}': the keyword leg returned no candidates. The keyword index is process-local and is not populated by the indexing API, so it is empty after a restart or in a process that did not index. Results are ranked by vector similarity alone.")]
    private static partial void LogHybridDegradedToVectorOnly(ILogger logger, string query);

    [LoggerMessage(Level = LogLevel.Information, Message = "Hybrid search activated for query: {Query}")]
    private static partial void LogHybridSearchActivated(ILogger logger, string query);

    [LoggerMessage(Level = LogLevel.Information, Message = "Hybrid search - keyword: {Keyword}, query: {Query}")]
    private static partial void LogHybridSearch(ILogger logger, string keyword, string query);

    [LoggerMessage(Level = LogLevel.Information, Message = "Keyword search: {Keyword}")]
    private static partial void LogKeywordSearch(ILogger logger, string keyword);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Getting document: {DocumentId}")]
    private static partial void LogGettingDocument(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Getting chunk: {ChunkId}")]
    private static partial void LogGettingChunk(ILogger logger, string chunkId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Finding similar documents to: {DocumentId}")]
    private static partial void LogFindingSimilarDocuments(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Quantized search for: {Query}")]
    private static partial void LogQuantizedSearch(ILogger logger, string query);

    [LoggerMessage(Level = LogLevel.Information, Message = "Quantized search with rerank for: {Query}")]
    private static partial void LogQuantizedSearchWithRerank(ILogger logger, string query);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RAG security pipeline blocked document '{DocumentId}' (chunk '{ChunkId}', risk score {RiskScore:F2}) from search results")]
    private static partial void LogRagSecurityBlocked(ILogger logger, string documentId, string chunkId, double riskScore);

}
