using FluxIndex.AI.OpenAI.Integration;
using FluxIndex.AI.OpenAI.Services;
using FluxIndex.Core.Application.Interfaces;
// using FluxIndex.Core.Application.Options; // Options may be in different namespace
using FluxIndex.Core.Domain.Entities;
using FluxIndex.SDK;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.AI.OpenAI.Extensions;

/// <summary>
/// FluxIndexClient를 위한 AI 메타데이터 추출 확장 메서드
/// </summary>
public static class FluxIndexClientExtensions
{
    /// <summary>
    /// 문서 인덱싱 시 AI 메타데이터 추출 활성화
    /// </summary>
    /// <param name="client">FluxIndex 클라이언트</param>
    /// <param name="chunks">인덱싱할 문서 청크들</param>
    /// <param name="options">배치 처리 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>메타데이터가 강화된 청크들</returns>
    public static async Task<IReadOnlyList<DocumentChunk>> IndexWithAIMetadataAsync(
        this FluxIndexClient client,
        IReadOnlyList<DocumentChunk> chunks,
        BatchProcessingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (client == null)
            throw new ArgumentNullException(nameof(client));

        if (chunks == null || chunks.Count == 0)
            throw new ArgumentException("Chunks cannot be null or empty", nameof(chunks));

        // 서비스 컨테이너에서 메타데이터 통합 서비스 가져오기
        var serviceProvider = GetServiceProvider(client);
        var metadataIntegration = serviceProvider?.GetService<DocumentChunkMetadataIntegration>();

        if (metadataIntegration == null)
        {
            throw new InvalidOperationException(
                "AI metadata enrichment service is not configured. " +
                "Please register the service using AddOpenAIMetadataExtraction() or AddAzureOpenAIMetadataExtraction().");
        }

        // AI 메타데이터 추출 및 적용
        await metadataIntegration.EnrichDocumentChunksBatchAsync(chunks, options, cancellationToken);

        // 일반적인 인덱싱 수행
        await client.Indexer.IndexAsync(chunks, cancellationToken);

        return chunks;
    }

    /// <summary>
    /// 단일 문서 청크에 AI 메타데이터 추출 적용
    /// </summary>
    /// <param name="client">FluxIndex 클라이언트</param>
    /// <param name="chunk">메타데이터를 추출할 청크</param>
    /// <param name="context">추가 컨텍스트 정보</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>메타데이터가 강화된 청크</returns>
    public static async Task<DocumentChunk> EnrichChunkMetadataAsync(
        this FluxIndexClient client,
        DocumentChunk chunk,
        string? context = null,
        CancellationToken cancellationToken = default)
    {
        if (client == null)
            throw new ArgumentNullException(nameof(client));

        if (chunk == null)
            throw new ArgumentNullException(nameof(chunk));

        var serviceProvider = GetServiceProvider(client);
        var metadataIntegration = serviceProvider?.GetService<DocumentChunkMetadataIntegration>();

        if (metadataIntegration == null)
        {
            throw new InvalidOperationException(
                "AI metadata enrichment service is not configured. " +
                "Please register the service using AddOpenAIMetadataExtraction() or AddAzureOpenAIMetadataExtraction().");
        }

        await metadataIntegration.EnrichDocumentChunkAsync(chunk, context, cancellationToken);
        return chunk;
    }

    /// <summary>
    /// AI 메타데이터 추출 서비스의 상태 확인
    /// </summary>
    /// <param name="client">FluxIndex 클라이언트</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>서비스가 정상 작동하는지 여부</returns>
    public static async Task<bool> IsAIMetadataServiceHealthyAsync(
        this FluxIndexClient client,
        CancellationToken cancellationToken = default)
    {
        if (client == null)
            throw new ArgumentNullException(nameof(client));

        try
        {
            var serviceProvider = GetServiceProvider(client);
            var enrichmentService = serviceProvider?.GetService<IMetadataEnrichmentService>();

            if (enrichmentService == null)
                return false;

            return await enrichmentService.IsHealthyAsync(cancellationToken);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// AI 메타데이터 추출 서비스의 사용 통계 조회
    /// </summary>
    /// <param name="client">FluxIndex 클라이언트</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>사용 통계 정보</returns>
    public static async Task<MetadataExtractionStatistics?> GetAIMetadataStatisticsAsync(
        this FluxIndexClient client,
        CancellationToken cancellationToken = default)
    {
        if (client == null)
            throw new ArgumentNullException(nameof(client));

        var serviceProvider = GetServiceProvider(client);
        var enrichmentService = serviceProvider?.GetService<IMetadataEnrichmentService>();

        if (enrichmentService == null)
            return null;

        return await enrichmentService.GetStatisticsAsync(cancellationToken);
    }

    /// <summary>
    /// 검색 결과에 AI 메타데이터 정보 포함
    /// </summary>
    /// <param name="client">FluxIndex 클라이언트</param>
    /// <param name="query">검색 쿼리</param>
    /// <param name="includeAIMetadata">AI 메타데이터 포함 여부</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>AI 메타데이터가 포함된 검색 결과</returns>
    public static async Task<IReadOnlyList<EnhancedSearchResult>> SearchWithAIMetadataAsync(
        this FluxIndexClient client,
        string query,
        bool includeAIMetadata = true,
        CancellationToken cancellationToken = default)
    {
        if (client == null)
            throw new ArgumentNullException(nameof(client));

        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be empty", nameof(query));

        // 일반적인 검색 수행
        var searchResults = await client.Retriever.SearchAsync(query, cancellationToken);

        // EnhancedSearchResult로 변환
        var enhancedResults = new List<EnhancedSearchResult>();

        foreach (var result in searchResults)
        {
            var enhancedResult = new EnhancedSearchResult
            {
                Chunk = result.Chunk,
                SimilarityScore = result.Score,
                BM25Score = result.Score, // 기본값으로 설정
                HybridScore = result.Score,
                RerankedScore = result.Score,
                MatchedTerms = ExtractMatchedTerms(query, result.Chunk?.Content),
                HighlightedContent = HighlightContent(result.Chunk?.Content, query)
            };

            // AI 메타데이터 정보 추가
            if (includeAIMetadata && result.Chunk != null)
            {
                AddAIMetadataToResult(enhancedResult, result.Chunk);
            }

            enhancedResults.Add(enhancedResult);
        }

        return enhancedResults;
    }

    /// <summary>
    /// FluxIndexClient에서 서비스 프로바이더 추출 (리플렉션 사용)
    /// </summary>
    private static IServiceProvider? GetServiceProvider(FluxIndexClient client)
    {
        // FluxIndexClient의 내부 서비스 프로바이더에 접근하는 로직
        // 실제 구현에서는 FluxIndexClient에 공개 속성이나 메서드로 제공될 수 있음
        var clientType = client.GetType();
        var serviceProviderField = clientType.GetField("_serviceProvider",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        return serviceProviderField?.GetValue(client) as IServiceProvider;
    }

    /// <summary>
    /// 쿼리와 매치되는 용어들 추출
    /// </summary>
    private static List<string> ExtractMatchedTerms(string query, string? content)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(content))
            return new List<string>();

        var queryTerms = query.ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        var contentLower = content.ToLowerInvariant();

        return queryTerms.Where(term => contentLower.Contains(term)).ToList();
    }

    /// <summary>
    /// 검색 쿼리에 따른 콘텐츠 하이라이팅
    /// </summary>
    private static string HighlightContent(string? content, string query)
    {
        if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(query))
            return content ?? string.Empty;

        var queryTerms = query.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var highlighted = content;

        foreach (var term in queryTerms)
        {
            highlighted = highlighted.Replace(term, $"**{term}**", StringComparison.OrdinalIgnoreCase);
        }

        return highlighted;
    }

