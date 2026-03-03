using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using ITextCompletionService = FluxIndex.Core.Application.Interfaces.ITextCompletionService;
using FluxIndex.Core.Application.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// LLM 기반 청크 분류 서비스
/// </summary>
public partial class LlmChunkClassificationService : IChunkClassificationService
{
    private static readonly JsonSerializerOptions s_caseInsensitiveJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ITextCompletionService? _textCompletion;
    private readonly IClassificationValidationService _validationService;
    private readonly IClassificationCacheService? _cacheService;
    private readonly ClassificationOptions _options;
    private readonly ILogger<LlmChunkClassificationService> _logger;

    public LlmChunkClassificationService(
        IOptions<ClassificationOptions> options,
        IClassificationValidationService validationService,
        ILogger<LlmChunkClassificationService> logger,
        ITextCompletionService? textCompletion = null,
        IClassificationCacheService? cacheService = null)
    {
        _options = options.Value;
        _validationService = validationService;
        _logger = logger;
        _textCompletion = textCompletion;
        _cacheService = cacheService;
    }

    public async Task<ChunkClassification> ClassifyAsync(
        IEnrichedChunk chunk,
        string? documentSummary = null,
        CancellationToken cancellationToken = default)
    {
        // 1. 검증 게이트
        var validation = await _validationService.ValidateAsync(chunk, cancellationToken);

        if (!validation.RequiresLlmClassification)
        {
            LogLlmChunkClassification8(_logger, chunk.ChunkId, validation.SkipReason ?? string.Empty);

            return validation.ExistingClassification ?? CreateSkippedClassification(validation.SkipReason);
        }

        // 2. LLM 서비스 확인
        if (_textCompletion == null)
        {
            LogLlmChunkClassification7(_logger);
            return CreateSkippedClassification("LLM service not configured");
        }

        // 3. LLM 분류 실행
        var classification = await ExecuteLlmClassificationAsync(
            chunk, documentSummary, validation.RecommendedScope, cancellationToken);

        // 4. 캐시 저장
        if (_cacheService != null && _options.EnableCache)
        {
            await _cacheService.SetAsync(chunk.ChunkId, classification, cancellationToken);
        }

        return classification;
    }

    public async Task<Dictionary<string, ChunkClassification>> ClassifyBatchAsync(
        IEnumerable<IEnrichedChunk> chunks,
        string? documentSummary = null,
        CancellationToken cancellationToken = default)
    {
        var chunkList = chunks.ToList();
        var results = new Dictionary<string, ChunkClassification>();

        LogLlmChunkClassification6(_logger, chunkList.Count);

        // 1. 배치 검증
        var validations = await _validationService.ValidateBatchAsync(chunkList, cancellationToken);

        // 2. LLM 필요 청크 분리
        var chunksRequiringLlm = chunkList
            .Where(c => validations[c.ChunkId].RequiresLlmClassification)
            .ToList();

        // 3. 스킵된 청크 결과 추가
        foreach (var chunk in chunkList)
        {
            var validation = validations[chunk.ChunkId];
            if (!validation.RequiresLlmClassification)
            {
                results[chunk.ChunkId] = validation.ExistingClassification ??
                    CreateSkippedClassification(validation.SkipReason);
            }
        }

        // 4. 배치 LLM 처리
        if (chunksRequiringLlm.Count > 0 && _textCompletion != null)
        {
            var llmResults = await ProcessBatchWithLlmAsync(
                chunksRequiringLlm, documentSummary, validations, cancellationToken);

            foreach (var (chunkId, classification) in llmResults)
            {
                results[chunkId] = classification;

                // 캐시 저장
                if (_cacheService != null && _options.EnableCache)
                {
                    await _cacheService.SetAsync(chunkId, classification, cancellationToken);
                }
            }
        }

        LogLlmChunkClassification5(_logger, chunksRequiringLlm.Count, chunkList.Count - chunksRequiringLlm.Count);

        return results;
    }

