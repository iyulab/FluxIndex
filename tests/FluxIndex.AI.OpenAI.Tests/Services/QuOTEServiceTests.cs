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
/// QuOTE 서비스 단위 테스트
/// </summary>
public class QuOTEServiceTests
{
    private readonly Mock<IOpenAIClient> _mockOpenAIClient;
    private readonly Mock<ILogger<QuOTEService>> _mockLogger;
    private readonly QuOTEServiceOptions _options;
    private readonly QuOTEService _service;

    public QuOTEServiceTests()
    {
        _mockOpenAIClient = new Mock<IOpenAIClient>();
        _mockLogger = new Mock<ILogger<QuOTEService>>();
        _options = QuOTEServiceOptions.CreateForTesting();

        _service = new QuOTEService(
            _mockOpenAIClient.Object,
            Options.Create(_options),
            _mockLogger.Object);
    }

    [Fact]
    public async Task GenerateQuestionOrientedEmbeddingAsync_ValidQuery_ReturnsSuccessfulResult()
    {
        // Arrange
        var query = "파이썬 머신러닝 성능 최적화";
        var expansionResponse = @"파이썬 ML 알고리즘 속도 향상 방법
머신러닝 모델 최적화 기법
Python 딥러닝 성능 튜닝";

        var questionResponse = @"파이썬에서 머신러닝 모델의 훈련 속도를 높이는 방법은?
어떤 최적화 라이브러리가 파이썬 ML에 효과적인가?
GPU 활용으로 머신러닝 성능을 개선할 수 있는가?";

        _mockOpenAIClient.SetupSequence(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expansionResponse)
            .ReturnsAsync(questionResponse);

        // Act
        var result = await _service.GenerateQuestionOrientedEmbeddingAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(query, result.OriginalQuery);
        Assert.NotEmpty(result.ExpandedQueries);
        Assert.NotEmpty(result.RelatedQuestions);
        Assert.NotEmpty(result.QueryWeights);
        Assert.True(result.QualityScore > 0);

        // 확장된 쿼리 검증
        Assert.True(result.ExpandedQueries.Count > 0);
        Assert.All(result.ExpandedQueries, eq => Assert.NotEmpty(eq));

        // 관련 질문 검증
        Assert.True(result.RelatedQuestions.Count > 0);
        Assert.All(result.RelatedQuestions, q => Assert.True(q.EndsWith('?')));

        // 가중치 검증
        Assert.True(result.QueryWeights.Values.All(w => w > 0 && w < 1));
    }

