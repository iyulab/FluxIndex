using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Options;
using FluxIndex.Core.Domain.ValueObjects;
using FluxIndex.AI.OpenAI.Parsers;
using FluxIndex.AI.OpenAI.Prompts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.AI.OpenAI.Services;

/// <summary>
/// OpenAI 기반 메타데이터 추출 서비스
/// 테스트 가능성과 오류 복구를 위한 강력한 설계
/// </summary>
public class OpenAIMetadataEnrichmentService : IMetadataEnrichmentService
{
    private readonly IOpenAIClient _openAIClient;
    private readonly MetadataJsonParser _parser;
    private readonly MetadataExtractionOptions _options;
    private readonly ILogger<OpenAIMetadataEnrichmentService> _logger;
    private readonly MetadataExtractionStatistics _statistics;

    public OpenAIMetadataEnrichmentService(
        IOpenAIClient openAIClient,
        IOptions<MetadataExtractionOptions> options,
        ILogger<OpenAIMetadataEnrichmentService> logger)
    {
        _openAIClient = openAIClient ?? throw new ArgumentNullException(nameof(openAIClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _parser = new MetadataJsonParser();
        _statistics = new MetadataExtractionStatistics();

        ValidateOptions();
    }

    /// <summary>
    /// 테스트용 생성자 (의존성 주입 없이)
    /// </summary>
    internal OpenAIMetadataEnrichmentService(
        IOpenAIClient openAIClient,
        MetadataExtractionOptions options,
        ILogger<OpenAIMetadataEnrichmentService> logger)
    {
        _openAIClient = openAIClient ?? throw new ArgumentNullException(nameof(openAIClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _parser = new MetadataJsonParser();
        _statistics = new MetadataExtractionStatistics();

        ValidateOptions();
    }

    /// <summary>
    /// 단일 텍스트 청크에서 메타데이터 추출
    /// </summary>
    public async Task<ChunkMetadata> ExtractMetadataAsync(
        string content,
        string? context = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content cannot be empty", nameof(content));

        var stopwatch = Stopwatch.StartNew();
        var attempt = 0;

        while (attempt <= _options.MaxRetries)
        {
            try
            {
                _logger.LogDebug("Extracting metadata (attempt {Attempt})", attempt + 1);

                var prompt = new MetadataPrompts.PromptBuilder()
                    .WithContent(content)
                    .WithContext(context)
                    .Build();

                var response = await _openAIClient.CompleteAsync(
                    prompt,
                    _options.Timeout,
                    cancellationToken);

                var metadata = _parser.ParseMetadata(response, "OpenAI");

                // 품질 검증
                var validation = MetadataJsonParser.ValidateMetadata(metadata);
                if (!validation.IsValid && validation.QualityScore < _options.MinQualityThreshold)
                {
                    if (attempt < _options.MaxRetries)
                    {
                        _logger.LogWarning("Low quality metadata (score: {Score}), retrying",
                            validation.QualityScore);
                        attempt++;
                        await Task.Delay(1000 * attempt, cancellationToken);
                        continue;
                    }

                    _logger.LogWarning("Final attempt produced low quality metadata: {Issues}",
                        string.Join(", ", validation.Issues));
                }

                stopwatch.Stop();
                RecordSuccessfulExtraction(stopwatch.ElapsedMilliseconds, metadata.QualityScore);

                _logger.LogInformation("Metadata extracted successfully in {Duration}ms",
                    stopwatch.ElapsedMilliseconds);

                return metadata;
            }
            catch (JsonException ex)
            {
                if (attempt >= _options.MaxRetries)
                {
                    stopwatch.Stop();
                    RecordFailedExtraction(stopwatch.ElapsedMilliseconds);
                    throw new MetadataExtractionException(
                        $"Failed to parse JSON after {_options.MaxRetries + 1} attempts: {ex.Message}",
                        MetadataExtractionErrorType.InvalidResponse,
                        false,
                        ex);
                }

                _logger.LogWarning(ex, "JSON parsing failed (attempt {Attempt}), retrying", attempt + 1);
                attempt++;
                await Task.Delay(1000 * attempt, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                RecordFailedExtraction(stopwatch.ElapsedMilliseconds);
                throw new MetadataExtractionException(
                    "Operation was cancelled",
                    MetadataExtractionErrorType.TimeoutError,
                    true);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                RecordFailedExtraction(stopwatch.ElapsedMilliseconds);

                var errorType = DetermineErrorType(ex);
                var isRetryable = IsRetryableError(errorType);

                throw new MetadataExtractionException(
                    $"Metadata extraction failed: {ex.Message}",
                    errorType,
                    isRetryable,
                    ex);
            }
        }

        // 모든 재시도 실패
        stopwatch.Stop();
        RecordFailedExtraction(stopwatch.ElapsedMilliseconds);
        throw new MetadataExtractionException(
            $"Failed to extract metadata after {_options.MaxRetries + 1} attempts",
            MetadataExtractionErrorType.ServiceUnavailable,
            true);
    }

    /// <summary>
    /// 배치 처리로 여러 텍스트 청크에서 메타데이터 추출
    /// </summary>
    public async Task<IReadOnlyList<ChunkMetadata>> ExtractBatchAsync(
        IReadOnlyList<string> contents,
        BatchProcessingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (contents == null || contents.Count == 0)
            throw new ArgumentException("Contents cannot be null or empty", nameof(contents));

        var batchOptions = options ?? new BatchProcessingOptions();
        var results = new List<ChunkMetadata>();
        var semaphore = new SemaphoreSlim(_options.MaxConcurrency, _options.MaxConcurrency);

        _logger.LogInformation("Starting batch extraction for {Count} items", contents.Count);

        // 배치를 청크로 나누어 처리
        for (int i = 0; i < contents.Count; i += batchOptions.Size)
        {
            var batch = contents.Skip(i).Take(batchOptions.Size).ToList();

            try
            {
                // 배치 프롬프트 생성
                var prompt = new MetadataPrompts.PromptBuilder()
                    .WithTemplate(MetadataPrompts.BatchExtractionPrompt)
                    .WithChunks(batch)
                    .Build();

                var response = await _openAIClient.CompleteAsync(
                    prompt,
                    _options.Timeout,
                    cancellationToken);

                var batchResults = _parser.ParseBatchMetadata(response, batch.Count, "OpenAI");
                results.AddRange(batchResults);

                batchOptions.ProgressCallback?.Invoke(results.Count, contents.Count);

                _logger.LogDebug("Processed batch {Start}-{End}", i + 1, Math.Min(i + batchOptions.Size, contents.Count));

                // 배치 간 지연
                if (i + batchOptions.Size < contents.Count)
                {
                    await Task.Delay(batchOptions.DelayBetweenBatches, cancellationToken);
                }
            }
            catch (Exception ex) when (batchOptions.ContinueOnFailure)
            {
                _logger.LogError(ex, "Batch {Start}-{End} failed, creating fallback metadata",
                    i + 1, Math.Min(i + batchOptions.Size, contents.Count));

                // 실패한 배치에 대해 개별 처리 시도
                var fallbackTasks = batch.Select(async content =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        return await ExtractMetadataAsync(content, cancellationToken: cancellationToken);
                    }
                    catch (Exception individualEx)
                    {
                        _logger.LogWarning(individualEx, "Individual extraction failed, using minimal metadata");
                        return CreateFallbackMetadata(content);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                var fallbackResults = await Task.WhenAll(fallbackTasks);
                results.AddRange(fallbackResults);
            }
        }

        _logger.LogInformation("Batch extraction completed: {Total} items processed", results.Count);
        return results;
    }

    /// <summary>
    /// 사용자 정의 스키마를 사용한 메타데이터 추출
    /// </summary>
    public async Task<ChunkMetadata> ExtractWithSchemaAsync<T>(
        string content,
        T schema,
        CancellationToken cancellationToken = default) where T : class
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content cannot be empty", nameof(content));

        if (schema == null)
            throw new ArgumentNullException(nameof(schema));

        var prompt = new MetadataPrompts.PromptBuilder()
            .WithTemplate(MetadataPrompts.DomainSpecificPrompt)
            .WithContent(content)
            .WithDomain(typeof(T).Name)
            .WithPlaceholder("schema", JsonSerializer.Serialize(schema))
            .Build();

        var response = await _openAIClient.CompleteAsync(
            prompt,
            _options.Timeout,
            cancellationToken);

        return _parser.ParseMetadata(response, $"OpenAI-{typeof(T).Name}");
    }

    /// <summary>
    /// 서비스 상태 확인
    /// </summary>
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var testContent = "This is a health check test content for metadata extraction service.";
            var metadata = await ExtractMetadataAsync(testContent, cancellationToken: cancellationToken);
            return metadata.IsValid;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 서비스 사용 통계 조회
    /// </summary>
    public Task<MetadataExtractionStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_statistics);
    }

    /// <summary>
    /// 옵션 유효성 검증
    /// </summary>
    private void ValidateOptions()
    {
        if (!_options.IsValid)
        {
            throw new ArgumentException("Invalid MetadataExtractionOptions configuration");
        }
    }

    /// <summary>
    /// 오류 타입 결정
    /// </summary>
    private static MetadataExtractionErrorType DetermineErrorType(Exception ex) => ex switch
    {
        ArgumentException => MetadataExtractionErrorType.InvalidInput,
        TimeoutException => MetadataExtractionErrorType.TimeoutError,
        OperationCanceledException => MetadataExtractionErrorType.TimeoutError,
        UnauthorizedAccessException => MetadataExtractionErrorType.AuthenticationError,
        JsonException => MetadataExtractionErrorType.InvalidResponse,
        _ => MetadataExtractionErrorType.Unknown
    };

    /// <summary>
    /// 재시도 가능 오류 판단
    /// </summary>
    private static bool IsRetryableError(MetadataExtractionErrorType errorType) => errorType switch
    {
        MetadataExtractionErrorType.ServiceUnavailable => true,
        MetadataExtractionErrorType.TimeoutError => true,
        MetadataExtractionErrorType.NetworkError => true,
        MetadataExtractionErrorType.InvalidResponse => true,
        _ => false
    };

    /// <summary>
    /// 성공한 추출 기록
    /// </summary>
    private void RecordSuccessfulExtraction(long durationMs, float qualityScore)
    {
        // 통계 업데이트 로직 (실제 구현에서는 스레드 안전성 고려)
        _logger.LogDebug("Recorded successful extraction: {Duration}ms, quality: {Quality}",
            durationMs, qualityScore);
    }

    /// <summary>
    /// 실패한 추출 기록
    /// </summary>
    private void RecordFailedExtraction(long durationMs)
    {
        // 통계 업데이트 로직
        _logger.LogDebug("Recorded failed extraction: {Duration}ms", durationMs);
    }

    /// <summary>
    /// 폴백 메타데이터 생성 (추출 실패 시)
    /// </summary>
    private static ChunkMetadata CreateFallbackMetadata(string content)
    {
        var truncatedContent = content.Length > 50 ? content[..50] + "..." : content;

        return ChunkMetadata.Builder()
            .WithTitle($"Content: {truncatedContent}")
            .WithSummary("Metadata extraction failed, minimal information available")
            .WithKeywords("fallback", "extraction-failed")
            .WithQualityScore(0.1f)
            .WithConfidence(ConfidenceLevel.Low)
            .Build();
    }

    /// <summary>
    /// 테스트용 팩토리 메서드
    /// </summary>
    public static OpenAIMetadataEnrichmentService CreateForTesting(
        IOpenAIClient mockClient,
        MetadataExtractionOptions? options = null,
        ILogger<OpenAIMetadataEnrichmentService>? logger = null)
    {
        return new OpenAIMetadataEnrichmentService(
            mockClient,
            options ?? MetadataExtractionOptions.CreateForTesting(),
            logger ?? new Microsoft.Extensions.Logging.Abstractions.NullLogger<OpenAIMetadataEnrichmentService>());
    }
}