using DotNetEnv;
using FluentAssertions;
using FluxIndex.AI.OpenAI;
using FluxIndex.AI.OpenAI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FluxIndex.AI.OpenAI.Tests.Integration;

/// <summary>
/// Integration tests for GPUStack Text Completion and Embedding Services
/// These tests require valid GPUSTACK_* environment variables
/// </summary>
public class GPUStackIntegrationTests
{
    private readonly string? _endpoint;
    private readonly string? _apiKey;
    private readonly string? _model;

    public GPUStackIntegrationTests()
    {
        // Load .env.local file from project root
        var envPath = Path.Combine(GetProjectRoot(), ".env.local");
        if (File.Exists(envPath))
        {
            Env.Load(envPath);
        }

        _endpoint = Environment.GetEnvironmentVariable("GPUSTACK_ENDPOINT");
        _apiKey = Environment.GetEnvironmentVariable("GPUSTACK_API_KEY");
        _model = Environment.GetEnvironmentVariable("GPUSTACK_MODEL");
    }

    private static string GetProjectRoot()
    {
        var directory = Directory.GetCurrentDirectory();
        while (directory != null && !File.Exists(Path.Combine(directory, "FluxIndex.sln")))
        {
            directory = Directory.GetParent(directory)?.FullName;
        }
        return directory ?? Directory.GetCurrentDirectory();
    }

    private bool HasGPUStackConfig =>
        !string.IsNullOrEmpty(_endpoint) &&
        !string.IsNullOrEmpty(_apiKey) &&
        !string.IsNullOrEmpty(_model);

    private OpenAITextCompletionService CreateTextCompletionService()
    {
        var options = Options.Create(new OpenAIOptions
        {
            Endpoint = _endpoint!,
            ApiKey = _apiKey!,
            ModelName = _model!,
            ProviderType = OpenAIProviderType.GPUStack
        });
        var logger = new Mock<ILogger<OpenAITextCompletionService>>();
        return new OpenAITextCompletionService(options, logger.Object);
    }

    #region Text Completion Tests

    [SkippableFact]
    public async Task GPUStack_GenerateCompletionAsync_WithSimplePrompt_ShouldReturnResponse()
    {
        Skip.IfNot(HasGPUStackConfig, "GPUStack environment variables not configured");

        // Arrange
        var service = CreateTextCompletionService();
        var prompt = "What is 2 + 2? Answer with just the number.";

        // Act
        var result = await service.GenerateCompletionAsync(prompt, maxTokens: 50, temperature: 0.0f);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("4");
    }

    [SkippableFact]
    public async Task GPUStack_GenerateCompletionAsync_WithCreativePrompt_ShouldReturnResponse()
    {
        Skip.IfNot(HasGPUStackConfig, "GPUStack environment variables not configured");

        // Arrange
        var service = CreateTextCompletionService();
        var prompt = "Write a haiku about programming.";

        // Act
        var result = await service.GenerateCompletionAsync(prompt, maxTokens: 100, temperature: 0.7f);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Length.Should().BeGreaterThan(10);
    }

    [SkippableFact]
    public async Task GPUStack_GenerateCompletionAsync_WithKoreanPrompt_ShouldReturnKoreanResponse()
    {
        Skip.IfNot(HasGPUStackConfig, "GPUStack environment variables not configured");

        // Arrange
        var service = CreateTextCompletionService();
        var prompt = "한국어로 '안녕하세요'라고 대답해주세요.";

        // Act
        var result = await service.GenerateCompletionAsync(prompt, maxTokens: 50);

        // Assert
        result.Should().NotBeNullOrEmpty();
        // Korean response should contain Korean characters
        result.Any(c => c >= 0xAC00 && c <= 0xD7A3).Should().BeTrue("Response should contain Korean characters");
    }

