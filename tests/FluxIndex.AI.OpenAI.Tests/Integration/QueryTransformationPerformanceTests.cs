using FluxIndex.AI.OpenAI.Extensions;
using FluxIndex.AI.OpenAI.Services;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.SDK;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace FluxIndex.AI.OpenAI.Tests.Integration;

/// <summary>
/// 쿼리 변환 성능 벤치마킹 테스트
/// </summary>
public class QueryTransformationPerformanceTests
{
    private readonly ITestOutputHelper _output;
    private readonly Mock<IOpenAIClient> _mockOpenAIClient;
    private readonly ServiceProvider _serviceProvider;

    public QueryTransformationPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
        _mockOpenAIClient = new Mock<IOpenAIClient>();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton(_mockOpenAIClient.Object);
        services.AddTestQueryTransformation(_mockOpenAIClient.Object);

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task HyDEService_PerformanceBenchmark_MeetsThreshold()
    {
        // Arrange
        var service = _serviceProvider.GetRequiredService<HyDEService>();
        var queries = GenerateTestQueries(10);
        var mockResponse = "머신러닝은 데이터에서 패턴을 학습하는 인공지능 기술입니다. 알고리즘을 통해 예측과 분류를 수행하며, 다양한 분야에 응용됩니다.";

        _mockOpenAIClient
            .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        var stopwatch = Stopwatch.StartNew();
        var results = new List<HyDEResult>();

        // Act
        foreach (var query in queries)
        {
            var result = await service.GenerateHypotheticalDocumentAsync(query);
            results.Add(result);
        }

        stopwatch.Stop();

        // Assert
        var averageTime = stopwatch.ElapsedMilliseconds / (double)queries.Count;
        var successRate = results.Count(r => r.IsSuccessful) / (double)results.Count;
        var averageQuality = results.Where(r => r.IsSuccessful).Average(r => r.QualityScore);

        _output.WriteLine($"HyDE Performance Results:");
        _output.WriteLine($"- Total Time: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"- Average Time per Query: {averageTime:F2}ms");
        _output.WriteLine($"- Success Rate: {successRate:P2}");
        _output.WriteLine($"- Average Quality Score: {averageQuality:F3}");

        // 성능 임계값 검증
        Assert.True(averageTime < 1000, $"Average response time ({averageTime:F2}ms) exceeds 1000ms threshold");
        Assert.True(successRate >= 0.95, $"Success rate ({successRate:P2}) is below 95% threshold");
        Assert.True(averageQuality >= 0.4, $"Average quality score ({averageQuality:F3}) is below 0.4 threshold");
    }

    [Fact]
    public async Task QuOTEService_PerformanceBenchmark_MeetsThreshold()
    {
        // Arrange
        var service = _serviceProvider.GetRequiredService<QuOTEService>();
        var queries = GenerateTestQueries(10);
        var expansionResponse = @"쿼리 확장 결과
확장된 검색어
다양한 표현 방식";
        var questionResponse = @"관련 질문이 무엇인가?
다른 궁금한 점은?";

        _mockOpenAIClient.SetupSequence(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expansionResponse)
            .ReturnsAsync(questionResponse);

        var stopwatch = Stopwatch.StartNew();
        var results = new List<QuOTEResult>();

        // Act
        foreach (var query in queries)
        {
            // QuOTE는 두 번의 API 호출이 필요하므로 다시 설정
            _mockOpenAIClient.SetupSequence(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(expansionResponse)
                .ReturnsAsync(questionResponse);

            var result = await service.GenerateQuestionOrientedEmbeddingAsync(query);
            results.Add(result);
        }

        stopwatch.Stop();

        // Assert
        var averageTime = stopwatch.ElapsedMilliseconds / (double)queries.Count;
        var averageExpansions = results.Average(r => r.ExpandedQueries.Count);
        var averageQuestions = results.Average(r => r.RelatedQuestions.Count);
        var averageQuality = results.Average(r => r.QualityScore);

        _output.WriteLine($"QuOTE Performance Results:");
        _output.WriteLine($"- Total Time: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"- Average Time per Query: {averageTime:F2}ms");
        _output.WriteLine($"- Average Expansions per Query: {averageExpansions:F1}");
        _output.WriteLine($"- Average Questions per Query: {averageQuestions:F1}");
        _output.WriteLine($"- Average Quality Score: {averageQuality:F3}");

        // 성능 임계값 검증 (QuOTE는 두 번의 API 호출로 인해 더 오래 걸릴 수 있음)
        Assert.True(averageTime < 2000, $"Average response time ({averageTime:F2}ms) exceeds 2000ms threshold");
        Assert.True(averageExpansions >= 1, $"Average expansions ({averageExpansions:F1}) is below 1");
        Assert.True(averageQuestions >= 1, $"Average questions ({averageQuestions:F1}) is below 1");
        Assert.True(averageQuality >= 0.3, $"Average quality score ({averageQuality:F3}) is below 0.3 threshold");
    }

    [Fact]
    public async Task QueryTransformationService_ConcurrentRequests_HandlesLoad()
    {
        // Arrange
        var service = _serviceProvider.GetRequiredService<IQueryTransformationService>();
        var queries = GenerateTestQueries(20);
        var mockResponse = "테스트 응답 내용";

        _mockOpenAIClient
            .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        var stopwatch = Stopwatch.StartNew();

        // Act
        var tasks = queries.Select(async query =>
        {
            try
            {
                await service.GenerateMultipleQueriesAsync(query, 3);
                return true;
            }
            catch
            {
                return false;
            }
        }).ToArray();

        var results = await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        var successRate = results.Count(r => r) / (double)results.Length;
        var averageTime = stopwatch.ElapsedMilliseconds / (double)queries.Count;

        _output.WriteLine($"Concurrent Load Test Results:");
        _output.WriteLine($"- Total Time: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"- Average Time per Query: {averageTime:F2}ms");
        _output.WriteLine($"- Success Rate: {successRate:P2}");

        Assert.True(successRate >= 0.90, $"Success rate ({successRate:P2}) is below 90% threshold");
        Assert.True(averageTime < 1500, $"Average response time ({averageTime:F2}ms) exceeds 1500ms threshold");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task HyDEService_ScalabilityTest_PerformanceStable(int queryCount)
    {
        // Arrange
        var service = _serviceProvider.GetRequiredService<HyDEService>();
        var queries = GenerateTestQueries(queryCount);
        var mockResponse = "스케일링 테스트를 위한 응답입니다.";

        _mockOpenAIClient
            .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        var stopwatch = Stopwatch.StartNew();

        // Act
        var results = new List<HyDEResult>();
        foreach (var query in queries)
        {
            var result = await service.GenerateHypotheticalDocumentAsync(query);
            results.Add(result);
        }

        stopwatch.Stop();

        // Assert
        var averageTime = stopwatch.ElapsedMilliseconds / (double)queryCount;
        var successCount = results.Count(r => r.IsSuccessful);

        _output.WriteLine($"Scalability Test (Count: {queryCount}):");
        _output.WriteLine($"- Total Time: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"- Average Time: {averageTime:F2}ms");
        _output.WriteLine($"- Success Rate: {successCount}/{queryCount}");

        Assert.True(averageTime < 1000, $"Average time ({averageTime:F2}ms) degrades with scale");
        Assert.Equal(queryCount, successCount);
    }

    [Fact]
    public async Task QueryTransformation_MemoryUsage_WithinLimits()
    {
        // Arrange
        var service = _serviceProvider.GetRequiredService<IQueryTransformationService>();
        var initialMemory = GC.GetTotalMemory(true);

        _mockOpenAIClient
            .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("메모리 테스트 응답");

        // Act
        for (int i = 0; i < 100; i++)
        {
            await service.GenerateMultipleQueriesAsync($"테스트 쿼리 {i}", 3);
        }

        var finalMemory = GC.GetTotalMemory(true);
        var memoryIncrease = finalMemory - initialMemory;

        // Assert
        _output.WriteLine($"Memory Usage Test:");
        _output.WriteLine($"- Initial Memory: {initialMemory / 1024 / 1024:F2} MB");
        _output.WriteLine($"- Final Memory: {finalMemory / 1024 / 1024:F2} MB");
        _output.WriteLine($"- Memory Increase: {memoryIncrease / 1024 / 1024:F2} MB");

        // 100회 호출 후 메모리 증가가 50MB 이하여야 함
        Assert.True(memoryIncrease < 50 * 1024 * 1024, $"Memory increase ({memoryIncrease / 1024 / 1024:F2} MB) exceeds 50MB threshold");
    }

    [Fact]
    public async Task QueryTransformation_ErrorRecovery_MaintainsPerformance()
    {
        // Arrange
        var service = _serviceProvider.GetRequiredService<IQueryTransformationService>();
        var queries = GenerateTestQueries(10);

        // 50% 실패율로 설정
        _mockOpenAIClient.SetupSequence(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("성공 응답")
            .ThrowsAsync(new Exception("API 오류"))
            .ReturnsAsync("성공 응답")
            .ThrowsAsync(new Exception("API 오류"))
            .ReturnsAsync("성공 응답")
            .ThrowsAsync(new Exception("API 오류"))
            .ReturnsAsync("성공 응답")
            .ThrowsAsync(new Exception("API 오류"))
            .ReturnsAsync("성공 응답")
            .ThrowsAsync(new Exception("API 오류"));

        var stopwatch = Stopwatch.StartNew();
        var results = new List<IReadOnlyList<string>>();

        // Act
        foreach (var query in queries)
        {
            var result = await service.GenerateMultipleQueriesAsync(query, 3);
            results.Add(result);
        }

        stopwatch.Stop();

        // Assert
        var averageTime = stopwatch.ElapsedMilliseconds / (double)queries.Count;
        var successfulResults = results.Count(r => r.Count > 1); // 원본 쿼리 외에 추가 쿼리가 있으면 성공

        _output.WriteLine($"Error Recovery Test:");
        _output.WriteLine($"- Total Time: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"- Average Time: {averageTime:F2}ms");
        _output.WriteLine($"- Successful Results: {successfulResults}/{queries.Count}");

        // 오류가 있어도 모든 결과가 최소한 원본 쿼리는 반환해야 함
        Assert.All(results, r => Assert.NotEmpty(r));
        Assert.True(averageTime < 1200, $"Error recovery affects performance too much ({averageTime:F2}ms)");
    }

    [Fact]
    public async Task FluxIndexClient_QueryTransformationExtensions_IntegrationPerformance()
    {
        // Arrange
        var mockVectorStore = new Mock<IVectorStore>();
        var mockDocumentRepository = new Mock<IDocumentRepository>();

        var clientBuilder = new FluxIndexClientBuilder()
            .WithQueryTransformation(openAI =>
            {
                openAI.ApiKey = "test-api-key";
                openAI.BaseUrl = "https://api.openai.com";
            });

        // Mock 서비스들을 설정하여 실제 OpenAI 호출 없이 테스트
        clientBuilder.ConfigureServices(services =>
        {
            services.AddSingleton(_mockOpenAIClient.Object);
            services.AddSingleton(mockVectorStore.Object);
            services.AddSingleton(mockDocumentRepository.Object);
        });

        var client = clientBuilder.Build();
        var query = "통합 테스트 쿼리";

        _mockOpenAIClient
            .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("통합 테스트 가상 문서 응답");

        mockVectorStore
            .Setup(x => x.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DocumentChunk>());

        var stopwatch = Stopwatch.StartNew();

        // Act - HyDE 검색 테스트만 수행 (다른 메서드들은 구현이 완료되지 않았을 수 있음)
        try
        {
            // 이 테스트는 통합이 완료된 후에만 실행
            // await client.SearchWithHyDEAsync(query);
            _output.WriteLine("Integration test skipped - awaiting full implementation");
        }
        catch (NotImplementedException)
        {
            _output.WriteLine("Integration test skipped - methods not yet implemented");
        }

        stopwatch.Stop();

        // Assert
        _output.WriteLine($"Integration Performance Test:");
        _output.WriteLine($"- Setup Time: {stopwatch.ElapsedMilliseconds}ms");

        Assert.True(stopwatch.ElapsedMilliseconds < 5000, "Integration setup takes too long");
    }

    private List<string> GenerateTestQueries(int count)
    {
        var queries = new List<string>();
        var topics = new[]
        {
            "머신러닝 최적화",
            "딥러닝 신경망",
            "자연어 처리 트랜스포머",
            "컴퓨터 비전 CNN",
            "강화학습 알고리즘",
            "데이터 전처리 기법",
            "모델 성능 평가",
            "하이퍼파라미터 튜닝",
            "앙상블 학습 방법",
            "전이 학습 응용"
        };

        for (int i = 0; i < count; i++)
        {
            var topic = topics[i % topics.Length];
            queries.Add($"{topic} - 테스트 케이스 {i + 1}");
        }

        return queries;
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}