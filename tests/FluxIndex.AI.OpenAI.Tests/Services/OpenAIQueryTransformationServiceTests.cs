using FluxIndex.AI.OpenAI.Services;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FluxIndex.AI.OpenAI.Tests.Services;

/// <summary>
/// OpenAI 쿼리 변환 서비스 통합 테스트
/// </summary>
public class OpenAIQueryTransformationServiceTests
{
    private readonly Mock<IOpenAIClient> _mockOpenAIClient;
    private readonly Mock<ILogger<OpenAIQueryTransformationService>> _mockLogger;
    private readonly Mock<HyDEService> _mockHyDEService;
    private readonly Mock<QuOTEService> _mockQuOTEService;
    private readonly QueryTransformationOptions _options;
    private readonly OpenAIQueryTransformationService _service;

    public OpenAIQueryTransformationServiceTests()
    {
        _mockOpenAIClient = new Mock<IOpenAIClient>();
        _mockLogger = new Mock<ILogger<OpenAIQueryTransformationService>>();
        _mockHyDEService = new Mock<HyDEService>(
            _mockOpenAIClient.Object,
            Options.Create(HyDEServiceOptions.CreateForTesting()),
            Mock.Of<ILogger<HyDEService>>());
        _mockQuOTEService = new Mock<QuOTEService>(
            _mockOpenAIClient.Object,
            Options.Create(QuOTEServiceOptions.CreateForTesting()),
            Mock.Of<ILogger<QuOTEService>>());

        _options = QueryTransformationOptions.CreateForTesting();

        _service = new OpenAIQueryTransformationService(
            _mockOpenAIClient.Object,
            _mockHyDEService.Object,
            _mockQuOTEService.Object,
            Options.Create(_options),
            _mockLogger.Object);
    }

