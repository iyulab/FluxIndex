using FluxIndex.AI.OpenAI.Extensions;
using FluxIndex.AI.OpenAI.Services;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace FluxIndex.AI.OpenAI.Tests.Integration;

/// <summary>
/// 실제 OpenAI API를 사용한 통합 테스트
/// </summary>
[Collection("RealAPI")]
public class RealOpenAIIntegrationTests
{
    private readonly ITestOutputHelper _output;
    private readonly IServiceProvider _serviceProvider;
    private readonly bool _hasApiKey;

    public RealOpenAIIntegrationTests(ITestOutputHelper output)
    {
        _output = output;

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());

        // .env.local 파일에서 설정 로드
        var configuration = LoadConfiguration();
        var apiKey = configuration["OPENAI_API_KEY"];
        var model = configuration["OPENAI_MODEL"] ?? "gpt-3.5-turbo";
        var embeddingModel = configuration["OPENAI_EMBEDDING_MODEL"] ?? "text-embedding-3-small";

        _hasApiKey = !string.IsNullOrEmpty(apiKey);

        if (_hasApiKey)
        {
            services.AddOpenAIQueryTransformation(openAI =>
            {
                openAI.ApiKey = apiKey;
                openAI.DefaultModel = model;
                openAI.BaseUrl = "https://api.openai.com";
                openAI.Temperature = 0.7f;
                openAI.MaxTokens = 1500;
            });

            // 실제 OpenAI 클라이언트 등록
            services.AddSingleton<IOpenAIClient>(provider =>
            {
                var logger = provider.GetService<ILogger<OpenAIClient>>();
                return new OpenAIClient(apiKey, "https://api.openai.com", logger);
            });
        }
        else
        {
            _output.WriteLine("Warning: No API key found in .env.local file. Tests will be skipped.");
        }