    private async Task<ChunkClassification> ExecuteLlmClassificationAsync(
        IEnrichedChunk chunk,
        string? documentSummary,
        ClassificationScope scope,
        CancellationToken cancellationToken)
    {
        var prompt = BuildClassificationPrompt(chunk, documentSummary, scope);

        for (int retry = 0; retry <= _options.MaxRetries; retry++)
        {
            try
            {
                var response = await _textCompletion!.CompleteAsync(
                    prompt, new Flux.Abstractions.TextCompletionOptions { MaxTokens = _options.MaxTokens, Temperature = _options.Temperature }, cancellationToken);

                var classification = ParseLlmResponse(response);
                classification.Source = ClassificationSource.Llm;

                // 출력 검증
                if (_validationService.ValidateOutput(classification))
                {
                    return classification;
                }

                if (_logger.IsEnabled(LogLevel.Debug))
                    LogLlmChunkClassification4(_logger, chunk.ChunkId, retry + 1, _options.MaxRetries);
            }
            catch (Exception ex)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                    LogLlmChunkClassification3(_logger, ex, chunk.ChunkId, retry + 1, _options.MaxRetries);

                if (retry == _options.MaxRetries)
                    throw;
            }
        }

        return CreateSkippedClassification("Max retries exceeded");
    }

    private async Task<Dictionary<string, ChunkClassification>> ProcessBatchWithLlmAsync(
        List<IEnrichedChunk> chunks,
        string? documentSummary,
        Dictionary<string, ClassificationValidationResult> validations,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, ChunkClassification>();

        // 배치 단위로 처리
        var totalBatches = (int)Math.Ceiling((double)chunks.Count / _options.BatchSize);
        for (int i = 0; i < chunks.Count; i += _options.BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = chunks.Skip(i).Take(_options.BatchSize).ToList();

            LogLlmChunkClassification2(_logger, (i / _options.BatchSize) + 1, totalBatches, batch.Count);

            // 개별 처리 (향후 배치 프롬프트 최적화 가능)
            foreach (var chunk in batch)
            {
                var scope = validations[chunk.ChunkId].RecommendedScope;
                var classification = await ExecuteLlmClassificationAsync(
                    chunk, documentSummary, scope, cancellationToken);
                results[chunk.ChunkId] = classification;
            }
        }

        return results;
    }

    private string BuildClassificationPrompt(
        IEnrichedChunk chunk,
        string? documentSummary,
        ClassificationScope scope)
    {
        var promptParts = new List<string>
        {
            "Analyze the following text chunk and provide classification in JSON format.",
            "",
            "Document Context:",
            $"- Title: {chunk.Source.Title}",
            $"- Type: {chunk.Source.SourceType}",
            $"- Language: {chunk.Source.Language}"
        };

        if (!string.IsNullOrEmpty(documentSummary))
        {
            promptParts.Add($"- Summary: {documentSummary}");
        }

        if (chunk.HeadingPath.Count > 0)
        {
            promptParts.Add($"- Section: {string.Join(" > ", chunk.HeadingPath)}");
        }

        promptParts.Add("");
        promptParts.Add("Text to classify:");
        promptParts.Add($"\"\"\"{chunk.Content}\"\"\"");
        promptParts.Add("");
        promptParts.Add("Provide JSON with the following fields (include only requested fields):");

        var requestedFields = new List<string>();

        if (scope.HasFlag(ClassificationScope.Topics))
            requestedFields.Add($"- \"topics\": array of {_options.MaxTopics} main topics/themes");

        if (scope.HasFlag(ClassificationScope.Categories))
            requestedFields.Add($"- \"categories\": array of {_options.MaxCategories} document categories");

        if (scope.HasFlag(ClassificationScope.Tags))
            requestedFields.Add($"- \"tags\": array of {_options.MaxTags} descriptive tags");

        if (scope.HasFlag(ClassificationScope.Keywords))
            requestedFields.Add("- \"refinedKeywords\": array of refined/improved keywords");

        if (scope.HasFlag(ClassificationScope.Summary))
            requestedFields.Add($"- \"summary\": brief summary ({_options.MaxSummaryLength} chars max)");

        if (scope.HasFlag(ClassificationScope.Questions))
            requestedFields.Add($"- \"potentialQuestions\": array of {_options.MaxQuestions} questions this text can answer");

        promptParts.AddRange(requestedFields);
        promptParts.Add("- \"confidence\": confidence score 0-1");
        promptParts.Add("");
        promptParts.Add("Return only valid JSON, no additional text.");

        return string.Join("\n", promptParts);
    }

    private ChunkClassification ParseLlmResponse(string response)
    {
        try
        {
            // JSON 부분 추출
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);

                var parsed = JsonSerializer.Deserialize<LlmClassificationResponse>(json, s_caseInsensitiveJsonOptions);

                return new ChunkClassification
                {
                    Topics = parsed?.Topics ?? new List<string>(),
                    Categories = parsed?.Categories ?? new List<string>(),
                    Tags = parsed?.Tags ?? new List<string>(),
                    RefinedKeywords = parsed?.RefinedKeywords ?? new List<string>(),
                    Summary = parsed?.Summary,
                    PotentialQuestions = parsed?.PotentialQuestions ?? new List<string>(),
                    Confidence = parsed?.Confidence ?? 0.5,
                    Source = ClassificationSource.Llm
                };
            }
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                LogLlmChunkClassification1(_logger, ex, response);
        }

        return new ChunkClassification
        {
            Confidence = 0,
            Source = ClassificationSource.Llm
        };
    }

    private static ChunkClassification CreateSkippedClassification(string? reason)
    {
        return new ChunkClassification
        {
            Source = ClassificationSource.Skipped,
            Confidence = 0,
            Summary = reason
        };
    }

    /// <summary>
    /// LLM 응답 파싱용 내부 클래스
    /// </summary>
    private sealed class LlmClassificationResponse
    {
        public List<string>? Topics { get; set; }
        public List<string>? Categories { get; set; }
        public List<string>? Tags { get; set; }
        public List<string>? RefinedKeywords { get; set; }
        public string? Summary { get; set; }
        public List<string>? PotentialQuestions { get; set; }
        public double Confidence { get; set; }
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping LLM classification for chunk {ChunkId}: {Reason}")]
    private static partial void LogLlmChunkClassification8(ILogger logger, string chunkId, string reason);
    [LoggerMessage(Level = LogLevel.Warning, Message = "LLM service not available, returning empty classification")]
    private static partial void LogLlmChunkClassification7(ILogger logger);
    [LoggerMessage(Level = LogLevel.Information, Message = "Starting batch classification for {Count} chunks")]
    private static partial void LogLlmChunkClassification6(ILogger logger, int count);
    [LoggerMessage(Level = LogLevel.Information, Message = "Batch classification complete: {LlmProcessed} LLM, {Skipped} skipped")]
    private static partial void LogLlmChunkClassification5(ILogger logger, int llmProcessed, int skipped);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Classification output validation failed for chunk {ChunkId}, retry {Retry}/{Max}")]
    private static partial void LogLlmChunkClassification4(ILogger logger, string chunkId, int retry, int max);
    [LoggerMessage(Level = LogLevel.Error, Message = "LLM classification failed for chunk {ChunkId}, retry {Retry}/{Max}")]
    private static partial void LogLlmChunkClassification3(ILogger logger, Exception exception, string chunkId, int retry, int max);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Processing batch {BatchNum}/{TotalBatches} ({Count} chunks)")]
    private static partial void LogLlmChunkClassification2(ILogger logger, int batchNum, int totalBatches, int count);
    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to parse LLM response: {Response}")]
    private static partial void LogLlmChunkClassification1(ILogger logger, Exception exception, string response);

    #endregion
}
