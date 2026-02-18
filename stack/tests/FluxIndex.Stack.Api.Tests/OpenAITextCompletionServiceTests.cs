using FluentAssertions;
using FluxIndex.Stack.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace FluxIndex.Stack.Api.Tests;

/// <summary>
/// Unit tests for OpenAITextCompletionService construction.
/// Note: Actual API call tests require an API key (Category=RequiresApiKey).
/// </summary>
public class OpenAITextCompletionServiceTests
{
    [Fact]
    public void Constructor_WithStandardOpenAI_CreatesSuccessfully()
    {
        var logger = Substitute.For<ILogger>();
        var service = new OpenAITextCompletionService("sk-test-key", "gpt-4o-mini", null, logger);

        // Assert — service created without throwing
        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithAzureEndpoint_CreatesSuccessfully()
    {
        var logger = Substitute.For<ILogger>();
        var service = new OpenAITextCompletionService(
            "azure-key", "gpt-4o", "https://my-resource.openai.azure.com/", logger);

        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithCustomEndpoint_CreatesSuccessfully()
    {
        var logger = Substitute.For<ILogger>();
        var service = new OpenAITextCompletionService(
            "test-key", "custom-model", "http://localhost:11434/v1", logger);

        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_ThrowsOnNullApiKey()
    {
        var logger = Substitute.For<ILogger>();
        var act = () => new OpenAITextCompletionService(null!, "model", null, logger);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ThrowsOnEmptyApiKey()
    {
        var logger = Substitute.For<ILogger>();
        var act = () => new OpenAITextCompletionService("", "model", null, logger);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ThrowsOnNullModelName()
    {
        var logger = Substitute.For<ILogger>();
        var act = () => new OpenAITextCompletionService("test-key", null!, null, logger);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CountTokens_ReturnsApproximation()
    {
        var logger = Substitute.For<ILogger>();
        var service = new OpenAITextCompletionService("test-key", "gpt-4o-mini", null, logger);

        // TextCompletionServiceBase provides approximate token counting
        var count = service.CountTokens("Hello, this is a test.");
        count.Should().BeGreaterThan(0);
    }
}
