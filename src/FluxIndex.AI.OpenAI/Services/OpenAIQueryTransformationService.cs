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
/// OpenAI 기반 쿼리 변환 서비스 통합 구현
/// HyDE, QuOTE, 쿼리 분해, 의도 분석 등 모든 기능 제공
/// </summary>
public class OpenAIQueryTransformationService : IQueryTransformationService
{
    private readonly HyDEService _hydeService;
    private readonly QuOTEService _quoteService;
    private readonly IOpenAIClient _openAIClient;
    private readonly QueryTransformationOptions _options;
    private readonly ILogger<OpenAIQueryTransformationService> _logger;

    public OpenAIQueryTransformationService(
        HyDEService hydeService,
        QuOTEService quoteService,
        IOpenAIClient openAIClient,
        IOptions<QueryTransformationOptions> options,
        ILogger<OpenAIQueryTransformationService> logger)
    {
        _hydeService = hydeService ?? throw new ArgumentNullException(nameof(hydeService));
        _quoteService = quoteService ?? throw new ArgumentNullException(nameof(quoteService));
        _openAIClient = openAIClient ?? throw new ArgumentNullException(nameof(openAIClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// HyDE: 가상 문서 생성
    /// </summary>
    public Task<HyDEResult> GenerateHypotheticalDocumentAsync(
        string query,
        HyDEOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return _hydeService.GenerateHypotheticalDocumentAsync(query, options, cancellationToken);
    }

    /// <summary>
    /// QuOTE: 질문 지향 임베딩
    /// </summary>
    public Task<QuOTEResult> GenerateQuestionOrientedEmbeddingAsync(
        string query,
        QuOTEOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return _quoteService.GenerateQuestionOrientedEmbeddingAsync(query, options, cancellationToken);
    }

    /// <summary>
    /// 다중 쿼리 생성
    /// </summary>
    public async Task<IReadOnlyList<string>> GenerateMultipleQueriesAsync(
        string query,
        int count = 3,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be empty", nameof(query));

        if (count <= 0 || count > 10)
            throw new ArgumentOutOfRangeException(nameof(count), "Count must be between 1 and 10");

        try
        {
            _logger.LogDebug("Generating {Count} multiple queries for: {Query}", count, query);

            var prompt = BuildMultipleQueriesPrompt(query, count);

            var response = await _openAIClient.CompleteAsync(
                prompt,
                _options.Timeout,
                cancellationToken);

            var queries = ParseMultipleQueries(response, count);

            _logger.LogInformation("Generated {ActualCount} queries from original query: {Query}",
                queries.Count, query);

            return queries;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate multiple queries for: {Query}", query);
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// 쿼리 분해
    /// </summary>
    public async Task<QueryDecompositionResult> DecomposeQueryAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be empty", nameof(query));

        try
        {
            _logger.LogDebug("Decomposing complex query: {Query}", query);

            var prompt = BuildQueryDecompositionPrompt(query);

            var response = await _openAIClient.CompleteAsync(
                prompt,
                _options.Timeout,
                cancellationToken);

            var result = ParseQueryDecomposition(response, query);

            _logger.LogInformation("Decomposed query '{Query}' into {SubQueryCount} sub-queries",
                query, result.SubQueries.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decompose query: {Query}", query);

            return new QueryDecompositionResult
            {
                OriginalQuery = query,
                SubQueries = Array.Empty<SubQuery>(),
                Relationship = QueryRelationshipType.Independent
            };
        }
    }

    /// <summary>
    /// 쿼리 의도 분석
    /// </summary>
    public async Task<QueryIntentResult> AnalyzeQueryIntentAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be empty", nameof(query));

        try
        {
            _logger.LogDebug("Analyzing query intent: {Query}", query);

            var prompt = BuildQueryIntentPrompt(query);

            var response = await _openAIClient.CompleteAsync(
                prompt,
                _options.Timeout,
                cancellationToken);

            var result = ParseQueryIntent(response, query);

            _logger.LogInformation("Analyzed query '{Query}' - Intent: {Intent}, Complexity: {Complexity}",
                query, result.PrimaryIntent, result.Complexity);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze query intent: {Query}", query);

            return new QueryIntentResult
            {
                Query = query,
                PrimaryIntent = QueryIntent.Informational,
                SecondaryIntents = Array.Empty<QueryIntent>(),
                Confidence = new Dictionary<QueryIntent, float>(),
                Domain = "Unknown",
                Complexity = QueryComplexity.Simple
            };
        }
    }

    /// <summary>
    /// 서비스 상태 확인
    /// </summary>
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var testQuery = "테스트 쿼리";
            var result = await GenerateMultipleQueriesAsync(testQuery, 1, cancellationToken);
            return result.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 다중 쿼리 생성 프롬프트
    /// </summary>
    private string BuildMultipleQueriesPrompt(string query, int count)
    {
        return $@"다음 검색 쿼리를 {count}가지 다른 방식으로 표현해주세요.
각 표현은 동일한 정보를 찾기 위한 것이지만, 다양한 관점과 어휘를 사용해야 합니다.

원본 쿼리: {query}

요구사항:
1. 원본과 동일한 검색 의도 유지
2. 서로 다른 어휘와 표현 방식 사용
3. 각 쿼리는 한 줄로 작성
4. 번호나 불필요한 텍스트 없이 순수 쿼리만
5. 자연스럽고 검색에 최적화된 표현

변형된 쿼리들:";
    }

    /// <summary>
    /// 쿼리 분해 프롬프트
    /// </summary>
    private string BuildQueryDecompositionPrompt(string query)
    {
        return $@"다음 복합 쿼리를 더 간단한 하위 쿼리들로 분해하여 JSON 형태로 반환해주세요.

원본 쿼리: {query}

JSON 스키마:
{{
  ""sub_queries"": [
    {{
      ""text"": ""하위 쿼리 텍스트"",
      ""importance"": 0.8,
      ""type"": ""factual|procedural|conceptual|comparative|problemSolving""
    }}
  ],
  ""relationship"": ""independent|sequential|conjunction|disjunction|hierarchical""
}}

요구사항:
1. 원본 쿼리의 모든 정보 요구사항을 포함
2. 각 하위 쿼리는 독립적으로 검색 가능
3. 중요도는 0.0~1.0 범위
4. 관계 타입은 하위 쿼리들 간의 논리적 관계

JSON 응답:";
    }

    /// <summary>
    /// 쿼리 의도 분석 프롬프트
    /// </summary>
    private string BuildQueryIntentPrompt(string query)
    {
        return $@"다음 검색 쿼리의 의도를 분석하여 JSON 형태로 반환해주세요.

쿼리: {query}

JSON 스키마:
{{
  ""primary_intent"": ""informational|navigational|transactional|troubleshooting|educational|comparative"",
  ""secondary_intents"": [""intent1"", ""intent2""],
  ""confidence"": {{
    ""informational"": 0.8,
    ""educational"": 0.3
  }},
  ""domain"": ""technology|business|health|education|general"",
  ""complexity"": ""simple|moderate|complex|veryComplex""
}}

분석 기준:
1. 주요 의도: 사용자가 가장 원하는 것
2. 보조 의도: 추가로 관심있을 수 있는 것들
3. 신뢰도: 각 의도에 대한 확신 정도 (0.0~1.0)
4. 도메인: 주제 분야
5. 복잡도: 답변에 필요한 추론 수준

JSON 응답:";
    }

    /// <summary>
    /// 다중 쿼리 파싱
    /// </summary>
    private IReadOnlyList<string> ParseMultipleQueries(string response, int maxCount)
    {
        if (string.IsNullOrWhiteSpace(response))
            return Array.Empty<string>();

        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(CleanQueryLine)
            .Where(line => !string.IsNullOrWhiteSpace(line) && line.Length > 3)
            .Take(maxCount)
            .ToList();

        return lines;
    }

    /// <summary>
    /// 쿼리 분해 결과 파싱
    /// </summary>
    private QueryDecompositionResult ParseQueryDecomposition(string response, string originalQuery)
    {
        try
        {
            var cleanedJson = ExtractJsonFromResponse(response);
            var jsonDoc = JsonDocument.Parse(cleanedJson);
            var root = jsonDoc.RootElement;

            var subQueries = new List<SubQuery>();

            if (root.TryGetProperty("sub_queries", out var subQueriesArray) &&
                subQueriesArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in subQueriesArray.EnumerateArray())
                {
                    if (item.TryGetProperty("text", out var textElement))
                    {
                        var subQuery = new SubQuery
                        {
                            Text = textElement.GetString() ?? string.Empty,
                            Importance = GetFloatProperty(item, "importance", 0.5f),
                            Type = ParseQueryType(GetStringProperty(item, "type", "factual"))
                        };

                        if (!string.IsNullOrWhiteSpace(subQuery.Text))
                            subQueries.Add(subQuery);
                    }
                }
            }

            var relationship = ParseRelationshipType(GetStringProperty(root, "relationship", "independent"));

            return new QueryDecompositionResult
            {
                OriginalQuery = originalQuery,
                SubQueries = subQueries,
                Relationship = relationship
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse query decomposition JSON, using fallback");

            // 폴백: 간단한 텍스트 분해
            return CreateFallbackDecomposition(originalQuery);
        }
    }

    /// <summary>
    /// 쿼리 의도 분석 결과 파싱
    /// </summary>
    private QueryIntentResult ParseQueryIntent(string response, string originalQuery)
    {
        try
        {
            var cleanedJson = ExtractJsonFromResponse(response);
            var jsonDoc = JsonDocument.Parse(cleanedJson);
            var root = jsonDoc.RootElement;

            var primaryIntent = ParseQueryIntent(GetStringProperty(root, "primary_intent", "informational"));

            var secondaryIntents = new List<QueryIntent>();
            if (root.TryGetProperty("secondary_intents", out var secondaryArray) &&
                secondaryArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in secondaryArray.EnumerateArray())
                {
                    var intent = ParseQueryIntent(item.GetString() ?? "");
                    if (intent != QueryIntent.Informational || primaryIntent != intent)
                        secondaryIntents.Add(intent);
                }
            }

            var confidence = new Dictionary<QueryIntent, float>();
            if (root.TryGetProperty("confidence", out var confidenceObj) &&
                confidenceObj.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in confidenceObj.EnumerateObject())
                {
                    var intent = ParseQueryIntent(prop.Name);
                    var conf = prop.Value.GetSingle();
                    confidence[intent] = Math.Clamp(conf, 0.0f, 1.0f);
                }
            }

            var domain = GetStringProperty(root, "domain", "general");
            var complexity = ParseQueryComplexity(GetStringProperty(root, "complexity", "simple"));

            return new QueryIntentResult
            {
                Query = originalQuery,
                PrimaryIntent = primaryIntent,
                SecondaryIntents = secondaryIntents,
                Confidence = confidence,
                Domain = domain,
                Complexity = complexity
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse query intent JSON, using fallback");

            return new QueryIntentResult
            {
                Query = originalQuery,
                PrimaryIntent = QueryIntent.Informational,
                SecondaryIntents = Array.Empty<QueryIntent>(),
                Confidence = new Dictionary<QueryIntent, float> { { QueryIntent.Informational, 0.7f } },
                Domain = "general",
                Complexity = QueryComplexity.Simple
            };
        }
    }

    /// <summary>
    /// JSON 응답에서 JSON 부분 추출
    /// </summary>
    private static string ExtractJsonFromResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return "{}";

        // JSON 객체 패턴 찾기
        var jsonStart = response.IndexOf('{');
        var jsonEnd = response.LastIndexOf('}');

        if (jsonStart >= 0 && jsonEnd > jsonStart)
        {
            return response[jsonStart..(jsonEnd + 1)];
        }

        return "{}";
    }

    /// <summary>
    /// 쿼리 라인 정리
    /// </summary>
    private static string CleanQueryLine(string line)
    {
        // 번호, 특수문자 제거
        line = System.Text.RegularExpressions.Regex.Replace(line, @"^[\d\-\*\•\>]+[\.\)\s]*", "").Trim();
        line = line.Trim('"', '\'', '"', '"');
        return line;
    }

    /// <summary>
    /// 폴백 쿼리 분해
    /// </summary>
    private QueryDecompositionResult CreateFallbackDecomposition(string originalQuery)
    {
        // 간단한 키워드 기반 분해
        var words = originalQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length <= 2)
        {
            return new QueryDecompositionResult
            {
                OriginalQuery = originalQuery,
                SubQueries = Array.Empty<SubQuery>(),
                Relationship = QueryRelationshipType.Independent
            };
        }

        var subQueries = words.Select((word, index) => new SubQuery
        {
            Text = word,
            Importance = 1.0f / words.Length,
            Type = QueryType.Factual
        }).ToArray();

        return new QueryDecompositionResult
        {
            OriginalQuery = originalQuery,
            SubQueries = subQueries,
            Relationship = QueryRelationshipType.Conjunction
        };
    }

    // 유틸리티 메서드들
    private static string GetStringProperty(JsonElement element, string propertyName, string defaultValue)
    {
        return element.TryGetProperty(propertyName, out var prop) ? prop.GetString() ?? defaultValue : defaultValue;
    }

    private static float GetFloatProperty(JsonElement element, string propertyName, float defaultValue)
    {
        return element.TryGetProperty(propertyName, out var prop) ? prop.GetSingle() : defaultValue;
    }

    private static QueryType ParseQueryType(string type) => type.ToLowerInvariant() switch
    {
        "factual" => QueryType.Factual,
        "procedural" => QueryType.Procedural,
        "conceptual" => QueryType.Conceptual,
        "comparative" => QueryType.Comparative,
        "problemsolving" => QueryType.ProblemSolving,
        _ => QueryType.Factual
    };

    private static QueryRelationshipType ParseRelationshipType(string relationship) => relationship.ToLowerInvariant() switch
    {
        "sequential" => QueryRelationshipType.Sequential,
        "conjunction" => QueryRelationshipType.Conjunction,
        "disjunction" => QueryRelationshipType.Disjunction,
        "hierarchical" => QueryRelationshipType.Hierarchical,
        _ => QueryRelationshipType.Independent
    };

    private static QueryIntent ParseQueryIntent(string intent) => intent.ToLowerInvariant() switch
    {
        "navigational" => QueryIntent.Navigational,
        "transactional" => QueryIntent.Transactional,
        "troubleshooting" => QueryIntent.Troubleshooting,
        "educational" => QueryIntent.Educational,
        "comparative" => QueryIntent.Comparative,
        _ => QueryIntent.Informational
    };

    private static QueryComplexity ParseQueryComplexity(string complexity) => complexity.ToLowerInvariant() switch
    {
        "moderate" => QueryComplexity.Moderate,
        "complex" => QueryComplexity.Complex,
        "verycomplex" => QueryComplexity.VeryComplex,
        _ => QueryComplexity.Simple
    };

    /// <summary>
    /// 테스트용 팩토리 메서드
    /// </summary>
    public static OpenAIQueryTransformationService CreateForTesting(
        HyDEService? hydeService = null,
        QuOTEService? quoteService = null,
        IOpenAIClient? openAIClient = null,
        QueryTransformationOptions? options = null,
        ILogger<OpenAIQueryTransformationService>? logger = null)
    {
        var mockClient = openAIClient ?? new Moq.Mock<IOpenAIClient>().Object;

        return new OpenAIQueryTransformationService(
            hydeService ?? HyDEService.CreateForTesting(mockClient),
            quoteService ?? QuOTEService.CreateForTesting(mockClient),
            mockClient,
            Microsoft.Extensions.Options.Options.Create(options ?? QueryTransformationOptions.CreateForTesting()),
            logger ?? new Microsoft.Extensions.Logging.Abstractions.NullLogger<OpenAIQueryTransformationService>());
    }
}

/// <summary>
/// 쿼리 변환 서비스 옵션
/// </summary>
public class QueryTransformationOptions
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
    public static QueryTransformationOptions CreateForTesting() => new()
    {
        Timeout = TimeSpan.FromSeconds(10),
        MaxRetries = 1,
        MaxConcurrency = 1,
        EnableDebugLogging = true
    };

    /// <summary>
    /// 운영용 설정
    /// </summary>
    public static QueryTransformationOptions CreateForProduction() => new()
    {
        Timeout = TimeSpan.FromSeconds(45),
        MaxRetries = 3,
        MaxConcurrency = 5,
        EnableDebugLogging = false
    };

    /// <summary>
    /// 설정 유효성 검증
    /// </summary>
    public bool IsValid =>
        Timeout > TimeSpan.Zero &&
        MaxRetries >= 0 &&
        MaxConcurrency > 0;
}