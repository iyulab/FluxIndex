using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Options;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.AI.OpenAI.Services;

/// <summary>
/// QuOTE (Question-Oriented Text Embeddings) 구현 서비스
/// 질문 지향적 쿼리 확장으로 검색 정확도 향상
/// </summary>
public class QuOTEService
{
    private readonly IOpenAIClient _openAIClient;
    private readonly QuOTEServiceOptions _options;
    private readonly ILogger<QuOTEService> _logger;

    public QuOTEService(
        IOpenAIClient openAIClient,
        IOptions<QuOTEServiceOptions> options,
        ILogger<QuOTEService> logger)
    {
        _openAIClient = openAIClient ?? throw new ArgumentNullException(nameof(openAIClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        ValidateOptions();
    }

    /// <summary>
    /// 질문 지향적 임베딩 생성
    /// </summary>
    public async Task<QuOTEResult> GenerateQuestionOrientedEmbeddingAsync(
        string query,
        QuOTEOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be empty", nameof(query));

        var quoteOptions = options ?? QuOTEOptions.CreateDefault();

        try
        {
            _logger.LogDebug("Generating question-oriented embeddings for query: {Query}", query);

            // 1. 쿼리 확장 생성
            var expandedQueries = await GenerateExpandedQueriesAsync(query, quoteOptions, cancellationToken);

            // 2. 관련 질문 생성
            var relatedQuestions = await GenerateRelatedQuestionsAsync(query, quoteOptions, cancellationToken);

            // 3. 쿼리별 가중치 계산
            var queryWeights = CalculateQueryWeights(query, expandedQueries, quoteOptions);

            // 4. 품질 점수 계산
            var qualityScore = EvaluateQuOTEQuality(expandedQueries, relatedQuestions, query);

            var result = new QuOTEResult
            {
                OriginalQuery = query,
                ExpandedQueries = expandedQueries,
                RelatedQuestions = relatedQuestions,
                QueryWeights = queryWeights,
                QualityScore = qualityScore
            };

            _logger.LogInformation(
                "Generated QuOTE result for query '{Query}': {ExpandedCount} expansions, {QuestionCount} questions, quality: {Quality}",
                query, expandedQueries.Count, relatedQuestions.Count, qualityScore);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate QuOTE result for query: {Query}", query);

            return new QuOTEResult
            {
                OriginalQuery = query,
                ExpandedQueries = Array.Empty<string>(),
                RelatedQuestions = Array.Empty<string>(),
                QueryWeights = new Dictionary<string, float>(),
                QualityScore = 0.0f
            };
        }
    }

    /// <summary>
    /// 확장 쿼리 생성
    /// </summary>
    private async Task<IReadOnlyList<string>> GenerateExpandedQueriesAsync(
        string originalQuery,
        QuOTEOptions options,
        CancellationToken cancellationToken)
    {
        var prompt = BuildQueryExpansionPrompt(originalQuery, options);

        var response = await _openAIClient.CompleteAsync(
            prompt,
            _options.Timeout,
            cancellationToken);

        return ParseExpandedQueries(response, options.MaxExpansions);
    }

    /// <summary>
    /// 관련 질문 생성
    /// </summary>
    private async Task<IReadOnlyList<string>> GenerateRelatedQuestionsAsync(
        string originalQuery,
        QuOTEOptions options,
        CancellationToken cancellationToken)
    {
        var prompt = BuildRelatedQuestionsPrompt(originalQuery, options);

        var response = await _openAIClient.CompleteAsync(
            prompt,
            _options.Timeout,
            cancellationToken);

        return ParseRelatedQuestions(response, options.MaxRelatedQuestions);
    }

    /// <summary>
    /// 쿼리 확장 프롬프트 구성
    /// </summary>
    private string BuildQueryExpansionPrompt(string originalQuery, QuOTEOptions options)
    {
        var domainWeights = options.DomainWeights.Any()
            ? $"\n우선 도메인: {string.Join(", ", options.DomainWeights.Keys)}"
            : "";

        return $@"다음 검색 쿼리를 {options.MaxExpansions}가지 다른 방식으로 표현해주세요.
각 표현은 원래 의도를 유지하면서도 다양한 관점에서 접근해야 합니다.

원본 쿼리: {originalQuery}{domainWeights}

요구사항:
1. 원본 쿼리와 동일한 의도 유지
2. 다양한 어휘와 표현 사용 (다양성 수준: {options.DiversityLevel:F1})
3. 각 쿼리는 한 줄로 작성
4. 번호나 특수 문자 없이 순수 질문만
5. 검색에 최적화된 명확한 표현

확장된 쿼리들:";
    }

    /// <summary>
    /// 관련 질문 프롬프트 구성
    /// </summary>
    private string BuildRelatedQuestionsPrompt(string originalQuery, QuOTEOptions options)
    {
        return $@"다음 검색 쿼리와 관련된 {options.MaxRelatedQuestions}가지 질문을 생성해주세요.
각 질문은 원본 쿼리와 관련되지만 조금 다른 각도에서 접근해야 합니다.

원본 쿼리: {originalQuery}

요구사항:
1. 원본 쿼리와 관련성 유지
2. 사용자가 함께 궁금해할 만한 질문들
3. 각 질문은 명확하고 구체적으로
4. 물음표(?)로 끝나는 완전한 질문 형태
5. 검색 가능한 형태로 구성

관련 질문들:";
    }

    /// <summary>
    /// 확장 쿼리 파싱
    /// </summary>
    private IReadOnlyList<string> ParseExpandedQueries(string response, int maxCount)
    {
        if (string.IsNullOrWhiteSpace(response))
            return Array.Empty<string>();

        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(CleanQueryLine)
            .Where(line => !string.IsNullOrWhiteSpace(line) && line.Length > 5)
            .Take(maxCount)
            .ToList();

        return lines;
    }

    /// <summary>
    /// 관련 질문 파싱
    /// </summary>
    private IReadOnlyList<string> ParseRelatedQuestions(string response, int maxCount)
    {
        if (string.IsNullOrWhiteSpace(response))
            return Array.Empty<string>();

        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(CleanQuestionLine)
            .Where(line => !string.IsNullOrWhiteSpace(line) && line.Contains('?'))
            .Take(maxCount)
            .ToList();

        return lines;
    }

    /// <summary>
    /// 쿼리 라인 정리
    /// </summary>
    private static string CleanQueryLine(string line)
    {
        // 번호 제거 (1., 2., -, * 등)
        line = System.Text.RegularExpressions.Regex.Replace(line, @"^[\d\-\*\•\>]+[\.\)\s]*", "").Trim();

        // 따옴표 제거
        line = line.Trim('"', '\'', '"', '"');

        return line;
    }

    /// <summary>
    /// 질문 라인 정리
    /// </summary>
    private static string CleanQuestionLine(string line)
    {
        line = CleanQueryLine(line);

        // 물음표가 없으면 추가
        if (!line.EndsWith('?'))
            line += "?";

        return line;
    }

    /// <summary>
    /// 쿼리별 가중치 계산
    /// </summary>
    private Dictionary<string, float> CalculateQueryWeights(
        string originalQuery,
        IReadOnlyList<string> expandedQueries,
        QuOTEOptions options)
    {
        var weights = new Dictionary<string, float>();

        if (expandedQueries.Count == 0)
            return weights;

        // 원본 쿼리에 가장 높은 가중치
        var baseWeight = 1.0f / (expandedQueries.Count + 1);
        var originalWeight = baseWeight * 1.5f; // 원본에 50% 추가 가중치

        // 도메인 가중치 적용
        foreach (var query in expandedQueries)
        {
            var weight = baseWeight;

            // 도메인별 가중치 적용
            foreach (var (domain, domainWeight) in options.DomainWeights)
            {
                if (query.ToLowerInvariant().Contains(domain.ToLowerInvariant()))
                {
                    weight *= domainWeight;
                    break;
                }
            }

            weights[query] = weight;
        }

        // 가중치 정규화
        var totalWeight = weights.Values.Sum() + originalWeight;
        var normalizedWeights = weights.ToDictionary(
            kv => kv.Key,
            kv => kv.Value / totalWeight);

        return normalizedWeights;
    }

    /// <summary>
    /// QuOTE 품질 평가
    /// </summary>
    private float EvaluateQuOTEQuality(
        IReadOnlyList<string> expandedQueries,
        IReadOnlyList<string> relatedQuestions,
        string originalQuery)
    {
        var score = 0.3f; // 기본 점수

        // 확장 쿼리 품질
        if (expandedQueries.Count > 0)
        {
            score += 0.3f;

            // 다양성 평가 (길이 및 어휘 다양성)
            var avgLength = expandedQueries.Average(q => q.Length);
            var lengthVariation = expandedQueries.Select(q => Math.Abs(q.Length - avgLength)).Average();

            if (lengthVariation > 10) // 길이가 다양하면 좋음
                score += 0.1f;
        }

        // 관련 질문 품질
        if (relatedQuestions.Count > 0)
        {
            score += 0.2f;

            // 모든 질문이 물음표로 끝나는지 확인
            if (relatedQuestions.All(q => q.EndsWith('?')))
                score += 0.1f;
        }

        // 전체적인 관련성 (간단한 키워드 매칭)
        var originalWords = originalQuery.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var allGeneratedText = string.Join(" ", expandedQueries.Concat(relatedQuestions)).ToLowerInvariant();
        var relevantWordsCount = originalWords.Count(word => allGeneratedText.Contains(word));

        if (originalWords.Length > 0)
        {
            var relevanceRatio = (float)relevantWordsCount / originalWords.Length;
            score += relevanceRatio * 0.1f;
        }

        return Math.Clamp(score, 0.0f, 1.0f);
    }

    /// <summary>
    /// 옵션 유효성 검증
    /// </summary>
    private void ValidateOptions()
    {
        if (!_options.IsValid)
            throw new ArgumentException("Invalid QuOTEServiceOptions configuration");
    }

    /// <summary>
    /// 테스트용 팩토리 메서드
    /// </summary>
    public static QuOTEService CreateForTesting(
        IOpenAIClient mockClient,
        QuOTEServiceOptions? options = null,
        ILogger<QuOTEService>? logger = null)
    {
        return new QuOTEService(
            mockClient,
            Microsoft.Extensions.Options.Options.Create(options ?? QuOTEServiceOptions.CreateForTesting()),
            logger ?? new Microsoft.Extensions.Logging.Abstractions.NullLogger<QuOTEService>());
    }
}

/// <summary>
/// QuOTE 서비스 옵션
/// </summary>
public class QuOTEServiceOptions
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
    /// 기본 확장 개수
    /// </summary>
    public int DefaultMaxExpansions { get; set; } = 3;

    /// <summary>
    /// 기본 관련 질문 개수
    /// </summary>
    public int DefaultMaxRelatedQuestions { get; set; } = 5;

    /// <summary>
    /// 기본 다양성 수준
    /// </summary>
    public float DefaultDiversityLevel { get; set; } = 0.7f;

    /// <summary>
    /// 품질 임계값
    /// </summary>
    public float QualityThreshold { get; set; } = 0.4f;

    /// <summary>
    /// 동시 처리 제한
    /// </summary>
    public int MaxConcurrency { get; set; } = 3;

    /// <summary>
    /// 디버그 로깅 활성화
    /// </summary>
    public bool EnableDebugLogging { get; set; } = false;

    /// <summary>
    /// 테스트용 설정
    /// </summary>
    public static QuOTEServiceOptions CreateForTesting() => new()
    {
        Timeout = TimeSpan.FromSeconds(10),
        MaxRetries = 1,
        DefaultMaxExpansions = 2,
        DefaultMaxRelatedQuestions = 3,
        DefaultDiversityLevel = 0.6f,
        QualityThreshold = 0.3f,
        MaxConcurrency = 1,
        EnableDebugLogging = true
    };

    /// <summary>
    /// 운영용 설정
    /// </summary>
    public static QuOTEServiceOptions CreateForProduction() => new()
    {
        Timeout = TimeSpan.FromSeconds(45),
        MaxRetries = 3,
        DefaultMaxExpansions = 4,
        DefaultMaxRelatedQuestions = 6,
        DefaultDiversityLevel = 0.8f,
        QualityThreshold = 0.5f,
        MaxConcurrency = 5,
        EnableDebugLogging = false
    };

    /// <summary>
    /// 설정 유효성 검증
    /// </summary>
    public bool IsValid =>
        Timeout > TimeSpan.Zero &&
        MaxRetries >= 0 &&
        DefaultMaxExpansions > 0 &&
        DefaultMaxRelatedQuestions > 0 &&
        DefaultDiversityLevel >= 0.0f && DefaultDiversityLevel <= 1.0f &&
        QualityThreshold >= 0.0f && QualityThreshold <= 1.0f &&
        MaxConcurrency > 0;
}