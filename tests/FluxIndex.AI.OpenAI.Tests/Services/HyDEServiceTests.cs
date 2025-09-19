using FluxIndex.AI.OpenAI.Services;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FluxIndex.AI.OpenAI.Tests.Services;

/// <summary>
/// HyDE 서비스 단위 테스트
/// </summary>
public class HyDEServiceTests
{
    private readonly Mock<IOpenAIClient> _mockOpenAIClient;
    private readonly Mock<ILogger<HyDEService>> _mockLogger;
    private readonly HyDEServiceOptions _options;
    private readonly HyDEService _service;

    public HyDEServiceTests()
    {
        _mockOpenAIClient = new Mock<IOpenAIClient>();
        _mockLogger = new Mock<ILogger<HyDEService>>();
        _options = HyDEServiceOptions.CreateForTesting();

        _service = new HyDEService(
            _mockOpenAIClient.Object,
            Options.Create(_options),
            _mockLogger.Object);
    }

    [Fact]
    public async Task GenerateHypotheticalDocumentAsync_ValidQuery_ReturnsSuccessfulResult()
    {
        // Arrange
        var query = "머신러닝이란 무엇인가?";
        var mockResponse = "머신러닝은 인공지능의 한 분야로, 컴퓨터가 명시적으로 프로그래밍되지 않고도 데이터로부터 학습할 수 있게 하는 기술입니다. 알고리즘을 사용하여 데이터의 패턴을 찾아내고 예측을 수행합니다.";

        _mockOpenAIClient
            .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        // Act
        var result = await _service.GenerateHypotheticalDocumentAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(query, result.OriginalQuery);
        Assert.NotEmpty(result.HypotheticalDocument);
        Assert.True(result.QualityScore > 0);
        Assert.True(result.IsSuccessful);
        Assert.True(result.TokensUsed > 0);
        Assert.True(result.GenerationTimeMs > 0);
    }

