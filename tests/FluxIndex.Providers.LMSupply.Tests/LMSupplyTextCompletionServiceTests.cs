using Flux.Abstractions;
using AwesomeAssertions;
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

        var act = () => new LMSupplyTextCompletionService((ITextGenerator)null!, logger);

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
        _mockGenerator.GenerateCompleteResultAsync(
                "test prompt",
                Arg.Any<GenerationOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(R("generated text"));

        var result = await _service.CompleteAsync("test prompt", new TextCompletionOptions { MaxTokens = 100, Temperature = 0.5f }, TestContext.Current.CancellationToken);

        result.Should().Be("generated text");
        await _mockGenerator.Received(1).GenerateCompleteResultAsync(
            "test prompt",
            Arg.Is<GenerationOptions>(o => o.MaxTokens == 100 && Math.Abs(o.Temperature - 0.5f) < 0.001f),
            Arg.Any<CancellationToken>());
    }

    // Every TextCompletionOptions member LMSupply's generator can express reaches it. Before, only MaxTokens and
    // Temperature did: stop sequences, sampling, penalties and the response schema were silently dropped.
    [Fact]
    public async Task CompleteAsync_ForwardsEveryOptionTheGeneratorSupports()
    {
        const string schema = "{\"type\":\"object\",\"properties\":{\"answer\":{\"type\":\"string\"}}}";
        _mockGenerator.GenerateCompleteResultAsync(Arg.Any<string>(), Arg.Any<GenerationOptions>(), Arg.Any<CancellationToken>())
            .Returns(R("{}"));

        await _service.CompleteAsync("prompt", new TextCompletionOptions
        {
            MaxTokens = 64,
            Temperature = 0.2f,
            TopP = 0.5f,
            FrequencyPenalty = 0.3f,
            PresencePenalty = 0.4f,
            StopSequences = ["END"],
            ResponseSchema = schema,
        }, TestContext.Current.CancellationToken);

        await _mockGenerator.Received(1).GenerateCompleteResultAsync(
            "prompt",
            Arg.Is<GenerationOptions>(o =>
                o.MaxTokens == 64
                && Math.Abs(o.Temperature - 0.2f) < 0.001f
                && Math.Abs(o.TopP - 0.5f) < 0.001f
                && Math.Abs(o.FrequencyPenalty - 0.3f) < 0.001f
                && Math.Abs(o.PresencePenalty - 0.4f) < 0.001f
                && o.StopSequences != null && o.StopSequences.SequenceEqual(new[] { "END" })
                && o.JsonSchema == schema),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteAsync_OptionsLeftUnset_KeepTheGeneratorsOwnDefaults()
    {
        var defaults = new GenerationOptions();
        _mockGenerator.GenerateCompleteResultAsync(Arg.Any<string>(), Arg.Any<GenerationOptions>(), Arg.Any<CancellationToken>())
            .Returns(R("text"));

        await _service.CompleteAsync("prompt", new TextCompletionOptions(), TestContext.Current.CancellationToken);

        await _mockGenerator.Received(1).GenerateCompleteResultAsync(
            "prompt",
            Arg.Is<GenerationOptions>(o =>
                Math.Abs(o.TopP - defaults.TopP) < 0.001f
                && o.StopSequences == null
                && o.JsonSchema == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteAsync_WithASystemPrompt_SendsItAsTheSystemMessage()
    {
        _mockGenerator.GenerateChatStreamAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<GenerationOptions>(), Arg.Any<CancellationToken>())
            .Returns(Stream("ans", "wer", finishReason: "stop"));

        var result = await _service.CompleteAsync("the question", new TextCompletionOptions { SystemPrompt = "You are terse." }, TestContext.Current.CancellationToken);

        result.Should().Be("answer");
        _mockGenerator.Received(1).GenerateChatStreamAsync(
            Arg.Is<IEnumerable<ChatMessage>>(m => m.SequenceEqual(new[] { ChatMessage.System("You are terse."), ChatMessage.User("the question") })),
            Arg.Any<GenerationOptions>(),
            Arg.Any<CancellationToken>());
        await _mockGenerator.DidNotReceive().GenerateCompleteResultAsync(Arg.Any<string>(), Arg.Any<GenerationOptions>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region ThrowOnTruncation

    [Fact]
    public async Task CompleteAsync_ThrowOnTruncation_AndTheModelStoppedAtMaxTokens_Throws()
    {
        _mockGenerator.GenerateCompleteResultAsync(Arg.Any<string>(), Arg.Any<GenerationOptions>(), Arg.Any<CancellationToken>())
            .Returns(R("half an ans", "length"));

        var act = () => _service.CompleteAsync("q", new TextCompletionOptions { MaxTokens = 16, ThrowOnTruncation = true }, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<TextCompletionTruncatedException>();
    }

    [Fact]
    public async Task CompleteAsync_WithoutThrowOnTruncation_ReturnsACutOffAnswerAsBefore()
    {
        _mockGenerator.GenerateCompleteResultAsync(Arg.Any<string>(), Arg.Any<GenerationOptions>(), Arg.Any<CancellationToken>())
            .Returns(R("half an ans", "length"));

        var result = await _service.CompleteAsync("q", new TextCompletionOptions { MaxTokens = 16 }, TestContext.Current.CancellationToken);

        result.Should().Be("half an ans");
    }

    [Fact]
    public async Task CompleteAsync_ThrowOnTruncation_AndAFinishedAnswer_ReturnsIt()
    {
        _mockGenerator.GenerateCompleteResultAsync(Arg.Any<string>(), Arg.Any<GenerationOptions>(), Arg.Any<CancellationToken>())
            .Returns(R("whole", "stop"));

        var result = await _service.CompleteAsync("q", new TextCompletionOptions { ThrowOnTruncation = true }, TestContext.Current.CancellationToken);

        result.Should().Be("whole");
    }

    [Fact]
    public async Task CompleteAsync_WithASystemPrompt_ThrowOnTruncation_ReadsTheLastChunksReason()
    {
        _mockGenerator.GenerateChatStreamAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<GenerationOptions>(), Arg.Any<CancellationToken>())
            .Returns(Stream("half", " an", finishReason: "length"));

        var act = () => _service.CompleteAsync("q", new TextCompletionOptions { SystemPrompt = "s", ThrowOnTruncation = true }, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<TextCompletionTruncatedException>();
    }

    [Fact]
    public async Task CompleteJsonAsync_ThrowOnTruncation_IsHonouredToo()
    {
        _mockGenerator.GenerateCompleteResultAsync(Arg.Any<string>(), Arg.Any<GenerationOptions>(), Arg.Any<CancellationToken>())
            .Returns(R("{\"a\":", "length"));

        var act = () => _service.CompleteJsonAsync("q", new TextCompletionOptions { ThrowOnTruncation = true }, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<TextCompletionTruncatedException>();
    }

    private static GenerationResult R(string text, string? finishReason = null) => new(text, TokenUsage.Empty, finishReason);

    private static async IAsyncEnumerable<ChatStreamChunk> Stream(string first, string second, string finishReason)
    {
        yield return new ChatStreamChunk { Text = first };
        yield return new ChatStreamChunk { Text = second };
        yield return new ChatStreamChunk { FinishReason = finishReason };
        await Task.CompletedTask;
    }

    [Fact]
    public async Task CompleteJsonAsync_PassesTheCallersResponseSchemaThrough()
    {
        const string schema = "{\"type\":\"array\"}";
        _mockGenerator.GenerateCompleteResultAsync(Arg.Any<string>(), Arg.Any<GenerationOptions>(), Arg.Any<CancellationToken>())
            .Returns(R("[1,2]"));

        var json = await _service.CompleteJsonAsync("list numbers", new TextCompletionOptions { ResponseSchema = schema }, TestContext.Current.CancellationToken);

        json.Should().Be("[1,2]");
        await _mockGenerator.Received(1).GenerateCompleteResultAsync(
            Arg.Any<string>(),
            Arg.Is<GenerationOptions>(o => o.JsonSchema == schema),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteAsync_EmptyPrompt_ReturnsEmpty()
    {
        var result = await _service.CompleteAsync("", cancellationToken: TestContext.Current.CancellationToken);

        result.Should().BeEmpty();
        await _mockGenerator.DidNotReceive().GenerateCompleteResultAsync(
            Arg.Any<string>(),
            Arg.Any<GenerationOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteAsync_WhitespacePrompt_ReturnsEmpty()
    {
        var result = await _service.CompleteAsync("   ", cancellationToken: TestContext.Current.CancellationToken);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CompleteAsync_NullPrompt_ReturnsEmpty()
    {
        var result = await _service.CompleteAsync(null!, cancellationToken: TestContext.Current.CancellationToken);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CompleteAsync_UsesDefaultParameters()
    {
        _mockGenerator.GenerateCompleteResultAsync(
                Arg.Any<string>(),
                Arg.Any<GenerationOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(R("result"));

        await _service.CompleteAsync("prompt", cancellationToken: TestContext.Current.CancellationToken);

        await _mockGenerator.Received(1).GenerateCompleteResultAsync(
            "prompt",
            Arg.Is<GenerationOptions>(o => o.MaxTokens == 500 && Math.Abs(o.Temperature - 0.7f) < 0.001f),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region CompleteJsonAsync

    [Fact]
    public async Task CompleteJsonAsync_ValidPrompt_AppendsJsonInstruction()
    {
        _mockGenerator.GenerateCompleteResultAsync(
                Arg.Any<string>(),
                Arg.Any<GenerationOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(R("{\"key\": \"value\"}"));

        var result = await _service.CompleteJsonAsync("generate JSON", cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Be("{\"key\": \"value\"}");
        await _mockGenerator.Received(1).GenerateCompleteResultAsync(
            Arg.Is<string>(s => s.Contains("generate JSON") && s.Contains("JSON")),
            Arg.Any<GenerationOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteJsonAsync_EmptyPrompt_ReturnsEmptyJson()
    {
        var result = await _service.CompleteJsonAsync("", cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Be("{}");
    }

    [Fact]
    public async Task CompleteJsonAsync_ResponseWithExtraText_ExtractsJson()
    {
        _mockGenerator.GenerateCompleteResultAsync(
                Arg.Any<string>(),
                Arg.Any<GenerationOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(R("Here is the JSON: {\"result\": 42} Hope that helps!"));

        var result = await _service.CompleteJsonAsync("get data", cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Be("{\"result\": 42}");
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
