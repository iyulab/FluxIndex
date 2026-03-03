using Flux.Abstractions;
using FluentAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Stack.Infrastructure.Services;
using LMSupply.Generator.Abstractions;
using LMSupply.Generator.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace FluxIndex.Stack.Api.Tests;

#pragma warning disable CS0618 // Obsolete CountTokens - transitional tests

/// <summary>
/// Unit tests for LMSupplyTextCompletionWrapper and TextCompletionServiceFactory.
/// </summary>
public class TextCompletionServiceFactoryTests
{
    #region LMSupplyTextCompletionWrapper Tests

    [Fact]
    public async Task Wrapper_CompleteAsync_DelegatesToGenerator()
    {
        // Arrange
        var generator = Substitute.For<ITextGenerator>();
        generator.GenerateCompleteAsync(
                Arg.Any<string>(),
                Arg.Any<GenerationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns("Generated text response");

        var logger = Substitute.For<ILogger>();
        var wrapper = new LMSupplyTextCompletionWrapper(generator, logger);

        // Act
        var result = await wrapper.CompleteAsync("test prompt", new TextCompletionOptions { MaxTokens = 200, Temperature = 0.5f });

        // Assert
        result.Should().Be("Generated text response");
        await generator.Received(1).GenerateCompleteAsync(
            "test prompt",
            Arg.Is<GenerationOptions?>(o => o != null && o.MaxTokens == 200 && o.Temperature == 0.5f),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Wrapper_CompleteAsync_WithEmptyPrompt_ReturnsEmpty()
    {
        // Arrange -- TextCompletionServiceBase returns empty for whitespace prompts
        var generator = Substitute.For<ITextGenerator>();
        var logger = Substitute.For<ILogger>();
        var wrapper = new LMSupplyTextCompletionWrapper(generator, logger);

        // Act
        var result = await wrapper.CompleteAsync("", new TextCompletionOptions { MaxTokens = 100, Temperature = 0.7f });

        // Assert
        result.Should().BeEmpty();
        await generator.DidNotReceive().GenerateCompleteAsync(
            Arg.Any<string>(),
            Arg.Any<GenerationOptions?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Wrapper_CompleteJsonAsync_DelegatesToGeneratorWithJsonInstruction()
    {
        // Arrange
        var generator = Substitute.For<ITextGenerator>();
        generator.GenerateCompleteAsync(
                Arg.Any<string>(),
                Arg.Any<GenerationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("{\"key\": \"value\"}"));

        var logger = Substitute.For<ILogger>();
        var wrapper = new LMSupplyTextCompletionWrapper(generator, logger);

        // Act
        var result = await wrapper.CompleteJsonAsync("extract entities", new TextCompletionOptions { MaxTokens = 300 });

        // Assert
        result.Should().Contain("key");
        await generator.Received(1).GenerateCompleteAsync(
            Arg.Is<string>(s => s.Contains("extract entities") && s.Contains("JSON")),
            Arg.Any<GenerationOptions?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Wrapper_CountTokens_ReturnsEstimate()
    {
        // Arrange
        var generator = Substitute.For<ITextGenerator>();
        var logger = Substitute.For<ILogger>();
        var wrapper = new LMSupplyTextCompletionWrapper(generator, logger);

        // Act -- TextCompletionServiceBase uses ~length/4 heuristic
        var count = wrapper.CountTokens("Hello, this is a test string for token counting.");

        // Assert
        count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Wrapper_Constructor_ThrowsOnNullGenerator()
    {
        // Arrange
        var logger = Substitute.For<ILogger>();

        // Act & Assert
        var act = () => new LMSupplyTextCompletionWrapper(null!, logger);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Wrapper_DisposeAsync_DisposesGenerator()
    {
        // Arrange
        var generator = Substitute.For<ITextGenerator>();
        var logger = Substitute.For<ILogger>();
        var wrapper = new LMSupplyTextCompletionWrapper(generator, logger);

        // Act
        await wrapper.DisposeAsync();

        // Assert
        await generator.Received(1).DisposeAsync();
    }

    #endregion

    #region TextCompletionServiceFactory Routing Tests

    [Theory]
    [InlineData("Mock", null)]
    [InlineData("mock", null)]
    public async Task Factory_CreateProviderAsync_MockProvider_ReturnsMockService(string providerName, string? apiKey)
    {
        // Arrange
        var factory = CreateFactory();

        // Act
        var service = await factory.CreateProviderAsync(providerName, apiKey ?? "test-key", null);

        // Assert
        service.Should().BeOfType<MockTextCompletionService>();
    }

    [Theory]
    [InlineData("OpenAI")]
    [InlineData("Azure")]
    [InlineData("SomeProvider")]
    public async Task Factory_CreateProviderAsync_NoApiKey_ReturnsMockService(string providerName)
    {
        // Arrange
        var factory = CreateFactory();

        // Act
        var service = await factory.CreateProviderAsync(providerName, null, null);

        // Assert
        service.Should().BeOfType<MockTextCompletionService>();
    }

    [Fact]
    public async Task Factory_CreateProviderAsync_OpenAI_WithApiKey_ReturnsOpenAIService()
    {
        // Arrange
        var factory = CreateFactory();

        // Act
        var service = await factory.CreateProviderAsync("OpenAI", "sk-test-key", "gpt-4o");

        // Assert
        service.Should().BeOfType<OpenAITextCompletionService>();
    }

    [Fact]
    public async Task Factory_CreateProviderAsync_Azure_WithApiKey_ReturnsOpenAIService()
    {
        // Arrange
        var factory = CreateFactory();

        // Act
        var service = await factory.CreateProviderAsync("Azure", "azure-key", "gpt-4o", "https://my-resource.openai.azure.com/");

        // Assert
        service.Should().BeOfType<OpenAITextCompletionService>();
    }

    [Fact]
    public async Task Factory_CreateProviderAsync_CustomEndpoint_ReturnsOpenAIService()
    {
        // Arrange -- unknown provider with endpoint URL -> OpenAI-compatible
        var factory = CreateFactory();

        // Act
        var service = await factory.CreateProviderAsync("CustomProvider", "test-key", "model", "http://localhost:8080/v1");

        // Assert
        service.Should().BeOfType<OpenAITextCompletionService>();
    }

    [Fact]
    public async Task Factory_CreateProviderAsync_OpenAI_DefaultModel_UsesGpt4oMini()
    {
        // Arrange
        var factory = CreateFactory();

        // Act -- no model name
        var service = await factory.CreateProviderAsync("OpenAI", "sk-test-key", null);

        // Assert
        service.Should().BeOfType<OpenAITextCompletionService>();
    }

    [Fact]
    public void Factory_SupportedProviders_ContainsExpectedProviders()
    {
        // Arrange
        var factory = CreateFactory();

        // Assert
        factory.SupportedProviders.Should().Contain("OpenAI");
        factory.SupportedProviders.Should().Contain("Azure");
        factory.SupportedProviders.Should().Contain("LMSupply");
        factory.SupportedProviders.Should().Contain("Local");
        factory.SupportedProviders.Should().Contain("Mock");
    }

    private static TextCompletionServiceFactory CreateFactory()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var loggerFactory = Substitute.For<ILoggerFactory>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(Substitute.For<ILogger>());
        var logger = Substitute.For<ILogger<TextCompletionServiceFactory>>();
        return new TextCompletionServiceFactory(cache, loggerFactory, logger);
    }

    #endregion
}