    [Fact]
    public async Task GenerateHypotheticalDocumentAsync_EmptyQuery_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.GenerateHypotheticalDocumentAsync(string.Empty));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.GenerateHypotheticalDocumentAsync("   "));
    }

    [Fact]
    public async Task GenerateHypotheticalDocumentAsync_NullQuery_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.GenerateHypotheticalDocumentAsync(null!));
    }

    [Fact]
    public async Task GenerateHypotheticalDocumentAsync_OpenAIClientThrows_ReturnsFailedResult()
    {
        // Arrange
        var query = "테스트 쿼리";
        _mockOpenAIClient
            .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("API Error"));

        // Act
        var result = await _service.GenerateHypotheticalDocumentAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(query, result.OriginalQuery);
        Assert.Empty(result.HypotheticalDocument);
        Assert.Equal(0, result.QualityScore);
        Assert.False(result.IsSuccessful);
        Assert.Equal(0, result.TokensUsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("답변 문서:")]
    public async Task GenerateHypotheticalDocumentAsync_EmptyResponse_ReturnsLowQualityResult(string response)
    {
        // Arrange
        var query = "테스트 쿼리";
        _mockOpenAIClient
            .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _service.GenerateHypotheticalDocumentAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.QualityScore <= 0.5f); // 낮은 품질 점수
    }

    [Fact]
    public async Task GenerateHypotheticalDocumentAsync_WithCustomOptions_UsesOptions()
    {
        // Arrange
        var query = "딥러닝 최적화 기법";
        var options = new HyDEOptions
        {
            MaxLength = 500,
            DocumentStyle = "technical",
            DomainContext = "기계학습 연구"
        };

        var mockResponse = "딥러닝 최적화는 신경망의 성능과 효율성을 향상시키는 다양한 기법들을 포함합니다. 주요 기법으로는 학습률 스케줄링, 배치 정규화, 드롭아웃, 가중치 초기화 등이 있습니다.";

        _mockOpenAIClient
            .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        // Act
        var result = await _service.GenerateHypotheticalDocumentAsync(query, options);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);

        // 프롬프트에 옵션이 포함되었는지 확인
        _mockOpenAIClient.Verify(x => x.CompleteAsync(
            It.Is<string>(prompt =>
                prompt.Contains("기술적이고 전문적인 형식으로") &&
                prompt.Contains("최대 500자 이내") &&
                prompt.Contains("기계학습 연구")),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GenerateHypotheticalDocumentAsync_QualityEvaluation_WorksCorrectly()
    {
        // Arrange
        var query = "파이썬 머신러닝 라이브러리";
        var highQualityResponse = "파이썬은 머신러닝 분야에서 가장 인기 있는 프로그래밍 언어 중 하나입니다. scikit-learn은 전통적인 머신러닝 알고리즘을 제공하며, TensorFlow와 PyTorch는 딥러닝 프레임워크입니다. NumPy와 Pandas는 데이터 처리를 위한 기본 라이브러리입니다.";

        _mockOpenAIClient
            .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(highQualityResponse);

        // Act
        var result = await _service.GenerateHypotheticalDocumentAsync(query);

        // Assert
        Assert.True(result.QualityScore > 0.5f); // 높은 품질 점수 기대
        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public async Task GenerateHypotheticalDocumentAsync_CancellationToken_PassedCorrectly()
    {
        // Arrange
        var query = "테스트 쿼리";
        var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        _mockOpenAIClient
            .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("테스트 응답");

        // Act
        await _service.GenerateHypotheticalDocumentAsync(query, cancellationToken: cancellationToken);

        // Assert
        _mockOpenAIClient.Verify(x => x.CompleteAsync(
            It.IsAny<string>(),
            It.IsAny<TimeSpan>(),
            cancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task GenerateHypotheticalDocumentAsync_WithDocumentStyleOptions_GeneratesCorrectPrompts()
    {
        // Arrange
        var query = "블록체인 기술";
        var mockResponse = "테스트 응답";

        _mockOpenAIClient
            .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        var testCases = new[]
        {
            ("academic", "학술적이고 정확한 형식으로"),
            ("technical", "기술적이고 전문적인 형식으로"),
            ("conversational", "대화체로 친근하게"),
            ("unknown", "정보 전달에 중점을 두어")
        };

        foreach (var (style, expectedInstruction) in testCases)
        {
            // Act
            var options = new HyDEOptions { DocumentStyle = style };
            await _service.GenerateHypotheticalDocumentAsync(query, options);

            // Assert
            _mockOpenAIClient.Verify(x => x.CompleteAsync(
                It.Is<string>(prompt => prompt.Contains(expectedInstruction)),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()),
                Times.AtLeastOnce);
        }
    }

    [Fact]
    public void CreateForTesting_ReturnsValidService()
    {
        // Arrange
        var mockClient = new Mock<IOpenAIClient>();
        var mockLogger = new Mock<ILogger<HyDEService>>();

        // Act
        var service = HyDEService.CreateForTesting(mockClient.Object, logger: mockLogger.Object);

        // Assert
        Assert.NotNull(service);
    }

    [Theory]
    [InlineData("답변 문서: 실제 내용", "실제 내용")]
    [InlineData("답변: 테스트 내용", "테스트 내용")]
    [InlineData("문서: 문서 내용", "문서 내용")]
    [InlineData("내용: 본문 내용", "본문 내용")]
    [InlineData("직접적인 내용", "직접적인 내용")]
    public async Task GenerateHypotheticalDocumentAsync_ResponseCleaning_WorksCorrectly(string response, string expectedContent)
    {
        // Arrange
        var query = "테스트 쿼리";
        _mockOpenAIClient
            .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _service.GenerateHypotheticalDocumentAsync(query);

        // Assert
        Assert.Equal(expectedContent.Trim(), result.HypotheticalDocument);
    }
}