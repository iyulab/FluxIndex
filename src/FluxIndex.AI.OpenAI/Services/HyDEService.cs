using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Options;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.AI.OpenAI.Services;

/// <summary>
/// HyDE (Hypothetical Document Embeddings) 구현 서비스
/// 쿼리에 대한 가상의 답변 문서를 생성하여 검색 품질 향상
/// </summary>
public class HyDEService
{
    private readonly IOpenAIClient _openAIClient;
    private readonly HyDEServiceOptions _options;
    private readonly ILogger<HyDEService> _logger;

    public HyDEService(
        IOpenAIClient openAIClient,
        IOptions<HyDEServiceOptions> options,
        ILogger<HyDEService> logger)
    {
        _openAIClient = openAIClient ?? throw new ArgumentNullException(nameof(openAIClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        ValidateOptions();
    }

    /// <summary>
    /// 가상 문서 생성
    /// </summary>
    public async Task<HyDEResult> GenerateHypotheticalDocumentAsync(
        string query,
        HyDEOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be empty", nameof(query));

        var hydeOptions = options ?? HyDEOptions.CreateDefault();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogDebug("Generating hypothetical document for query: {Query}", query);

            var prompt = BuildHyDEPrompt(query, hydeOptions);

            var response = await _openAIClient.CompleteAsync(
                prompt,
                _options.Timeout,
                cancellationToken);

            var hypotheticalDocument = ExtractDocumentFromResponse(response);
            var qualityScore = EvaluateDocumentQuality(hypotheticalDocument, query);

            stopwatch.Stop();

            var result = new HyDEResult
            {
                OriginalQuery = query,
                HypotheticalDocument = hypotheticalDocument,
                QualityScore = qualityScore,
                TokensUsed = EstimateTokenUsage(prompt, response),
                GenerationTimeMs = stopwatch.ElapsedMilliseconds
            };

            if (result.IsSuccessful)
            {
                _logger.LogInformation(
                    "Successfully generated hypothetical document for query '{Query}' " +
                    "with quality score {QualityScore} in {Duration}ms",
                    query, qualityScore, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogWarning(
                    "Generated document quality below threshold for query '{Query}': {QualityScore}",
                    query, qualityScore);
            }

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Failed to generate hypothetical document for query: {Query}", query);

            return new HyDEResult
            {
                OriginalQuery = query,
                HypotheticalDocument = string.Empty,
                QualityScore = 0.0f,
                TokensUsed = 0,
                GenerationTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    /// <summary>
    /// HyDE 프롬프트 구성
    /// </summary>
    private string BuildHyDEPrompt(string query, HyDEOptions options)
    {
        var domainContext = !string.IsNullOrWhiteSpace(options.DomainContext)
            ? $"\n도메인 컨텍스트: {options.DomainContext}"
            : "";

        var styleInstruction = options.DocumentStyle switch
        {
            "academic" => "학술적이고 정확한 형식으로",
            "technical" => "기술적이고 전문적인 형식으로",
            "conversational" => "대화체로 친근하게",
            _ => "정보 전달에 중점을 두어"
        };

        return $@"다음 질문에 대한 가상의 답변 문서를 {styleInstruction} 작성해주세요.

질문: {query}{domainContext}

요구사항:
1. 질문에 직접적으로 답변하는 내용
2. 구체적이고 실용적인 정보 포함
3. 최대 {options.MaxLength}자 이내
4. 정확하고 신뢰할 수 있는 톤으로 작성
5. 불필요한 서론이나 결론 없이 핵심 내용만

답변 문서:";
    }

    /// <summary>
    /// 응답에서 문서 내용 추출
    /// </summary>
    private static string ExtractDocumentFromResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return string.Empty;

        // 응답 정리
        var cleaned = response.Trim();

        // 공통 패턴 제거
        var patterns = new[]
        {
            "답변 문서:",
            "답변:",
            "문서:",
            "내용:"
        };

        foreach (var pattern in patterns)
        {
            if (cleaned.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[pattern.Length..].Trim();
            }
        }

        return cleaned;
    }

    /// <summary>
    /// 생성된 문서의 품질 평가
    /// </summary>
    private float EvaluateDocumentQuality(string document, string originalQuery)
    {
        if (string.IsNullOrWhiteSpace(document))
            return 0.0f;

        var score = 0.5f; // 기본 점수

        // 길이 평가 (너무 짧거나 길면 감점)
        if (document.Length >= 50 && document.Length <= 500)
            score += 0.2f;
        else if (document.Length < 20)
            score -= 0.3f;

        // 구조 평가 (문장 완성도)
        var sentences = document.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
        if (sentences.Length >= 2)
            score += 0.1f;

        // 쿼리 관련성 평가 (간단한 키워드 매칭)
        var queryWords = originalQuery.ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var documentLower = document.ToLowerInvariant();

        var matchCount = 0;
        foreach (var word in queryWords)
        {
            if (word.Length > 2 && documentLower.Contains(word))
                matchCount++;
        }

        if (queryWords.Length > 0)
        {
            var relevanceRatio = (float)matchCount / queryWords.Length;
            score += relevanceRatio * 0.2f;
        }

        return Math.Clamp(score, 0.0f, 1.0f);
    }

    /// <summary>
    /// 토큰 사용량 추정
    /// </summary>
    private static int EstimateTokenUsage(string prompt, string response)
    {
        // 간단한 토큰 추정 (한국어 기준)
        var totalText = prompt + response;
        return (int)(totalText.Length / 3.5); // 한국어는 영어보다 토큰 효율이 좋음
    }

    /// <summary>
    /// 옵션 유효성 검증
    /// </summary>
    private void ValidateOptions()
    {
        if (_options.Timeout <= TimeSpan.Zero)
            throw new ArgumentException("Timeout must be positive", nameof(_options.Timeout));

        if (_options.MaxRetries < 0)
            throw new ArgumentException("MaxRetries cannot be negative", nameof(_options.MaxRetries));
    }

    /// <summary>
    /// 테스트용 팩토리 메서드
    /// </summary>
    public static HyDEService CreateForTesting(
        IOpenAIClient mockClient,
        HyDEServiceOptions? options = null,
        ILogger<HyDEService>? logger = null)
    {
        return new HyDEService(
            mockClient,
            Microsoft.Extensions.Options.Options.Create(options ?? HyDEServiceOptions.CreateForTesting()),
            logger ?? new Microsoft.Extensions.Logging.Abstractions.NullLogger<HyDEService>());
    }
}

/// <summary>
/// HyDE 서비스 옵션
/// </summary>
public class HyDEServiceOptions
{
    /// <summary>
    /// API 호출 타임아웃
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 최대 재시도 횟수
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>
    /// 기본 문서 스타일
    /// </summary>
    public string DefaultDocumentStyle { get; set; } = "informative";

    /// <summary>
    /// 기본 최대 길이
    /// </summary>
    public int DefaultMaxLength { get; set; } = 300;

    /// <summary>
    /// 품질 임계값
    /// </summary>
    public float QualityThreshold { get; set; } = 0.4f;

    /// <summary>
    /// 디버그 로깅 활성화
    /// </summary>
    public bool EnableDebugLogging { get; set; } = false;

    /// <summary>
    /// 테스트용 설정
    /// </summary>
    public static HyDEServiceOptions CreateForTesting() => new()
    {
        Timeout = TimeSpan.FromSeconds(10),
        MaxRetries = 1,
        DefaultMaxLength = 200,
        QualityThreshold = 0.3f,
        EnableDebugLogging = true
    };

    /// <summary>
    /// 운영용 설정
    /// </summary>
    public static HyDEServiceOptions CreateForProduction() => new()
    {
        Timeout = TimeSpan.FromSeconds(45),
        MaxRetries = 3,
        DefaultMaxLength = 400,
        QualityThreshold = 0.5f,
        EnableDebugLogging = false
    };

    /// <summary>
    /// 설정 유효성 검증
    /// </summary>
    public bool IsValid =>
        Timeout > TimeSpan.Zero &&
        MaxRetries >= 0 &&
        DefaultMaxLength > 0 &&
        QualityThreshold >= 0.0f && QualityThreshold <= 1.0f;
}