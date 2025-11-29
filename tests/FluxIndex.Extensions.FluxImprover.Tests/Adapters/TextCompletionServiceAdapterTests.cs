using FluentAssertions;
using FluxIndex.Extensions.FluxImprover.Adapters;
using Moq;
using Xunit;
using FluxIndexCompletion = FluxIndex.Core.Application.Interfaces.ITextCompletionService;
using FluxImproverCompletion = FluxImprover.Services.ITextCompletionService;
using FluxImprover.Services;

namespace FluxIndex.Extensions.FluxImprover.Tests.Adapters;

/// <summary>
/// Tests for TextCompletionServiceAdapter - adapts FluxIndex ITextCompletionService to FluxImprover's interface
/// </summary>
public class TextCompletionServiceAdapterTests
{
    private readonly Mock<FluxIndexCompletion> _mockFluxIndexService;
    private readonly TextCompletionServiceAdapter _adapter;

    public TextCompletionServiceAdapterTests()
    {
        _mockFluxIndexService = new Mock<FluxIndexCompletion>();
        _adapter = new TextCompletionServiceAdapter(_mockFluxIndexService.Object);
    }

    [Fact]
    public async Task CompleteAsync_WithDefaultOptions_CallsFluxIndexService()
    {
        // Arrange
        const string prompt = "Test prompt";
        const string expectedResponse = "Test response";

        _mockFluxIndexService
            .Setup(s => s.GenerateCompletionAsync(
                prompt,
                It.IsAny<int>(),
                It.IsAny<float>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _adapter.CompleteAsync(prompt);

        // Assert
        result.Should().Be(expectedResponse);
        _mockFluxIndexService.Verify(s => s.GenerateCompletionAsync(
            prompt,
            It.IsAny<int>(),
            It.IsAny<float>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CompleteAsync_WithCustomOptions_PassesCorrectParameters()
    {
        // Arrange
        const string prompt = "Test prompt";
        const string expectedResponse = "Test response";
        var options = new CompletionOptions
        {
            MaxTokens = 1000,
            Temperature = 0.5f
        };

        _mockFluxIndexService
            .Setup(s => s.GenerateCompletionAsync(
                prompt,
                1000,
                0.5f,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _adapter.CompleteAsync(prompt, options);

        // Assert
        result.Should().Be(expectedResponse);
        _mockFluxIndexService.Verify(s => s.GenerateCompletionAsync(
            prompt, 1000, 0.5f, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CompleteAsync_WithJsonMode_CallsJsonCompletionMethod()
    {
        // Arrange
        const string prompt = "Test prompt";
        const string expectedJson = "{\"key\": \"value\"}";
        var options = new CompletionOptions
        {
            JsonMode = true,
            MaxTokens = 500
        };

        _mockFluxIndexService
            .Setup(s => s.GenerateJsonCompletionAsync(
                prompt,
                500,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedJson);

        // Act
        var result = await _adapter.CompleteAsync(prompt, options);

        // Assert
        result.Should().Be(expectedJson);
        _mockFluxIndexService.Verify(s => s.GenerateJsonCompletionAsync(
            prompt, 500, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CompleteAsync_WithCancellationToken_PassesTokenToService()
    {
        // Arrange
        const string prompt = "Test prompt";
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        _mockFluxIndexService
            .Setup(s => s.GenerateCompletionAsync(
                prompt,
                It.IsAny<int>(),
                It.IsAny<float>(),
                token))
            .ReturnsAsync("response");

        // Act
        await _adapter.CompleteAsync(prompt, null, token);

        // Assert
        _mockFluxIndexService.Verify(s => s.GenerateCompletionAsync(
            prompt, It.IsAny<int>(), It.IsAny<float>(), token),
            Times.Once);
    }

    [Fact]
    public async Task CompleteStreamingAsync_YieldsTokensFromNonStreamingCall()
    {
        // Arrange
        const string prompt = "Test prompt";
        const string fullResponse = "This is a test response";

        _mockFluxIndexService
            .Setup(s => s.GenerateCompletionAsync(
                prompt,
                It.IsAny<int>(),
                It.IsAny<float>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(fullResponse);

        // Act
        var tokens = new List<string>();
        await foreach (var token in _adapter.CompleteStreamingAsync(prompt))
        {
            tokens.Add(token);
        }

        // Assert - Since FluxIndex doesn't support streaming, we return the full response as a single token
        tokens.Should().HaveCount(1);
        tokens[0].Should().Be(fullResponse);
    }

    [Fact]
    public void Constructor_WithNullService_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new TextCompletionServiceAdapter(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("fluxIndexService");
    }

    [Fact]
    public async Task CompleteAsync_WithNullOptions_UsesDefaults()
    {
        // Arrange
        const string prompt = "Test prompt";
        const int defaultMaxTokens = 500;
        const float defaultTemperature = 0.7f;

        _mockFluxIndexService
            .Setup(s => s.GenerateCompletionAsync(
                prompt,
                defaultMaxTokens,
                defaultTemperature,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("response");

        // Act
        await _adapter.CompleteAsync(prompt, null);

        // Assert
        _mockFluxIndexService.Verify(s => s.GenerateCompletionAsync(
            prompt, defaultMaxTokens, defaultTemperature, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CompleteAsync_WithSystemPrompt_PrependsToPrompt()
    {
        // Arrange
        const string prompt = "User question";
        const string systemPrompt = "You are a helpful assistant.";
        var options = new CompletionOptions { SystemPrompt = systemPrompt };

        string? capturedPrompt = null;
        _mockFluxIndexService
            .Setup(s => s.GenerateCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<float>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, int, float, CancellationToken>((p, _, _, _) => capturedPrompt = p)
            .ReturnsAsync("response");

        // Act
        await _adapter.CompleteAsync(prompt, options);

        // Assert
        capturedPrompt.Should().Contain(systemPrompt);
        capturedPrompt.Should().Contain(prompt);
    }
}