    /// <summary>
    /// 검색 결과에 AI 메타데이터 정보 추가
    /// </summary>
    private static void AddAIMetadataToResult(EnhancedSearchResult result, DocumentChunk chunk)
    {
        var metadata = new Dictionary<string, object>();

        // AI 생성 정보 추가
        if (chunk.Properties.TryGetValue("ai_generated_title", out var title))
            metadata["ai_title"] = title;

        if (chunk.Properties.TryGetValue("ai_generated_summary", out var summary))
            metadata["ai_summary"] = summary;

        if (chunk.Properties.TryGetValue("ai_quality_score", out var qualityScore))
            metadata["ai_quality_score"] = qualityScore;

        if (chunk.Properties.TryGetValue("ai_confidence", out var confidence))
            metadata["ai_confidence"] = confidence;

        if (chunk.Properties.TryGetValue("ai_generated_questions", out var questions))
            metadata["ai_questions"] = questions;

        // AI 키워드 및 엔터티 정보
        if (chunk.ChunkMetadata?.Keywords?.Any() == true)
            metadata["ai_keywords"] = chunk.ChunkMetadata.Keywords;

        if (chunk.ChunkMetadata?.Entities?.Any() == true)
            metadata["ai_entities"] = chunk.ChunkMetadata.Entities;

        result.ExplanationMetadata = metadata;
    }
}

/// <summary>
/// AI 메타데이터 추출을 위한 FluxIndexClientBuilder 확장
/// </summary>
public static class FluxIndexClientBuilderExtensions
{
    /// <summary>
    /// FluxIndex 클라이언트에 AI 메타데이터 추출 서비스 추가
    /// </summary>
    /// <param name="builder">FluxIndex 클라이언트 빌더</param>
    /// <param name="configureOpenAI">OpenAI 옵션 설정</param>
    /// <param name="configureExtraction">메타데이터 추출 옵션 설정</param>
    /// <returns>설정이 완료된 빌더</returns>
    public static FluxIndexClientBuilder WithAIMetadataExtraction(
        this FluxIndexClientBuilder builder,
        Action<OpenAIOptions> configureOpenAI,
        Action<MetadataExtractionOptions>? configureExtraction = null)
    {
        // 서비스 등록 로직 (실제 구현에서는 FluxIndexClientBuilder에 서비스 등록 메서드가 있어야 함)
        builder.ConfigureServices(services =>
        {
            services.AddOpenAIMetadataExtraction(configureOpenAI, configureExtraction);
            services.AddSingleton<DocumentChunkMetadataIntegration>();
        });

        return builder;
    }

    /// <summary>
    /// FluxIndex 클라이언트에 Azure OpenAI 메타데이터 추출 서비스 추가
    /// </summary>
    /// <param name="builder">FluxIndex 클라이언트 빌더</param>
    /// <param name="apiKey">Azure OpenAI API 키</param>
    /// <param name="resourceUrl">Azure OpenAI 리소스 URL</param>
    /// <param name="deploymentName">배포 이름</param>
    /// <param name="configureExtraction">메타데이터 추출 옵션 설정</param>
    /// <returns>설정이 완료된 빌더</returns>
    public static FluxIndexClientBuilder WithAzureAIMetadataExtraction(
        this FluxIndexClientBuilder builder,
        string apiKey,
        string resourceUrl,
        string deploymentName,
        Action<MetadataExtractionOptions>? configureExtraction = null)
    {
        builder.ConfigureServices(services =>
        {
            services.AddAzureOpenAIMetadataExtraction(apiKey, resourceUrl, deploymentName, configureExtraction);
            services.AddSingleton<DocumentChunkMetadataIntegration>();
        });

        return builder;
    }
}