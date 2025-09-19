using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.ValueObjects;
using FluxIndex.Domain.Entities;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.AI.OpenAI.Integration;

/// <summary>
/// 새로운 메타데이터 추출 서비스와 기존 DocumentChunk 모델 간 통합
/// 호환성을 유지하면서 새로운 기능을 제공
/// </summary>
public class DocumentChunkMetadataIntegration
{
    private readonly IMetadataEnrichmentService _enrichmentService;
    private readonly ILogger<DocumentChunkMetadataIntegration> _logger;

    public DocumentChunkMetadataIntegration(
        IMetadataEnrichmentService enrichmentService,
        ILogger<DocumentChunkMetadataIntegration> logger)
    {
        _enrichmentService = enrichmentService ?? throw new ArgumentNullException(nameof(enrichmentService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// DocumentChunk에 새로운 메타데이터 추출 결과 적용
    /// </summary>
    /// <param name="chunk">업데이트할 DocumentChunk</param>
    /// <param name="context">추가 컨텍스트 정보</param>
    /// <param name="cancellationToken">취소 토큰</param>
    public async Task EnrichDocumentChunkAsync(
        DocumentChunk chunk,
        string? context = null,
        CancellationToken cancellationToken = default)
    {
        if (chunk == null)
            throw new ArgumentNullException(nameof(chunk));

        try
        {
            _logger.LogDebug("Enriching metadata for chunk {ChunkId}", chunk.Id);

            // 새로운 메타데이터 추출 서비스 사용
            var enrichedMetadata = await _enrichmentService.ExtractMetadataAsync(
                chunk.Content,
                context,
                cancellationToken);

            // 기존 ChunkMetadata 모델로 변환
            var legacyMetadata = ConvertToLegacyMetadata(enrichedMetadata, chunk.Content);

            // DocumentChunk에 메타데이터 적용
            chunk.SetMetadata(legacyMetadata);

            // 추가 메타데이터를 Properties에 저장
            ApplyAdditionalMetadata(chunk, enrichedMetadata);

            _logger.LogInformation("Successfully enriched metadata for chunk {ChunkId} with quality score {QualityScore}",
                chunk.Id, enrichedMetadata.QualityScore);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enrich metadata for chunk {ChunkId}", chunk.Id);

            // 실패 시 기본 메타데이터 설정
            ApplyFallbackMetadata(chunk);
            throw;
        }
    }

    /// <summary>
    /// 여러 DocumentChunk를 배치로 메타데이터 강화
    /// </summary>
    /// <param name="chunks">강화할 청크 목록</param>
    /// <param name="batchOptions">배치 처리 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    public async Task EnrichDocumentChunksBatchAsync(
        IReadOnlyList<DocumentChunk> chunks,
        Core.Application.Options.BatchProcessingOptions? batchOptions = null,
        CancellationToken cancellationToken = default)
    {
        if (chunks == null || chunks.Count == 0)
            throw new ArgumentException("Chunks cannot be null or empty", nameof(chunks));

        _logger.LogInformation("Starting batch metadata enrichment for {ChunkCount} chunks", chunks.Count);

        try
        {
            // 콘텐츠 추출
            var contents = chunks.Select(c => c.Content).ToList();

            // 배치 메타데이터 추출
            var enrichedMetadatas = await _enrichmentService.ExtractBatchAsync(
                contents,
                batchOptions,
                cancellationToken);

            // 각 청크에 메타데이터 적용
            for (int i = 0; i < chunks.Count; i++)
            {
                if (i < enrichedMetadatas.Count)
                {
                    var chunk = chunks[i];
                    var metadata = enrichedMetadatas[i];

                    var legacyMetadata = ConvertToLegacyMetadata(metadata, chunk.Content);
                    chunk.SetMetadata(legacyMetadata);
                    ApplyAdditionalMetadata(chunk, metadata);
                }
            }

            _logger.LogInformation("Successfully completed batch metadata enrichment for {ChunkCount} chunks", chunks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete batch metadata enrichment");

            // 실패한 청크들에 대해 기본 메타데이터 적용
            foreach (var chunk in chunks)
            {
                if (chunk.ChunkMetadata == null || IsEmptyMetadata(chunk.ChunkMetadata))
                {
                    ApplyFallbackMetadata(chunk);
                }
            }
            throw;
        }
    }

    /// <summary>
    /// 새로운 ChunkMetadata를 기존 모델로 변환
    /// </summary>
    private static FluxIndex.Domain.Entities.ChunkMetadata ConvertToLegacyMetadata(
        Core.Domain.ValueObjects.ChunkMetadata enrichedMetadata,
        string content)
    {
        var legacyMetadata = new FluxIndex.Domain.Entities.ChunkMetadata
        {
            // 텍스트 분석 메타데이터
            TokenCount = EstimateTokenCount(content),
            CharacterCount = content.Length,
            SentenceCount = EstimateSentenceCount(content),
            ReadabilityScore = 0.7, // 기본값, 필요시 실제 계산 로직 추가
            Language = "ko",

            // 의미적 메타데이터 (새로운 메타데이터에서 가져옴)
            Keywords = enrichedMetadata.Keywords?.ToList() ?? new List<string>(),
            Entities = enrichedMetadata.Entities?.ToList() ?? new List<string>(),
            Topics = ExtractTopicsFromKeywords(enrichedMetadata.Keywords),
            ContentType = "text", // 기본값

            // 검색 최적화 메타데이터
            ImportanceScore = enrichedMetadata.QualityScore,
            SearchableTerms = enrichedMetadata.GetSearchableTerms().ToList(),
            KeywordWeights = CalculateKeywordWeights(enrichedMetadata.Keywords)
        };

        return legacyMetadata;
    }

    /// <summary>
    /// 추가 메타데이터를 DocumentChunk Properties에 저장
    /// </summary>
    private static void ApplyAdditionalMetadata(
        DocumentChunk chunk,
        Core.Domain.ValueObjects.ChunkMetadata enrichedMetadata)
    {
        chunk.AddProperty("ai_generated_title", enrichedMetadata.Title);
        chunk.AddProperty("ai_generated_summary", enrichedMetadata.Summary);
        chunk.AddProperty("ai_quality_score", enrichedMetadata.QualityScore);
        chunk.AddProperty("ai_extracted_at", enrichedMetadata.ExtractedAt);
        chunk.AddProperty("ai_extracted_by", enrichedMetadata.ExtractedBy);
        chunk.AddProperty("ai_confidence", enrichedMetadata.Confidence.ToString());

        // 생성된 질문들 저장
        if (enrichedMetadata.GeneratedQuestions?.Any() == true)
        {
            chunk.AddProperty("ai_generated_questions", enrichedMetadata.GeneratedQuestions.ToList());
        }

        // 사용자 정의 필드들 저장
        foreach (var customField in enrichedMetadata.CustomFields)
        {
            chunk.AddProperty($"ai_custom_{customField.Key}", customField.Value);
        }
    }

    /// <summary>
    /// 실패 시 기본 메타데이터 적용
    /// </summary>
    private static void ApplyFallbackMetadata(DocumentChunk chunk)
    {
        var fallbackMetadata = new FluxIndex.Domain.Entities.ChunkMetadata
        {
            TokenCount = EstimateTokenCount(chunk.Content),
            CharacterCount = chunk.Content.Length,
            SentenceCount = EstimateSentenceCount(chunk.Content),
            ReadabilityScore = 0.5,
            Language = "ko",
            ContentType = "text",
            ImportanceScore = 0.5,
            SearchableTerms = ExtractBasicTerms(chunk.Content)
        };

        chunk.SetMetadata(fallbackMetadata);
        chunk.AddProperty("ai_enrichment_failed", true);
        chunk.AddProperty("ai_fallback_applied", DateTime.UtcNow);
    }

    /// <summary>
    /// 메타데이터가 비어있는지 확인
    /// </summary>
    private static bool IsEmptyMetadata(FluxIndex.Domain.Entities.ChunkMetadata metadata)
    {
        return metadata.Keywords.Count == 0 &&
               metadata.Entities.Count == 0 &&
               metadata.SearchableTerms.Count == 0;
    }

    /// <summary>
    /// 토큰 수 추정 (간단한 로직)
    /// </summary>
    private static int EstimateTokenCount(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0;

        // 한국어 기준 대략적인 토큰 수 계산
        var words = content.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        return (int)(words.Length * 1.3); // 한국어는 영어보다 토큰 수가 많음
    }

    /// <summary>
    /// 문장 수 추정
    /// </summary>
    private static int EstimateSentenceCount(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0;

        var sentenceEnders = new[] { '.', '!', '?', '。', '！', '？' };
        return content.Count(c => sentenceEnders.Contains(c));
    }

    /// <summary>
    /// 키워드에서 토픽 추출
    /// </summary>
    private static List<string> ExtractTopicsFromKeywords(IReadOnlyList<string>? keywords)
    {
        if (keywords == null || keywords.Count == 0)
            return new List<string>();

        // 간단한 토픽 추출 로직
        return keywords.Where(k => k.Length > 3).Take(5).ToList();
    }

    /// <summary>
    /// 키워드 가중치 계산
    /// </summary>
    private static Dictionary<string, float> CalculateKeywordWeights(IReadOnlyList<string>? keywords)
    {
        var weights = new Dictionary<string, float>();

        if (keywords == null || keywords.Count == 0)
            return weights;

        // 순서에 따른 가중치 부여 (앞쪽 키워드가 더 중요)
        for (int i = 0; i < keywords.Count; i++)
        {
            var weight = 1.0f - (i * 0.1f);
            weights[keywords[i]] = Math.Max(weight, 0.1f);
        }

        return weights;
    }

    /// <summary>
    /// 기본 검색 가능한 용어 추출
    /// </summary>
    private static List<string> ExtractBasicTerms(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return new List<string>();

        return content
            .Split(new[] { ' ', '\t', '\n', '\r', '.', ',', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(term => term.Length > 2)
            .Take(10)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// 테스트용 팩토리 메서드
    /// </summary>
    public static DocumentChunkMetadataIntegration CreateForTesting(
        IMetadataEnrichmentService enrichmentService,
        ILogger<DocumentChunkMetadataIntegration>? logger = null)
    {
        return new DocumentChunkMetadataIntegration(
            enrichmentService,
            logger ?? new Microsoft.Extensions.Logging.Abstractions.NullLogger<DocumentChunkMetadataIntegration>());
    }
}