using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.SDK;

/// <summary>
/// FluxIndex 클라이언트 - Retriever와 Indexer를 통한 간편한 진입점
/// </summary>
public class FluxIndexClient : IFluxIndexClient
{
    private readonly Retriever _retriever;
    private readonly Indexer _indexer;
    private readonly ILogger<FluxIndexClient> _logger;

    public FluxIndexClient(
        Retriever retriever,
        Indexer indexer,
        ILogger<FluxIndexClient>? logger = null)
    {
        _retriever = retriever;
        _indexer = indexer;
        _logger = logger ?? new NullLogger<FluxIndexClient>();
    }

    /// <summary>
    /// Retriever 접근자
    /// </summary>
    public Retriever Retriever => _retriever;

    /// <summary>
    /// Indexer 접근자
    /// </summary>
    public Indexer Indexer => _indexer;

    /// <summary>
    /// FluxIndexClient 빌더 생성
    /// </summary>
    public static FluxIndexClientBuilder CreateBuilder()
    {
        return new FluxIndexClientBuilder();
    }

    // Convenience methods - delegate to Retriever

    /// <summary>
    /// 벡터 검색 (Retriever 위임)
    /// </summary>
    public async Task<IEnumerable<SearchResult>> SearchAsync(
        string query,
        int maxResults = 10,
        float minScore = 0.5f,
        Dictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default)
    {
        return await _retriever.SearchAsync(query, maxResults, minScore, filter, cancellationToken);
    }

    /// <summary>
    /// 하이브리드 검색 (Retriever 위임)
    /// </summary>
    public async Task<IEnumerable<SearchResult>> HybridSearchAsync(
        string keyword,
        string query,
        int maxResults = 10,
        float vectorWeight = 0.7f,
        Dictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default)
    {
        return await _retriever.HybridSearchAsync(keyword, query, maxResults, vectorWeight, filter, cancellationToken);
    }

    /// <summary>
    /// 문서 조회 (Retriever 위임)
    /// </summary>
    public async Task<Document?> GetDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        return await _retriever.GetDocumentAsync(documentId, cancellationToken);
    }

    /// <summary>
    /// 문서 수 조회 (Retriever 위임)
    /// </summary>
    public async Task<int> GetDocumentCountAsync(CancellationToken cancellationToken = default)
    {
        var stats = await _retriever.GetStatisticsAsync(cancellationToken);
        return stats.TotalDocuments;
    }

    /// <summary>
    /// 청크 수 조회 (Retriever 위임)
    /// </summary>
    public async Task<int> GetChunkCountAsync(CancellationToken cancellationToken = default)
    {
        var stats = await _retriever.GetStatisticsAsync(cancellationToken);
        return stats.TotalChunks;
    }

    // Convenience methods - delegate to Indexer

    /// <summary>
    /// 문서 인덱싱 (Indexer 위임)
    /// </summary>
    public async Task<string> IndexAsync(
        Document document,
        CancellationToken cancellationToken = default)
    {
        return await _indexer.IndexDocumentAsync(document, cancellationToken);
    }

    /// <summary>
    /// 청크 인덱싱 (Indexer 위임)
    /// </summary>
    public async Task<string> IndexChunksAsync(
        IEnumerable<DocumentChunk> chunks,
        string? documentId = null,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        return await _indexer.IndexChunksAsync(chunks, documentId, metadata, cancellationToken);
    }

    /// <summary>
    /// 문서 삭제 (Indexer 위임)
    /// </summary>
    public async Task<bool> DeleteDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        return await _indexer.DeleteDocumentAsync(documentId, cancellationToken);
    }

    /// <summary>
    /// 문서 업데이트 (Indexer 위임)
    /// </summary>
    public async Task UpdateDocumentAsync(
        string documentId,
        Document updatedDocument,
        CancellationToken cancellationToken = default)
    {
        await _indexer.UpdateDocumentAsync(documentId, updatedDocument, cancellationToken);
    }

    /// <summary>
    /// 통계 조회
    /// </summary>
    public async Task<ClientStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var retrieverStats = await _retriever.GetStatisticsAsync(cancellationToken);
        var indexerStats = await _indexer.GetStatisticsAsync(cancellationToken);

        return new ClientStatistics
        {
            TotalDocuments = retrieverStats.TotalDocuments,
            TotalChunks = retrieverStats.TotalChunks,
            AverageChunksPerDocument = retrieverStats.AverageChunksPerDocument,
            CacheEnabled = retrieverStats.CacheEnabled,
            VectorStoreProvider = retrieverStats.VectorStoreProvider,
            DefaultChunkSize = indexerStats.DefaultChunkSize,
            DefaultChunkOverlap = indexerStats.DefaultChunkOverlap,
            EmbeddingModel = indexerStats.EmbeddingModel
        };
    }
}

/// <summary>
/// FluxIndex 클라이언트 인터페이스
/// </summary>
public interface IFluxIndexClient
{
    Retriever Retriever { get; }
    Indexer Indexer { get; }
    
    // Convenience methods
    Task<IEnumerable<SearchResult>> SearchAsync(string query, int maxResults = 10, float minScore = 0.5f, Dictionary<string, object>? filter = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<SearchResult>> HybridSearchAsync(string keyword, string query, int maxResults = 10, float vectorWeight = 0.7f, Dictionary<string, object>? filter = null, CancellationToken cancellationToken = default);
    Task<Document?> GetDocumentAsync(string documentId, CancellationToken cancellationToken = default);
    Task<string> IndexAsync(Document document, CancellationToken cancellationToken = default);
    Task<string> IndexChunksAsync(IEnumerable<DocumentChunk> chunks, string? documentId = null, Dictionary<string, object>? metadata = null, CancellationToken cancellationToken = default);
    Task<bool> DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default);
    Task UpdateDocumentAsync(string documentId, Document updatedDocument, CancellationToken cancellationToken = default);
    Task<ClientStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
    Task<int> GetDocumentCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetChunkCountAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 클라이언트 통계
/// </summary>
public class ClientStatistics
{
    public int TotalDocuments { get; set; }
    public int TotalChunks { get; set; }
    public double AverageChunksPerDocument { get; set; }
    public bool CacheEnabled { get; set; }
    public string VectorStoreProvider { get; set; } = string.Empty;
    public int DefaultChunkSize { get; set; }
    public int DefaultChunkOverlap { get; set; }
    public string EmbeddingModel { get; set; } = string.Empty;
}