    [Fact]
    public async Task GenerateQuestionOrientedEmbeddingAsync_EmptyQuery_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.GenerateQuestionOrientedEmbeddingAsync(string.Empty));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.GenerateQuestionOrientedEmbeddingAsync("   "));
    }

    [Fact]
    public async Task GenerateQuestionOrientedEmbeddingAsync_NullQuery_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.GenerateQuestionOrientedEmbeddingAsync(null!));
    }

    [Fact]
    public async Task GenerateQuestionOrientedEmbeddingAsync_OpenAIClientThrows_ReturnsFailedResult()
    {
        // Arrange
        var query = "테스트 쿼리";
        _mockOpenAIClient
            .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("API Error"));

        // Act
        var result = await _service.GenerateQuestionOrientedEmbeddingAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(query, result.OriginalQuery);
        Assert.Empty(result.ExpandedQueries);
        Assert.Empty(result.RelatedQuestions);
        Assert.Empty(result.QueryWeights);
        Assert.Equal(0, result.QualityScore);
    }

    [Fact]
    public async Task GenerateQuestionOrientedEmbeddingAsync_WithCustomOptions_UsesOptions()
    {
        // Arrange
        var query = "블록체인 보안";
        var options = new QuOTEOptions
        {
            MaxExpansions = 5,
            MaxRelatedQuestions = 3,
            DiversityLevel = 0.8f,
            DomainWeights = new Dictionary<string, float>
            {
                { "보안", 1.2f },
                { "암호화", 1.1f }
            }
        };

        var expansionResponse = @"1. 블록체인 암호화 보안 메커니즘
2. 분산원장 보안 취약점 분석
3. 스마트 컨트랙트 보안 감사
4. 블록체인 네트워크 보안 프로토콜
5. 탈중앙화 보안 모델";

        var questionResponse = @"블록체인의 암호화 보안이 어떻게 작동하는가?
스마트 컨트랙트 보안 감사는 왜 필요한가?
블록체인 네트워크 공격을 방어하는 방법은?";

        _mockOpenAIClient.SetupSequence(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expansionResponse)
            .ReturnsAsync(questionResponse);

        // Act
        var result = await _service.GenerateQuestionOrientedEmbeddingAsync(query, options);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.ExpandedQueries.Count <= options.MaxExpansions);
        Assert.True(result.RelatedQuestions.Count <= options.MaxRelatedQuestions);

        // 프롬프트에 옵션이 포함되었는지 확인
        _mockOpenAIClient.Verify(x => x.CompleteAsync(
            It.Is<string>(prompt =>
                prompt.Contains("5가지 다른 방식으로") &&
                prompt.Contains("다양성 수준: 0.8") &&
                prompt.Contains("보안, 암호화")),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()),
            Times.Once);

        _mockOpenAIClient.Verify(x => x.CompleteAsync(
            It.Is<string>(prompt => prompt.Contains("3가지 질문을")),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GenerateQuestionOrientedEmbeddingAsync_QueryWeightCalculation_WorksCorrectly()
    {
        // Arrange
        var query = "인공지능 윤리";
        var options = new QuOTEOptions
        {
            DomainWeights = new Dictionary<string, float>
            {
                { "윤리", 1.5f },
                { "AI", 1.3f }
            }
        };

        var expansionResponse = @"AI 윤리적 고려사항 분석
인공지능 도덕적 딜레마 해결
머신러닝 편향성 윤리 문제";

        var questionResponse = @"AI가 윤리적 결정을 내릴 수 있는가?
인공지능의 편향성을 어떻게 해결할까?";

        _mockOpenAIClient.SetupSequence(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expansionResponse)
            .ReturnsAsync(questionResponse);

        // Act
        var result = await _service.GenerateQuestionOrientedEmbeddingAsync(query, options);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.QueryWeights);

        // 도메인 가중치가 적용되었는지 확인
        var ethicsQuery = result.QueryWeights.Keys.FirstOrDefault(k => k.Contains("윤리"));
        if (ethicsQuery != null)
        {
            var ethicsWeight = result.QueryWeights[ethicsQuery];
            var regularQueries = result.QueryWeights.Where(kv => !kv.Key.Contains("윤리") && !kv.Key.Contains("AI")).ToList();

            if (regularQueries.Any())
            {
                Assert.True(ethicsWeight >= regularQueries.First().Value);
            }
        }

        // 모든 가중치의 합이 정규화되었는지 확인 (대략적으로)
        var totalWeight = result.QueryWeights.Values.Sum();
        Assert.True(totalWeight <= 1.1f); // 약간의 오차 허용
    }

    [Theory]
    [InlineData("1. 첫 번째 쿼리", "첫 번째 쿼리")]
    [InlineData("2) 두 번째 쿼리", "두 번째 쿼리")]
    [InlineData("- 세 번째 쿼리", "세 번째 쿼리")]
    [InlineData("• 네 번째 쿼리", "네 번째 쿼리")]
    [InlineData("\"다섯 번째 쿼리\"", "다섯 번째 쿼리")]
    public async Task GenerateQuestionOrientedEmbeddingAsync_QueryParsing_CleansFormatting(string input, string expected)
    {
        // Arrange
        var query = "테스트 쿼리";
        var expansionResponse = input;
        var questionResponse = "테스트 질문?";

        _mockOpenAIClient.SetupSequence(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expansionResponse)
            .ReturnsAsync(questionResponse);

        // Act
        var result = await _service.GenerateQuestionOrientedEmbeddingAsync(query);

        // Assert
        Assert.Contains(expected, result.ExpandedQueries);
    }

    [Theory]
    [InlineData("테스트 질문", "테스트 질문?")]
    [InlineData("다른 질문?", "다른 질문?")]
    [InlineData("1. 번호가 있는 질문", "번호가 있는 질문?")]
    public async Task GenerateQuestionOrientedEmbeddingAsync_QuestionParsing_AddsQuestionMark(string input, string expected)
    {
        // Arrange
        var query = "테스트 쿼리";
        var expansionResponse = "테스트 확장 쿼리";
        var questionResponse = input;

        _mockOpenAIClient.SetupSequence(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expansionResponse)
            .ReturnsAsync(questionResponse);

        // Act
        var result = await _service.GenerateQuestionOrientedEmbeddingAsync(query);

        // Assert
        Assert.Contains(expected, result.RelatedQuestions);
    }

    [Fact]
    public async Task GenerateQuestionOrientedEmbeddingAsync_QualityEvaluation_WorksCorrectly()
    {
        // Arrange
        var query = "자연어 처리 트랜스포머";
        var highQualityExpansionResponse = @"트랜스포머 아키텍처 자연어 이해
BERT 모델 언어 처리 성능
어텐션 메커니즘 NLP 응용";

        var highQualityQuestionResponse = @"트랜스포머가 자연어 처리에 어떤 혁신을 가져왔는가?
BERT와 GPT의 차이점은 무엇인가?
어텐션 메커니즘이 기존 RNN보다 나은 이유는?";

        _mockOpenAIClient.SetupSequence(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(highQualityExpansionResponse)
            .ReturnsAsync(highQualityQuestionResponse);

        // Act
        var result = await _service.GenerateQuestionOrientedEmbeddingAsync(query);

        // Assert
        Assert.True(result.QualityScore > 0.5f); // 높은 품질 점수 기대
        Assert.True(result.ExpandedQueries.Count > 0);
        Assert.True(result.RelatedQuestions.All(q => q.EndsWith('?')));

        // 키워드 관련성 검증
        var allGeneratedText = string.Join(" ", result.ExpandedQueries.Concat(result.RelatedQuestions)).ToLowerInvariant();
        Assert.True(allGeneratedText.Contains("트랜스포머") || allGeneratedText.Contains("자연어"));
    }

    [Fact]
    public async Task GenerateQuestionOrientedEmbeddingAsync_CancellationToken_PassedCorrectly()
    {
        // Arrange
        var query = "테스트 쿼리";
        var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        _mockOpenAIClient
            .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("테스트 응답");

        // Act
        await _service.GenerateQuestionOrientedEmbeddingAsync(query, cancellationToken: cancellationToken);

        // Assert
        _mockOpenAIClient.Verify(x => x.CompleteAsync(
            It.IsAny<string>(),
            It.IsAny<TimeSpan>(),
            cancellationToken),
            Times.Exactly(2)); // 확장 쿼리용 1회, 관련 질문용 1회
    }

    [Fact]
    public async Task GenerateQuestionOrientedEmbeddingAsync_EmptyResponses_ReturnsEmptyResult()
    {
        // Arrange
        var query = "테스트 쿼리";
        _mockOpenAIClient
            .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("");

        // Act
        var result = await _service.GenerateQuestionOrientedEmbeddingAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.ExpandedQueries);
        Assert.Empty(result.RelatedQuestions);
        Assert.Empty(result.QueryWeights);
        Assert.True(result.QualityScore < 0.5f); // 낮은 품질 점수
    }

    [Fact]
    public void CreateForTesting_ReturnsValidService()
    {
        // Arrange
        var mockClient = new Mock<IOpenAIClient>();
        var mockLogger = new Mock<ILogger<QuOTEService>>();

        // Act
        var service = QuOTEService.CreateForTesting(mockClient.Object, logger: mockLogger.Object);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public async Task GenerateQuestionOrientedEmbeddingAsync_LengthVariationEvaluation_AffectsQuality()
    {
        // Arrange
        var query = "데이터 시각화";
        var diverseResponse = @"데이터 시각화 도구 비교 분석
데이터 차트와 그래프 활용법
빅데이터 시각화 기술 및 플랫폼 선택 가이드";

        var uniformResponse = @"데이터 시각화
차트 그래프
플롯 도구";

        _mockOpenAIClient.SetupSequence(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(diverseResponse)
            .ReturnsAsync("테스트 질문?");

        // Act
        var diverseResult = await _service.GenerateQuestionOrientedEmbeddingAsync(query);

        _mockOpenAIClient.Reset();
        _mockOpenAIClient.SetupSequence(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(uniformResponse)
            .ReturnsAsync("테스트 질문?");

        var uniformResult = await _service.GenerateQuestionOrientedEmbeddingAsync(query);

        // Assert
        // 다양성이 있는 응답이 더 높은 품질 점수를 가져야 함
        Assert.True(diverseResult.QualityScore >= uniformResult.QualityScore);
    }

    [Fact]
    public async Task GenerateQuestionOrientedEmbeddingAsync_MaxLimits_RespectedCorrectly()
    {
        // Arrange
        var query = "클라우드 컴퓨팅";
        var options = new QuOTEOptions
        {
            MaxExpansions = 2,
            MaxRelatedQuestions = 2
        };

        var longExpansionResponse = @"1. 클라우드 서비스 비교
2. AWS vs Azure 분석
3. 멀티클라우드 전략
4. 하이브리드 클라우드 구성
5. 서버리스 컴퓨팅";

        var longQuestionResponse = @"클라우드 마이그레이션 전략은?
어떤 클라우드 제공업체가 좋은가?
클라우드 보안은 어떻게 관리하는가?
클라우드 비용 최적화 방법은?";

        _mockOpenAIClient.SetupSequence(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(longExpansionResponse)
            .ReturnsAsync(longQuestionResponse);

        // Act
        var result = await _service.GenerateQuestionOrientedEmbeddingAsync(query, options);

        // Assert
        Assert.True(result.ExpandedQueries.Count <= options.MaxExpansions);
        Assert.True(result.RelatedQuestions.Count <= options.MaxRelatedQuestions);
    }
}