        _serviceProvider = services.BuildServiceProvider();
    }

    private static IConfiguration LoadConfiguration()
    {
        var builder = new ConfigurationBuilder();

        var envPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", ".env.local");
        if (File.Exists(envPath))
        {
            var envVars = new Dictionary<string, string>();
            foreach (var line in File.ReadAllLines(envPath))
            {
                if (line.Contains('=') && !line.StartsWith('#'))
                {
                    var parts = line.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        envVars[parts[0].Trim()] = parts[1].Trim();
                    }
                }
            }
            builder.AddInMemoryCollection(envVars);
        }

        return builder.Build();
    }

    [Fact]
    public async Task HyDEService_RealAPI_GeneratesQualityDocument()
    {
        // Skip if no API key
        if (!_hasApiKey)
        {
            _output.WriteLine("Skipping test: No API key available");
            return;
        }

        // Arrange
        var service = _serviceProvider.GetRequiredService<HyDEService>();
        var query = "머신러닝에서 오버피팅을 방지하는 방법은 무엇인가?";

        // Act
        var result = await service.GenerateHypotheticalDocumentAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(query, result.OriginalQuery);
        Assert.NotEmpty(result.HypotheticalDocument);
        Assert.True(result.QualityScore > 0);
        Assert.True(result.TokensUsed > 0);
        Assert.True(result.GenerationTimeMs > 0);

        // 품질 검증
        Assert.True(result.HypotheticalDocument.Length > 50, "Generated document should have meaningful content");
        Assert.Contains("오버피팅", result.HypotheticalDocument, StringComparison.OrdinalIgnoreCase);

        _output.WriteLine($"Generated Document: {result.HypotheticalDocument}");
        _output.WriteLine($"Quality Score: {result.QualityScore}");
        _output.WriteLine($"Tokens Used: {result.TokensUsed}");
        _output.WriteLine($"Generation Time: {result.GenerationTimeMs}ms");
    }

    [Fact]
    public async Task QuOTEService_RealAPI_GeneratesExpandedQueries()
    {
        // Skip if no API key
        if (!_hasApiKey)
        {
            _output.WriteLine("Skipping test: No API key available");
            return;
        }

        // Arrange
        var service = _serviceProvider.GetRequiredService<QuOTEService>();
        var query = "딥러닝 신경망의 활성화 함수 종류";
        var options = new QuOTEOptions
        {
            MaxExpansions = 4,
            MaxRelatedQuestions = 3,
            DiversityLevel = 0.8f
        };

        // Act
        var result = await service.GenerateQuestionOrientedEmbeddingAsync(query, options);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(query, result.OriginalQuery);
        Assert.NotEmpty(result.ExpandedQueries);
        Assert.NotEmpty(result.RelatedQuestions);
        Assert.True(result.QualityScore > 0);

        // 확장 쿼리 검증
        Assert.True(result.ExpandedQueries.Count <= options.MaxExpansions);
        Assert.All(result.ExpandedQueries, eq => Assert.NotEmpty(eq));

        // 관련 질문 검증
        Assert.True(result.RelatedQuestions.Count <= options.MaxRelatedQuestions);
        Assert.All(result.RelatedQuestions, q => Assert.EndsWith("?", q));

        _output.WriteLine($"Expanded Queries:");
        foreach (var eq in result.ExpandedQueries)
        {
            _output.WriteLine($"  - {eq}");
        }

        _output.WriteLine($"Related Questions:");
        foreach (var q in result.RelatedQuestions)
        {
            _output.WriteLine($"  - {q}");
        }

        _output.WriteLine($"Quality Score: {result.QualityScore}");
    }

    [Fact]
    public async Task OpenAIQueryTransformationService_RealAPI_CompleteWorkflow()
    {
        // Skip if no API key
        if (!_hasApiKey)
        {
            _output.WriteLine("Skipping test: No API key available");
            return;
        }

        // Arrange
        var service = _serviceProvider.GetRequiredService<IQueryTransformationService>();
        var query = "자연어 처리에서 트랜스포머 아키텍처의 주요 구성요소와 작동 원리";

        // Act & Assert
        _output.WriteLine($"Testing query: {query}");

        // 1. HyDE 테스트
        var hydeResult = await service.GenerateHypotheticalDocumentAsync(query);
        Assert.NotNull(hydeResult);
        Assert.True(hydeResult.IsSuccessful);
        _output.WriteLine($"HyDE Quality: {hydeResult.QualityScore}");

        // 2. QuOTE 테스트
        var quoteResult = await service.GenerateQuestionOrientedEmbeddingAsync(query);
        Assert.NotNull(quoteResult);
        Assert.NotEmpty(quoteResult.ExpandedQueries);
        _output.WriteLine($"QuOTE Expansions: {quoteResult.ExpandedQueries.Count}");

        // 3. 다중 쿼리 생성 테스트
        var multipleQueries = await service.GenerateMultipleQueriesAsync(query, 5);
        Assert.NotEmpty(multipleQueries);
        Assert.Contains(query, multipleQueries);
        _output.WriteLine($"Multiple Queries: {multipleQueries.Count}");

        // 4. 쿼리 분해 테스트
        var decomposed = await service.DecomposeQueryAsync(query);
        Assert.NotNull(decomposed);
        Assert.NotEmpty(decomposed.SubQueries);
        _output.WriteLine($"Decomposed Sub-queries: {decomposed.SubQueries.Count}");

        // 5. 의도 분석 테스트
        var intentResult = await service.AnalyzeQueryIntentAsync(query);
        Assert.NotNull(intentResult);
        Assert.NotEmpty(intentResult.PrimaryIntent);
        Assert.NotEmpty(intentResult.Domain);
        _output.WriteLine($"Intent: {intentResult.PrimaryIntent}, Domain: {intentResult.Domain}");
    }

    [Theory]
    [InlineData("머신러닝 알고리즘 비교", "academic")]
    [InlineData("Python pandas 사용법", "technical")]
    [InlineData("AI 윤리 문제점", "conversational")]
    public async Task HyDEService_RealAPI_DifferentStyles(string query, string style)
    {
        // Skip if no API key
        if (!_hasApiKey)
        {
            _output.WriteLine("Skipping test: No API key available");
            return;
        }

        // Arrange
        var service = _serviceProvider.GetRequiredService<HyDEService>();
        var options = new HyDEOptions
        {
            DocumentStyle = style,
            MaxLength = 250,
            DomainContext = "기술 문서"
        };

        // Act
        var result = await service.GenerateHypotheticalDocumentAsync(query, options);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.True(result.HypotheticalDocument.Length <= options.MaxLength + 50); // 약간의 오차 허용

        _output.WriteLine($"Style: {style}");
        _output.WriteLine($"Query: {query}");
        _output.WriteLine($"Generated: {result.HypotheticalDocument}");
        _output.WriteLine($"Quality: {result.QualityScore}");
        _output.WriteLine("---");
    }

    [Fact]
    public async Task QuOTEService_RealAPI_DomainWeights()
    {
        // Skip if no API key
        if (!_hasApiKey)
        {
            _output.WriteLine("Skipping test: No API key available");
            return;
        }

        // Arrange
        var service = _serviceProvider.GetRequiredService<QuOTEService>();
        var query = "블록체인 보안 암호화";
        var options = new QuOTEOptions
        {
            MaxExpansions = 4,
            DomainWeights = new Dictionary<string, float>
            {
                { "보안", 1.3f },
                { "암호화", 1.2f },
                { "블록체인", 1.1f }
            }
        };

        // Act
        var result = await service.GenerateQuestionOrientedEmbeddingAsync(query, options);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.QueryWeights);

        _output.WriteLine($"Original Query: {query}");
        _output.WriteLine("Query Weights:");
        foreach (var (q, weight) in result.QueryWeights)
        {
            _output.WriteLine($"  {weight:F3}: {q}");
        }
    }

    [Fact]
    public async Task QueryTransformation_RealAPI_PerformanceBenchmark()
    {
        // Skip if no API key
        if (!_hasApiKey)
        {
            _output.WriteLine("Skipping test: No API key available");
            return;
        }

        // Arrange
        var service = _serviceProvider.GetRequiredService<IQueryTransformationService>();
        var queries = new[]
        {
            "데이터베이스 인덱스 최적화",
            "웹 서비스 확장성 개선",
            "클라우드 마이그레이션 전략"
        };

        var results = new List<(string Query, long TimeMs, bool Success)>();

        // Act
        foreach (var query in queries)
        {
            var startTime = DateTime.UtcNow;

            try
            {
                var result = await service.GenerateMultipleQueriesAsync(query, 3);
                var endTime = DateTime.UtcNow;
                var timeMs = (long)(endTime - startTime).TotalMilliseconds;

                results.Add((query, timeMs, result.Count > 0));
                _output.WriteLine($"Query: {query} | Time: {timeMs}ms | Success: {result.Count > 0}");
            }
            catch (Exception ex)
            {
                var endTime = DateTime.UtcNow;
                var timeMs = (long)(endTime - startTime).TotalMilliseconds;
                results.Add((query, timeMs, false));
                _output.WriteLine($"Query: {query} | Time: {timeMs}ms | Error: {ex.Message}");
            }
        }

        // Assert
        var avgTime = results.Count > 0 ? results.Sum(r => r.TimeMs) / results.Count : 0;
        var successRate = results.Count > 0 ? results.Count(r => r.Success) / (double)results.Count : 0;

        _output.WriteLine($"Average Time: {avgTime}ms");
        _output.WriteLine($"Success Rate: {successRate:P2}");

        Assert.True(avgTime < 10000, $"Average response time ({avgTime}ms) should be under 10 seconds for real API");
        Assert.True(successRate >= 0.8, $"Success rate ({successRate:P2}) should be at least 80%");
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}

/// <summary>
/// 실제 OpenAI 클라이언트 구현 (테스트용)
/// </summary>
public class OpenAIClient : IOpenAIClient
{
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly ILogger<OpenAIClient>? _logger;
    private readonly HttpClient _httpClient;

    public OpenAIClient(string apiKey, string baseUrl, ILogger<OpenAIClient>? logger = null)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _baseUrl = baseUrl ?? throw new ArgumentNullException(nameof(baseUrl));
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
    }

    public async Task<string> CompleteAsync(string prompt, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        try
        {
            var requestBody = new
            {
                model = "gpt-3.5-turbo",
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                max_tokens = 1500,
                temperature = 0.7
            };

            var json = System.Text.Json.JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            var response = await _httpClient.PostAsync($"{_baseUrl}/v1/chat/completions", content, cts.Token);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cts.Token);
            var responseObj = System.Text.Json.JsonDocument.Parse(responseJson);

            var message = responseObj.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return message ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "OpenAI API call failed for prompt: {Prompt}", prompt.Substring(0, Math.Min(100, prompt.Length)));
            throw;
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

[CollectionDefinition("RealAPI")]
public class RealAPICollection : ICollectionFixture<object>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}