    [SkippableFact]
    public async Task GPUStack_GenerateJsonCompletionAsync_ShouldReturnValidJson()
    {
        Skip.IfNot(HasGPUStackConfig, "GPUStack environment variables not configured");

        // Arrange
        var service = CreateTextCompletionService();
        var prompt = "Generate a JSON object with fields 'name' (string) and 'age' (number) for a person named 'John' who is 30 years old.";

        // Act
        var result = await service.GenerateJsonCompletionAsync(prompt, maxTokens: 100);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("John");
        result.Should().Contain("30");

        // Verify it's valid JSON
        var isValidJson = false;
        try
        {
            System.Text.Json.JsonDocument.Parse(result);
            isValidJson = true;
        }
        catch { }

        isValidJson.Should().BeTrue("Response should be valid JSON");
    }

    [SkippableFact]
    public async Task GPUStack_GenerateCompletionAsync_WithLongContext_ShouldHandleCorrectly()
    {
        Skip.IfNot(HasGPUStackConfig, "GPUStack environment variables not configured");

        // Arrange
        var service = CreateTextCompletionService();
        var longText = string.Join(" ", Enumerable.Repeat("This is a test sentence.", 50));
        var prompt = $"Summarize this text in one sentence: {longText}";

        // Act
        var result = await service.GenerateCompletionAsync(prompt, maxTokens: 100);

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    [SkippableFact]
    public async Task GPUStack_GenerateCompletionAsync_MultipleCalls_ShouldBeConsistent()
    {
        Skip.IfNot(HasGPUStackConfig, "GPUStack environment variables not configured");

        // Arrange
        var service = CreateTextCompletionService();
        var prompt = "What is the capital of France? Answer in one word.";

        // Act - Make multiple calls
        var results = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            var result = await service.GenerateCompletionAsync(prompt, maxTokens: 20, temperature: 0.0f);
            results.Add(result.ToLower());
        }

        // Assert - All responses should mention Paris
        results.Should().AllSatisfy(r => r.Should().Contain("paris"));
    }

    [Fact]
    public void GPUStack_CountTokens_ShouldReturnEstimate()
    {
        // Arrange - This test doesn't need actual GPUStack connection
        var options = Options.Create(new OpenAIOptions
        {
            Endpoint = "http://dummy",
            ApiKey = "dummy-key",
            ModelName = "dummy-model",
            ProviderType = OpenAIProviderType.GPUStack
        });
        var logger = new Mock<ILogger<OpenAITextCompletionService>>();
        var service = new OpenAITextCompletionService(options, logger.Object);
        var text = "This is a test string for token counting.";

        // Act
        var tokens = service.CountTokens(text);

        // Assert
        tokens.Should().BeGreaterThan(0);
        tokens.Should().BeLessThan(text.Length); // Tokens should be fewer than characters
    }

    [SkippableFact]
    public async Task GPUStack_GenerateCompletionAsync_WithCancellation_ShouldRespectToken()
    {
        Skip.IfNot(HasGPUStackConfig, "GPUStack environment variables not configured");

        // Arrange
        var service = CreateTextCompletionService();
        var prompt = "Count from 1 to 100 slowly.";
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await service.GenerateCompletionAsync(prompt, maxTokens: 500, cancellationToken: cts.Token));
    }

    #endregion

    #region Configuration Tests

    [Fact]
    public void GPUStack_Options_ShouldBeConfiguredCorrectly()
    {
        // Arrange
        var options = new OpenAIOptions
        {
            Endpoint = "http://localhost:8080",
            ApiKey = "test-key",
            ModelName = "test-model",
            ProviderType = OpenAIProviderType.GPUStack
        };

        // Assert
        options.ProviderType.Should().Be(OpenAIProviderType.GPUStack);
        options.Endpoint.Should().Be("http://localhost:8080");
        options.ApiKey.Should().Be("test-key");
        options.ModelName.Should().Be("test-model");
    }

    [Fact]
    public void GPUStack_ProviderType_ShouldBeDifferentFromOpenAI()
    {
        // Assert
        OpenAIProviderType.GPUStack.Should().NotBe(OpenAIProviderType.OpenAI);
        OpenAIProviderType.GPUStack.Should().NotBe(OpenAIProviderType.AzureOpenAI);
        OpenAIProviderType.GPUStack.Should().NotBe(OpenAIProviderType.OpenAICompatible);
    }

    #endregion
}
