using Flux.Abstractions;
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
public partial class ClassificationValidationService : IClassificationValidationService
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
            LogClassificationValidation11(_logger, chunk.ChunkId, result.SkipReason);
            return result;
        }

        // 2. 콘텐츠 길이 검사
        if (chunk.Content.Length < _options.Validation.MinContentLength)
        {
            result.RequiresLlmClassification = false;
            result.SkipReason = $"Content too short: {chunk.Content.Length} < {_options.Validation.MinContentLength}";
            result.ValidationScore = 0;
            LogClassificationValidation10(_logger, chunk.ChunkId, result.SkipReason);
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
                LogClassificationValidation9(_logger, chunk.ChunkId);
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
                LogClassificationValidation8(_logger, chunk.ChunkId);
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
                LogClassificationValidation7(_logger, chunk.ChunkId);
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
            LogClassificationValidation6(_logger, chunk.ChunkId);
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
        LogClassificationValidation5(_logger, requiresLlm, results.Count);

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
            LogClassificationValidation4(_logger, classification.Confidence, _options.Validation.MinConfidenceThreshold);
            return false;
        }

        // 최소 결과 검증 (적어도 하나는 있어야 함)
        var hasContent = classification.Topics.Count > 0 ||
                        classification.Categories.Count > 0 ||
                        classification.Tags.Count > 0 ||
                        !string.IsNullOrEmpty(classification.Summary);

        if (!hasContent)
        {
            LogClassificationValidation3(_logger);
            return false;
        }

        // 개수 제한 검증
        if (classification.Topics.Count > _options.MaxTopics ||
            classification.Categories.Count > _options.MaxCategories ||
            classification.Tags.Count > _options.MaxTags ||
            classification.PotentialQuestions.Count > _options.MaxQuestions)
        {
            LogClassificationValidation2(_logger);
            return false;
        }

        // 요약 길이 검증
        if (classification.Summary?.Length > _options.MaxSummaryLength)
        {
            LogClassificationValidation1(_logger, classification.Summary.Length, _options.MaxSummaryLength);
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

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping chunk {ChunkId}: {Reason}")]
    private static partial void LogClassificationValidation11(ILogger logger, string chunkId, string? reason);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping chunk {ChunkId}: {Reason}")]
    private static partial void LogClassificationValidation10(ILogger logger, string chunkId, string? reason);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Using cached classification for chunk {ChunkId}")]
    private static partial void LogClassificationValidation9(ILogger logger, string chunkId);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Inheriting classification for chunk {ChunkId} from similar chunk")]
    private static partial void LogClassificationValidation8(ILogger logger, string chunkId);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping duplicate chunk {ChunkId}")]
    private static partial void LogClassificationValidation7(ILogger logger, string chunkId);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Chunk {ChunkId} has sufficient keywords, reducing scope")]
    private static partial void LogClassificationValidation6(ILogger logger, string chunkId);
    [LoggerMessage(Level = LogLevel.Information, Message = "Validation complete: {RequiresLlm}/{Total} chunks require LLM classification")]
    private static partial void LogClassificationValidation5(ILogger logger, int requiresLlm, int total);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Classification confidence below threshold: {Confidence} < {Threshold}")]
    private static partial void LogClassificationValidation4(ILogger logger, double confidence, double threshold);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Classification has no content")]
    private static partial void LogClassificationValidation3(ILogger logger);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Classification exceeds maximum counts")]
    private static partial void LogClassificationValidation2(ILogger logger);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Summary exceeds maximum length: {Length} > {Max}")]
    private static partial void LogClassificationValidation1(ILogger logger, int length, int max);

    #endregion
}