    [Fact]
    public async Task GenerateHypotheticalDocumentAsync_CallsHyDEService()
    {
        // Arrange
        var query = "딥러닝 최적화";
        var expectedResult = new HyDEResult
        {
            OriginalQuery = query,
            HypotheticalDocument = "딥러닝 최적화는 신경망 성능 향상을 위한 다양한 기법들을 포함합니다.",
            QualityScore = 0.8f,
            TokensUsed = 150,
            GenerationTimeMs = 500
        };

        _mockHyDEService
            .Setup(x => x.GenerateHypotheticalDocumentAsync(query, It.IsAny<HyDEOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _service.GenerateHypotheticalDocumentAsync(query);

        // Assert
        Assert.Equal(expectedResult, result);
        _mockHyDEService.Verify(x => x.GenerateHypotheticalDocumentAsync(
            query,
            It.IsAny<HyDEOptions>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GenerateQuestionOrientedEmbeddingAsync_CallsQuOTEService()
    {
        // Arrange
        var query = "자연어 처리 트랜스포머";
        var expectedResult = new QuOTEResult
        {
            OriginalQuery = query,
            ExpandedQueries = new[] { "트랜스포머 아키텍처", "BERT 모델", "GPT 언어모델" },
            RelatedQuestions = new[] { "트랜스포머는 어떻게 작동하는가?", "BERT와 GPT의 차이는?" },
            QueryWeights = new Dictionary<string, float>
            {
                { "트랜스포머 아키텍처", 0.35f },
                { "BERT 모델", 0.33f },
                { "GPT 언어모델", 0.32f }
            },
            QualityScore = 0.9f
        };

        _mockQuOTEService
            .Setup(x => x.GenerateQuestionOrientedEmbeddingAsync(query, It.IsAny<QuOTEOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _service.GenerateQuestionOrientedEmbeddingAsync(query);

        // Assert
        Assert.Equal(expectedResult, result);
        _mockQuOTEService.Verify(x => x.GenerateQuestionOrientedEmbeddingAsync(
            query,
            It.IsAny<QuOTEOptions>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GenerateMultipleQueriesAsync_GeneratesVariedQueries()
    {
        // Arrange
        var query = "컴퓨터 비전 객체 탐지";
        var mockResponse = @"컴퓨터 비전에서 객체 인식 알고리즘
YOLO와 R-CNN 객체 탐지 비교
딥러닝 기반 이미지 객체 검출 기법";

        _mockOpenAIClient
            .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        // Act
        var result = await _service.GenerateMultipleQueriesAsync(query, 3);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Count <= 3);
        Assert.All(result, q => Assert.NotEmpty(q));
        Assert.Contains(query, result); // 원본 쿼리도 포함되어야 함
    }

    [Fact]
    public async Task DecomposeQueryAsync_BreaksComplexQuery()
    {
        // Arrange
        var complexQuery = "머신러닝을 사용한 자연어 처리에서 트랜스포머 아키텍처의 성능 최적화와 BERT 모델의 파인튜닝 방법";
        var mockResponse = @"1. 머신러닝 자연어 처리 기초
2. 트랜스포머 아키텍처 구조
3. 트랜스포머 성능 최적화 기법
4. BERT 모델 이해
5. BERT 파인튜닝 방법론";

        _mockOpenAIClient
            .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        // Act
        var result = await _service.DecomposeQueryAsync(complexQuery);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(complexQuery, result.OriginalQuery);
        Assert.NotEmpty(result.SubQueries);
        Assert.True(result.SubQueries.Count > 1);
        Assert.All(result.SubQueries, sq => Assert.NotEmpty(sq));
        Assert.True(result.Confidence > 0);
    }

    [Fact]
    public async Task AnalyzeQueryIntentAsync_IdentifiesIntentCorrectly()
    {
        // Arrange
        var query = "파이썬 판다스 데이터프레임 병합 방법";
        var mockResponse = @"쿼리 의도 분석:
- 주요 의도: HOW_TO (방법 설명)
- 도메인: 데이터 처리, 프로그래밍
- 복잡도: MEDIUM
- 키워드: python, pandas, dataframe, merge
- 사용자 목적: 구체적인 기술적 방법 습득";

        _mockOpenAIClient
            .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        // Act
        var result = await _service.AnalyzeQueryIntentAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(query, result.OriginalQuery);
        Assert.NotEmpty(result.PrimaryIntent);
        Assert.NotEmpty(result.Domain);
        Assert.NotEmpty(result.Keywords);
        Assert.True(result.Confidence > 0);
        Assert.True(result.Confidence <= 1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task GenerateMultipleQueriesAsync_InvalidQuery_ThrowsArgumentException(string invalidQuery)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.GenerateMultipleQueriesAsync(invalidQuery, 3));
    }

    [Fact]
    public async Task GenerateMultipleQueriesAsync_ZeroCount_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.GenerateMultipleQueriesAsync("valid query", 0));
    }

    [Fact]
    public async Task GenerateMultipleQueriesAsync_NegativeCount_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.GenerateMultipleQueriesAsync("valid query", -1));
    }

    [Fact]
    public async Task DecomposeQueryAsync_SimpleQuery_ReturnsMinimalDecomposition()
    {
        // Arrange
        var simpleQuery = "머신러닝";
        var mockResponse = "1. 머신러닝 기초 개념";

        _mockOpenAIClient
            .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        // Act
        var result = await _service.DecomposeQueryAsync(simpleQuery);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.SubQueries.Count >= 1);
        Assert.Contains(result.OriginalQuery, result.SubQueries); // 원본도 포함되어야 함
    }

    [Fact]
    public async Task AnalyzeQueryIntentAsync_OpenAIError_ReturnsLowConfidenceResult()
    {
        // Arrange
        var query = "test query";
        _mockOpenAIClient
            .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("API Error"));

        // Act
        var result = await _service.AnalyzeQueryIntentAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(query, result.OriginalQuery);
        Assert.Equal("UNKNOWN", result.PrimaryIntent);
        Assert.Equal("GENERAL", result.Domain);
        Assert.Equal(0.0f, result.Confidence);
    }

    [Fact]
    public async Task GenerateMultipleQueriesAsync_OpenAIError_ReturnsOriginalQuery()
    {
        // Arrange
        var query = "test query";
        _mockOpenAIClient
            .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("API Error"));

        // Act
        var result = await _service.GenerateMultipleQueriesAsync(query, 3);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(query, result.First());
    }

    [Fact]
    public async Task DecomposeQueryAsync_OpenAIError_ReturnsOriginalQuery()
    {
        // Arrange
        var query = "complex query";
        _mockOpenAIClient
            .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("API Error"));

        // Act
        var result = await _service.DecomposeQueryAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(query, result.OriginalQuery);
        Assert.Single(result.SubQueries);
        Assert.Equal(query, result.SubQueries.First());
        Assert.Equal(0.0f, result.Confidence);
    }

    [Fact]
    public async Task AllMethods_CancellationToken_PassedCorrectly()
    {
        // Arrange
        var query = "test query";
        var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        _mockOpenAIClient
            .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("test response");

        _mockHyDEService
            .Setup(x => x.GenerateHypotheticalDocumentAsync(query, It.IsAny<HyDEOptions>(), cancellationToken))
            .ReturnsAsync(new HyDEResult { OriginalQuery = query });

        _mockQuOTEService
            .Setup(x => x.GenerateQuestionOrientedEmbeddingAsync(query, It.IsAny<QuOTEOptions>(), cancellationToken))
            .ReturnsAsync(new QuOTEResult { OriginalQuery = query });

        // Act & Assert
        await _service.GenerateHypotheticalDocumentAsync(query, cancellationToken: cancellationToken);
        _mockHyDEService.Verify(x => x.GenerateHypotheticalDocumentAsync(query, It.IsAny<HyDEOptions>(), cancellationToken), Times.Once);

        await _service.GenerateQuestionOrientedEmbeddingAsync(query, cancellationToken: cancellationToken);
        _mockQuOTEService.Verify(x => x.GenerateQuestionOrientedEmbeddingAsync(query, It.IsAny<QuOTEOptions>(), cancellationToken), Times.Once);

        await _service.GenerateMultipleQueriesAsync(query, cancellationToken: cancellationToken);
        _mockOpenAIClient.Verify(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), cancellationToken), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ResponseParsing_HandlesVariousFormats()
    {
        // Arrange
        var query = "파싱 테스트";
        var responses = new[]
        {
            "1. 첫 번째 항목\n2. 두 번째 항목\n3. 세 번째 항목",
            "- 항목 A\n- 항목 B\n- 항목 C",
            "• 불릿 1\n• 불릿 2\n• 불릿 3",
            "항목1\n항목2\n항목3"
        };

        foreach (var response in responses)
        {
            _mockOpenAIClient
                .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);

            // Act
            var result = await _service.GenerateMultipleQueriesAsync(query, 5);

            // Assert
            Assert.NotEmpty(result);
            Assert.All(result, item => Assert.False(string.IsNullOrWhiteSpace(item)));

            // 번호나 기호가 제거되었는지 확인
            Assert.All(result, item =>
            {
                Assert.DoesNotContain("1.", item);
                Assert.DoesNotContain("2.", item);
                Assert.DoesNotContain("-", item.Substring(0, Math.Min(1, item.Length)));
                Assert.DoesNotContain("•", item.Substring(0, Math.Min(1, item.Length)));
            });
        }
    }

