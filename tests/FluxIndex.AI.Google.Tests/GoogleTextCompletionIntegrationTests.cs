using DotNetEnv;
using FluentAssertions;
using FluxIndex.AI.Google.Configuration;
using FluxIndex.AI.Google.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FluxIndex.AI.Google.Tests;

/// <summary>
/// Integration tests for Google Gemini Text Completion Service
/// These tests require a valid GOOGLE_API_KEY environment variable
/// </summary>
public class GoogleTextCompletionIntegrationTests
{
    private readonly string? _apiKey;

    public GoogleTextCompletionIntegrationTests()
    {
        // Load .env.local file from project root
        var envPath = Path.Combine(GetProjectRoot(), ".env.local");
        if (File.Exists(envPath))
        {
            Env.Load(envPath);
        }

        _apiKey = Environment.GetEnvironmentVariable("GOOGLE_API_KEY");
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

    private bool HasApiKey => !string.IsNullOrEmpty(_apiKey);

    private GoogleTextCompletionService CreateService(string model = "gemini-2.0-flash")
    {
        var options = Options.Create(new GoogleOptions
        {
            ApiKey = _apiKey!,
            DefaultModel = model,
            Temperature = 0.3f
        });
        var logger = new Mock<ILogger<GoogleTextCompletionService>>();
        return new GoogleTextCompletionService(options, logger.Object);
    }

    [SkippableFact]
    public async Task GenerateCompletionAsync_WithSimplePrompt_ShouldReturnResponse()
    {
        Skip.IfNot(HasApiKey, "GOOGLE_API_KEY not configured");

        // Arrange
        var service = CreateService();
        var prompt = "What is 2 + 2? Answer with just the number.";

        // Act
        var result = await service.GenerateCompletionAsync(prompt, maxTokens: 50, temperature: 0.0f);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("4");
    }

    [SkippableFact]
    public async Task GenerateCompletionAsync_WithCreativePrompt_ShouldReturnResponse()
    {
        Skip.IfNot(HasApiKey, "GOOGLE_API_KEY not configured");

        // Arrange
        var service = CreateService();
        var prompt = "Write a haiku about programming.";

        // Act
        var result = await service.GenerateCompletionAsync(prompt, maxTokens: 100, temperature: 0.7f);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Length.Should().BeGreaterThan(10);
    }

    [SkippableFact]
    public async Task GenerateJsonCompletionAsync_WithJsonRequest_ShouldReturnValidJson()
    {
        Skip.IfNot(HasApiKey, "GOOGLE_API_KEY not configured");

        // Arrange
        var service = CreateService();
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
    public async Task GenerateCompletionAsync_WithKoreanPrompt_ShouldReturnKoreanResponse()
    {
        Skip.IfNot(HasApiKey, "GOOGLE_API_KEY not configured");

        // Arrange
        var service = CreateService();
        var prompt = "한국어로 '안녕하세요'라고 대답해주세요.";

        // Act
        var result = await service.GenerateCompletionAsync(prompt, maxTokens: 50);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("안녕");
    }

    [SkippableFact]
    public async Task GenerateCompletionAsync_WithLongContext_ShouldHandleCorrectly()
    {
        Skip.IfNot(HasApiKey, "GOOGLE_API_KEY not configured");

        // Arrange
        var service = CreateService();
        var longText = string.Join(" ", Enumerable.Repeat("This is a test sentence.", 50));
        var prompt = $"Summarize this text in one sentence: {longText}";

        // Act
        var result = await service.GenerateCompletionAsync(prompt, maxTokens: 100);

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CountTokens_WithText_ShouldReturnEstimate()
    {
        // Arrange - Create service without API key for this test
        var options = Options.Create(new GoogleOptions
        {
            ApiKey = "dummy-key",
            DefaultModel = "gemini-2.0-flash"
        });
        var logger = new Mock<ILogger<GoogleTextCompletionService>>();
        var service = new GoogleTextCompletionService(options, logger.Object);
        var text = "This is a test string for token counting.";

        // Act
        var tokens = service.CountTokens(text);

        // Assert
        tokens.Should().BeGreaterThan(0);
        tokens.Should().BeLessThan(text.Length); // Tokens should be fewer than characters
    }

    [SkippableFact]
    public async Task GenerateCompletionAsync_WithDifferentModels_ShouldWork()
    {
        Skip.IfNot(HasApiKey, "GOOGLE_API_KEY not configured");

        // Test with gemini-2.0-flash-lite (a different model variant)
        var service = CreateService("gemini-2.0-flash-lite");
        var prompt = "Say 'Hello' and nothing else.";

        // Act
        var result = await service.GenerateCompletionAsync(prompt, maxTokens: 20);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.ToLower().Should().Contain("hello");
    }

    [SkippableFact]
    public async Task GenerateCompletionAsync_WithFlashModel_ShouldBeFaster()
    {
        Skip.IfNot(HasApiKey, "GOOGLE_API_KEY not configured");

        // Arrange - Use flash model for speed
        var service = CreateService("gemini-2.0-flash");
        var prompt = "What is the capital of France? Answer in one word.";

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await service.GenerateCompletionAsync(prompt, maxTokens: 20);
        stopwatch.Stop();

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.ToLower().Should().Contain("paris");
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(10000); // Should complete within 10 seconds
    }
}
