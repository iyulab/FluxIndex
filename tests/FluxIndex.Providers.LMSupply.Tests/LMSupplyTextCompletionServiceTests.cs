using Flux.Abstractions;
using FluentAssertions;
using FluxIndex.Providers.LMSupply.Services;
using LMSupply.Generator;
using LMSupply.Generator.Abstractions;
using LMSupply.Generator.Models;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace FluxIndex.Providers.LMSupply.Tests;

#pragma warning disable CS0618 // Obsolete CountTokens - transitional tests

public class LMSupplyTextCompletionServiceTests
{
    private readonly ITextGenerator _mockGenerator;
    private readonly ILogger<LMSupplyTextCompletionService> _mockLogger;
    private readonly LMSupplyTextCompletionService _service;

    public LMSupplyTextCompletionServiceTests()
    {
        _mockGenerator = Substitute.For<ITextGenerator>();
        _mockLogger = Substitute.For<ILogger<LMSupplyTextCompletionService>>();
        _service = new LMSupplyTextCompletionService(_mockGenerator, _mockLogger);
    }

    #region Constructor

    [Fact]
    public void Constructor_NullGenerator_ThrowsArgumentNullException()
    {
        var logger = Substitute.For<ILogger<LMSupplyTextCompletionService>>();

        var act = () => new LMSupplyTextCompletionService(null!, logger);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var generator = Substitute.For<ITextGenerator>();

        var act = () => new LMSupplyTextCompletionService(generator, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region CompleteAsync

    [Fact]
    public async Task CompleteAsync_ValidPrompt_DelegatesToGenerator()
    {
        _mockGenerator.GenerateCompleteAsync(
                "test prompt",
                Arg.Any<GenerationOptions>(),
                Arg.Any<CancellationToken>())
            .Returns("generated text");

        var result = await _service.CompleteAsync("test prompt", new TextCompletionOptions { MaxTokens = 100, Temperature = 0.5f });

        result.Should().Be("generated text");
        await _mockGenerator.Received(1).GenerateCompleteAsync(
            "test prompt",
            Arg.Is<GenerationOptions>(o => o.MaxTokens == 100 && Math.Abs(o.Temperature - 0.5f) < 0.001f),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteAsync_EmptyPrompt_ReturnsEmpty()
    {
        var result = await _service.CompleteAsync("");

        result.Should().BeEmpty();
        await _mockGenerator.DidNotReceive().GenerateCompleteAsync(
            Arg.Any<string>(),
            Arg.Any<GenerationOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteAsync_WhitespacePrompt_ReturnsEmpty()
    {
        var result = await _service.CompleteAsync("   ");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CompleteAsync_NullPrompt_ReturnsEmpty()
    {
        var result = await _service.CompleteAsync(null!);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CompleteAsync_UsesDefaultParameters()
    {
        _mockGenerator.GenerateCompleteAsync(
                Arg.Any<string>(),
                Arg.Any<GenerationOptions>(),
                Arg.Any<CancellationToken>())
            .Returns("result");

        await _service.CompleteAsync("prompt");

        await _mockGenerator.Received(1).GenerateCompleteAsync(
            "prompt",
            Arg.Is<GenerationOptions>(o => o.MaxTokens == 500 && Math.Abs(o.Temperature - 0.7f) < 0.001f),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region CompleteJsonAsync

    [Fact]
    public async Task CompleteJsonAsync_ValidPrompt_AppendsJsonInstruction()
    {
        _mockGenerator.GenerateCompleteAsync(
                Arg.Any<string>(),
                Arg.Any<GenerationOptions>(),
                Arg.Any<CancellationToken>())
            .Returns("{\"key\": \"value\"}");

        var result = await _service.CompleteJsonAsync("generate JSON");

        result.Should().Be("{\"key\": \"value\"}");
        await _mockGenerator.Received(1).GenerateCompleteAsync(
            Arg.Is<string>(s => s.Contains("generate JSON") && s.Contains("JSON")),
            Arg.Any<GenerationOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteJsonAsync_EmptyPrompt_ReturnsEmptyJson()
    {
        var result = await _service.CompleteJsonAsync("");

        result.Should().Be("{}");
    }

    [Fact]
    public async Task CompleteJsonAsync_ResponseWithExtraText_ExtractsJson()
    {
        _mockGenerator.GenerateCompleteAsync(
                Arg.Any<string>(),
                Arg.Any<GenerationOptions>(),
                Arg.Any<CancellationToken>())
            .Returns("Here is the JSON: {\"result\": 42} Hope that helps!");

        var result = await _service.CompleteJsonAsync("get data");

        result.Should().Be("{\"result\": 42}");
    }

    #endregion

    #region CountTokens

    [Fact]
    public void CountTokens_EmptyText_ReturnsZero()
    {
        _service.CountTokens("").Should().Be(0);
    }

    [Fact]
    public void CountTokens_EnglishText_ReturnsApproximation()
    {
        var count = _service.CountTokens("hello world");

        count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CountTokens_CjkText_CountsPerCharacter()
    {
        // Korean: 5 Hangul chars -> 5 CJK + 1 special = 6
        var count = _service.CountTokens("안녕하세요");

        count.Should().Be(6);
    }

    #endregion

    #region DisposeAsync

    [Fact]
    public async Task DisposeAsync_DisposesUnderlyingGenerator()
    {
        var generator = Substitute.For<ITextGenerator>();
        var logger = Substitute.For<ILogger<LMSupplyTextCompletionService>>();
        var service = new LMSupplyTextCompletionService(generator, logger);

        await service.DisposeAsync();

        await generator.Received(1).DisposeAsync();
    }

    #endregion
}
