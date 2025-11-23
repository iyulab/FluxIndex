using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// 분류 검증 서비스 - LLM 호출 최소화를 위한 검증 게이트
/// </summary>
public class ClassificationValidationService : IClassificationValidationService
{
    private readonly ClassificationOptions _options;
    private readonly IClassificationCacheService? _cacheService;
    private readonly ILogger<ClassificationValidationService> _logger;
    private readonly HashSet<string> _processedHashes = new();

    public ClassificationValidationService(
        IOptions<ClassificationOptions> options,
        ILogger<ClassificationValidationService> logger,
        IClassificationCacheService? cacheService = null)
    {
        _options = options.Value;
        _logger = logger;
        _cacheService = cacheService;
    }

    public async Task<ClassificationValidationResult> ValidateAsync(
        IEnrichedChunk chunk,
        CancellationToken cancellationToken = default)
    {
        var result = new ClassificationValidationResult
        {
            RequiresLlmClassification = true,
            ValidationScore = 1.0,
            RecommendedScope = _options.Scope
        };

        // 1. 품질 검사
        if (chunk.Quality < _options.Validation.MinQualityThreshold)
        {
            result.RequiresLlmClassification = false;
            result.SkipReason = $"Quality below threshold: {chunk.Quality:F2} < {_options.Validation.MinQualityThreshold}";
            result.ValidationScore = 0;
            _logger.LogDebug("Skipping chunk {ChunkId}: {Reason}", chunk.ChunkId, result.SkipReason);
            return result;
        }

        // 2. 콘텐츠 길이 검사
        if (chunk.Content.Length < _options.Validation.MinContentLength)
        {
            result.RequiresLlmClassification = false;
            result.SkipReason = $"Content too short: {chunk.Content.Length} < {_options.Validation.MinContentLength}";
            result.ValidationScore = 0;
            _logger.LogDebug("Skipping chunk {ChunkId}: {Reason}", chunk.ChunkId, result.SkipReason);
            return result;
        }

        // 3. 캐시 확인
        if (_cacheService != null && _options.EnableCache)
        {
            var cached = await _cacheService.GetAsync(chunk.ChunkId, cancellationToken);
            if (cached != null)
            {
                result.RequiresLlmClassification = false;
                result.SkipReason = "Found in cache";
                result.ExistingClassification = cached;
                result.ValidationScore = 0;
                _logger.LogDebug("Using cached classification for chunk {ChunkId}", chunk.ChunkId);
                return result;
            }

            // 유사 청크 확인
            var contentHash = ComputeContentHash(chunk.Content);
            var similar = await _cacheService.GetSimilarAsync(
                contentHash,
                _options.SimilarityInheritanceThreshold,
                cancellationToken);

            if (similar != null)
            {
                result.RequiresLlmClassification = false;
                result.SkipReason = "Inherited from similar chunk";
                result.ExistingClassification = new ChunkClassification
                {
                    Topics = similar.Topics,
                    Categories = similar.Categories,
                    Tags = similar.Tags,
                    RefinedKeywords = similar.RefinedKeywords,
                    Summary = similar.Summary,
                    PotentialQuestions = similar.PotentialQuestions,
                    Confidence = similar.Confidence,
                    Source = ClassificationSource.Inherited,
                    CreatedAt = similar.CreatedAt
                };
                result.ValidationScore = 0;
                _logger.LogDebug("Inheriting classification for chunk {ChunkId} from similar chunk", chunk.ChunkId);
                return result;
            }
        }

        // 4. 중복 검사
        if (_options.Validation.EnableDuplicateCheck)
        {
            var contentHash = ComputeContentHash(chunk.Content);
            if (_processedHashes.Contains(contentHash))
            {
                result.RequiresLlmClassification = false;
                result.SkipReason = "Duplicate content detected";
                result.ValidationScore = 0;
                _logger.LogDebug("Skipping duplicate chunk {ChunkId}", chunk.ChunkId);
                return result;
            }
            _processedHashes.Add(contentHash);
        }

        // 5. 기존 메타데이터 충분성 검사
        var existingKeywords = chunk.Source.Keywords?.Count ?? 0;
        if (existingKeywords >= _options.Validation.MinExistingKeywords)
        {
            // 메타데이터가 충분하면 범위 축소
            result.RecommendedScope &= ~ClassificationScope.Keywords;
            result.ValidationScore *= 0.8;
            _logger.LogDebug("Chunk {ChunkId} has sufficient keywords, reducing scope", chunk.ChunkId);
        }

        // 6. ContextDependency 기반 범위 조정
        if (chunk.ContextDependency < 0.5)
        {
            // 문맥 의존도가 낮으면 요약/질문 스킵 가능
            result.RecommendedScope &= ~(ClassificationScope.Summary | ClassificationScope.Questions);
            result.ValidationScore *= 0.7;
        }

        return result;
    }

    public async Task<Dictionary<string, ClassificationValidationResult>> ValidateBatchAsync(
        IEnumerable<IEnrichedChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, ClassificationValidationResult>();

        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results[chunk.ChunkId] = await ValidateAsync(chunk, cancellationToken);
        }

        var requiresLlm = results.Count(r => r.Value.RequiresLlmClassification);
        _logger.LogInformation(
            "Validation complete: {RequiresLlm}/{Total} chunks require LLM classification",
            requiresLlm, results.Count);

        return results;
    }

    public bool ValidateOutput(ChunkClassification classification)
    {
        if (!_options.Validation.EnableOutputValidation)
            return true;

        // 기본 검증
        if (classification == null)
            return false;

        // 신뢰도 검증
        if (classification.Confidence < _options.Validation.MinConfidenceThreshold)
        {
            _logger.LogWarning(
                "Classification confidence below threshold: {Confidence:F2} < {Threshold}",
                classification.Confidence, _options.Validation.MinConfidenceThreshold);
            return false;
        }

        // 최소 결과 검증 (적어도 하나는 있어야 함)
        var hasContent = classification.Topics.Count > 0 ||
                        classification.Categories.Count > 0 ||
                        classification.Tags.Count > 0 ||
                        !string.IsNullOrEmpty(classification.Summary);

        if (!hasContent)
        {
            _logger.LogWarning("Classification has no content");
            return false;
        }

        // 개수 제한 검증
        if (classification.Topics.Count > _options.MaxTopics ||
            classification.Categories.Count > _options.MaxCategories ||
            classification.Tags.Count > _options.MaxTags ||
            classification.PotentialQuestions.Count > _options.MaxQuestions)
        {
            _logger.LogWarning("Classification exceeds maximum counts");
            return false;
        }

        // 요약 길이 검증
        if (classification.Summary?.Length > _options.MaxSummaryLength)
        {
            _logger.LogWarning(
                "Summary exceeds maximum length: {Length} > {Max}",
                classification.Summary.Length, _options.MaxSummaryLength);
            return false;
        }

        return true;
    }

    private static string ComputeContentHash(string content)
    {
        var normalized = content.Trim().ToLowerInvariant();
        var bytes = Encoding.UTF8.GetBytes(normalized);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }
}