    [Fact]
    public void Constructor_NullDependencies_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new OpenAIQueryTransformationService(
            null!, _mockHyDEService.Object, _mockQuOTEService.Object, Options.Create(_options), _mockLogger.Object));

        Assert.Throws<ArgumentNullException>(() => new OpenAIQueryTransformationService(
            _mockOpenAIClient.Object, null!, _mockQuOTEService.Object, Options.Create(_options), _mockLogger.Object));

        Assert.Throws<ArgumentNullException>(() => new OpenAIQueryTransformationService(
            _mockOpenAIClient.Object, _mockHyDEService.Object, null!, Options.Create(_options), _mockLogger.Object));

        Assert.Throws<ArgumentNullException>(() => new OpenAIQueryTransformationService(
            _mockOpenAIClient.Object, _mockHyDEService.Object, _mockQuOTEService.Object, null!, _mockLogger.Object));

        Assert.Throws<ArgumentNullException>(() => new OpenAIQueryTransformationService(
            _mockOpenAIClient.Object, _mockHyDEService.Object, _mockQuOTEService.Object, Options.Create(_options), null!));
    }

    [Fact]
    public async Task IntentAnalysis_ExtractsKeywords_Correctly()
    {
        // Arrange
        var query = "React TypeScript 컴포넌트 최적화";
        var mockResponse = @"쿼리 의도 분석:
- 주요 의도: OPTIMIZATION
- 도메인: 웹 개발, 프론트엔드
- 복잡도: HIGH
- 키워드: React, TypeScript, 컴포넌트, 최적화, 성능
- 사용자 목적: 성능 향상 방법 습득";

        _mockOpenAIClient
            .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        // Act
        var result = await _service.AnalyzeQueryIntentAsync(query);

        // Assert
        Assert.Contains("React", result.Keywords);
        Assert.Contains("TypeScript", result.Keywords);
        Assert.Contains("최적화", result.Keywords);
        Assert.Equal("OPTIMIZATION", result.PrimaryIntent);
    }
}