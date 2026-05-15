using FluentAssertions;
using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Stack.Application.Interfaces.Repositories;
using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Application.Services;
using FluxIndex.Stack.Domain.Entities;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace FluxIndex.Stack.Api.Tests;

/// <summary>
/// Unit tests for AiProviderSettingsService.TestProviderConnectionAsync.
/// </summary>
public class AiProviderSettingsServiceTests
{
    private static readonly float[] TestEmbedding = [0.1f, 0.2f, 0.3f];
    private static readonly float[] SingleEmbedding = [0.1f];

    private readonly IAiProviderSettingsRepository _repository;
    private readonly IEmbeddingServiceFactory _embeddingFactory;
    private readonly ITextCompletionServiceFactory _textCompletionFactory;
    private readonly AiProviderSettingsService _service;

    public AiProviderSettingsServiceTests()
    {
        _repository = Substitute.For<IAiProviderSettingsRepository>();
        _embeddingFactory = Substitute.For<IEmbeddingServiceFactory>();
        _textCompletionFactory = Substitute.For<ITextCompletionServiceFactory>();
        var logger = Substitute.For<ILogger<AiProviderSettingsService>>();

        _service = new AiProviderSettingsService(
            _repository, _embeddingFactory, _textCompletionFactory, logger);
    }

    [Fact]
    public async Task TestConnection_WithNoSettings_ReturnsFalse()
    {
        // Arrange
        _repository.GetByProviderNameAsync("OpenAI", Arg.Any<CancellationToken>())
            .Returns((AiProviderSettings?)null);

        // Act
        var result = await _service.TestProviderConnectionAsync("OpenAI");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task TestConnection_WithNoApiKey_ReturnsFalse()
    {
        // Arrange
        var settings = AiProviderSettings.Create("OpenAI", "OpenAI");
        _repository.GetByProviderNameAsync("OpenAI", Arg.Any<CancellationToken>())
            .Returns(settings);

        // Act
        var result = await _service.TestProviderConnectionAsync("OpenAI");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task TestConnection_WithEmbeddingModel_CallsEmbeddingFactory()
    {
        // Arrange
        var settings = AiProviderSettings.Create(
            "OpenAI", "OpenAI", apiKey: "sk-test", embeddingModel: "text-embedding-3-small");
        _repository.GetByProviderNameAsync("OpenAI", Arg.Any<CancellationToken>())
            .Returns(settings);

        var embeddingProvider = Substitute.For<IEmbeddingProvider>();
        embeddingProvider.GetEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TestEmbedding);

        _embeddingFactory.CreateProviderAsync(
                "OpenAI", "sk-test", "text-embedding-3-small", null, Arg.Any<CancellationToken>())
            .Returns(embeddingProvider);

        // Act
        var result = await _service.TestProviderConnectionAsync("OpenAI");

        // Assert
        result.Should().BeTrue();
        await embeddingProvider.Received(1).GetEmbeddingAsync("connection test", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TestConnection_WithLlmModel_CallsTextCompletionFactory()
    {
        // Arrange
        var settings = AiProviderSettings.Create(
            "OpenAI", "OpenAI", apiKey: "sk-test", llmModel: "gpt-4o-mini");
        _repository.GetByProviderNameAsync("OpenAI", Arg.Any<CancellationToken>())
            .Returns(settings);

        var completionService = Substitute.For<ITextCompletionService>();
        completionService.CompleteAsync(
                Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns("ok");

        _textCompletionFactory.CreateProviderAsync(
                "OpenAI", "sk-test", "gpt-4o-mini", null, Arg.Any<CancellationToken>())
            .Returns(completionService);

        // Act
        var result = await _service.TestProviderConnectionAsync("OpenAI");

        // Assert
        result.Should().BeTrue();
        await completionService.Received(1).CompleteAsync(
            "Say ok", Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TestConnection_WithBothModels_TestsBoth()
    {
        // Arrange
        var settings = AiProviderSettings.Create(
            "OpenAI", "OpenAI", apiKey: "sk-test",
            embeddingModel: "text-embedding-3-small", llmModel: "gpt-4o-mini");
        _repository.GetByProviderNameAsync("OpenAI", Arg.Any<CancellationToken>())
            .Returns(settings);

        var embeddingProvider = Substitute.For<IEmbeddingProvider>();
        embeddingProvider.GetEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SingleEmbedding);

        var completionService = Substitute.For<ITextCompletionService>();
        completionService.CompleteAsync(
                Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns("ok");

        _embeddingFactory.CreateProviderAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(embeddingProvider);
        _textCompletionFactory.CreateProviderAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(completionService);

        // Act
        var result = await _service.TestProviderConnectionAsync("OpenAI");

        // Assert
        result.Should().BeTrue();
        await embeddingProvider.Received(1).GetEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await completionService.Received(1).CompleteAsync(
            Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TestConnection_WhenEmbeddingFails_ReturnsFalse()
    {
        // Arrange
        var settings = AiProviderSettings.Create(
            "OpenAI", "OpenAI", apiKey: "bad-key", embeddingModel: "text-embedding-3-small");
        _repository.GetByProviderNameAsync("OpenAI", Arg.Any<CancellationToken>())
            .Returns(settings);

        _embeddingFactory.CreateProviderAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Unauthorized"));

        // Act
        var result = await _service.TestProviderConnectionAsync("OpenAI");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task TestConnection_WhenCompletionFails_ReturnsFalse()
    {
        // Arrange
        var settings = AiProviderSettings.Create(
            "OpenAI", "OpenAI", apiKey: "bad-key", llmModel: "gpt-4o-mini");
        _repository.GetByProviderNameAsync("OpenAI", Arg.Any<CancellationToken>())
            .Returns(settings);

        _textCompletionFactory.CreateProviderAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Unauthorized"));

        // Act
        var result = await _service.TestProviderConnectionAsync("OpenAI");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task TestConnection_WithEndpointUrl_PassesToFactory()
    {
        // Arrange
        var settings = AiProviderSettings.Create(
            "Azure", "Azure OpenAI", apiKey: "azure-key",
            embeddingModel: "text-embedding-3-small",
            endpointUrl: "https://my-resource.openai.azure.com/");
        _repository.GetByProviderNameAsync("Azure", Arg.Any<CancellationToken>())
            .Returns(settings);

        var embeddingProvider = Substitute.For<IEmbeddingProvider>();
        embeddingProvider.GetEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SingleEmbedding);

        _embeddingFactory.CreateProviderAsync(
                "Azure", "azure-key", "text-embedding-3-small",
                "https://my-resource.openai.azure.com/", Arg.Any<CancellationToken>())
            .Returns(embeddingProvider);

        // Act
        var result = await _service.TestProviderConnectionAsync("Azure");

        // Assert
        result.Should().BeTrue();
        await _embeddingFactory.Received(1).CreateProviderAsync(
            "Azure", "azure-key", "text-embedding-3-small",
            "https://my-resource.openai.azure.com/", Arg.Any<CancellationToken>());
    }
}
