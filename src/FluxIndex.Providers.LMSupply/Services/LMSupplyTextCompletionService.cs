using Flux.Abstractions;
using FluxIndex.Core.Application.Services.Base;
using LMSupply.Generator;
using LMSupply.Generator.Abstractions;
using LMSupply.Generator.Models;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Providers.LMSupply.Services;

/// <summary>
/// Adapts LMSupply's local generator to FluxIndex's <see cref="ITextCompletionService"/> using the
/// <see cref="TextCompletionServiceBase"/> template. Either wraps an already loaded generator, or loads
/// it on first use (see <see cref="LMSupplyTextCompletionService(LMSupplyTextCompletionOptions, ILogger)"/>).
/// </summary>
public sealed partial class LMSupplyTextCompletionService : TextCompletionServiceBase, IAsyncDisposable, ILazilyLoadedModel
{
    private readonly ITextGenerator? _eager;
    private readonly LazyModelHandle<IGeneratorModel>? _handle;
    private readonly ILogger _logger;

    /// <summary>Wraps an already loaded <paramref name="generator"/>.</summary>
    public LMSupplyTextCompletionService(ITextGenerator generator, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(logger);
        _eager = generator;
        _logger = logger;
    }

    /// <summary>
    /// Loads the generator on the first completion call (or <see cref="EnsureLoadedAsync"/>) under
    /// <paramref name="options"/>' progress reporting and timeout; the container never blocks on it.
    /// </summary>
    public LMSupplyTextCompletionService(LMSupplyTextCompletionOptions options, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ModelId);
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _handle = new LazyModelHandle<IGeneratorModel>(
            (progress, ct) => LocalGenerator.LoadAsync(options.ModelId, options.Generator, progress, ct),
            options.Progress,
            options.LoadTimeout);
    }

    /// <summary>Creates a text completion service by loading a local generator model now.</summary>
    public static async Task<LMSupplyTextCompletionService> CreateAsync(
        string modelId = "default",
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var generator = await LocalGenerator.LoadAsync(modelId, cancellationToken: cancellationToken);
        return new LMSupplyTextCompletionService(generator, logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
    }

    /// <inheritdoc />
    public bool IsLoaded => _eager is not null || _handle!.IsLoaded;

    /// <inheritdoc />
    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_handle is not null)
            await _handle.GetAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ITextGenerator> GetGeneratorAsync(CancellationToken cancellationToken) =>
        _eager ?? await _handle!.GetAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    /// <remarks>
    /// Every <see cref="TextCompletionOptions"/> member LMSupply's generator can express is forwarded; a member left
    /// unset keeps the generator's own default. <see cref="TextCompletionOptions.SystemPrompt"/> switches to the chat
    /// path (system message, then the prompt as the user message). <see cref="TextCompletionOptions.ResponseSchema"/>
    /// becomes <see cref="GenerationOptions.JsonSchema"/> and is enforced by the generator.
    /// <see cref="TextCompletionOptions.ResponseFormat"/> = <c>"json"</c> without a schema has no LMSupply counterpart
    /// (a schema is the only structural constraint it offers, and a generic one would reject arrays), so it is not
    /// forwarded; the JSON request then rests on the prompt.
    /// Both paths read the generator's finish reason (<see cref="ITextGenerator.GenerateCompleteResultAsync"/> for a plain
    /// prompt, the last <see cref="ITextGenerator.GenerateChatStreamAsync"/> chunk with a system prompt): with
    /// <see cref="TextCompletionOptions.ThrowOnTruncation"/> set, an answer that stopped at
    /// <see cref="TextCompletionOptions.MaxTokens"/> throws <see cref="TextCompletionTruncatedException"/> instead of
    /// being returned as if it were whole.
    /// </remarks>
    protected override async Task<string> CompleteCoreAsync(
        string prompt,
        TextCompletionOptions options,
        CancellationToken cancellationToken)
    {
        LogGeneration(_logger, options.MaxTokens, options.Temperature, prompt.Length);

        var genOptions = new GenerationOptions
        {
            MaxTokens = options.MaxTokens,
            Temperature = options.Temperature,
        };
        if (options.TopP is { } topP)
            genOptions.TopP = topP;
        if (options.FrequencyPenalty is { } frequencyPenalty)
            genOptions.FrequencyPenalty = frequencyPenalty;
        if (options.PresencePenalty is { } presencePenalty)
            genOptions.PresencePenalty = presencePenalty;
        if (options.StopSequences is { Count: > 0 } stops)
            genOptions.StopSequences = stops;
        if (!string.IsNullOrWhiteSpace(options.ResponseSchema))
            genOptions.JsonSchema = options.ResponseSchema;

        var generator = await GetGeneratorAsync(cancellationToken).ConfigureAwait(false);
        string text;
        string? finishReason;
        if (string.IsNullOrWhiteSpace(options.SystemPrompt))
        {
            var result = await generator.GenerateCompleteResultAsync(prompt, genOptions, cancellationToken).ConfigureAwait(false);
            text = result.Content;
            finishReason = result.FinishReason;
        }
        else
        {
            ChatMessage[] messages = [ChatMessage.System(options.SystemPrompt), ChatMessage.User(prompt)];
            var sb = new System.Text.StringBuilder();
            finishReason = null;
            await foreach (var chunk in generator.GenerateChatStreamAsync(messages, genOptions, cancellationToken).ConfigureAwait(false))
            {
                sb.Append(chunk.Text);
                finishReason = chunk.FinishReason ?? finishReason;
            }
            text = sb.ToString();
        }

        if (options.ThrowOnTruncation && finishReason == "length")
            throw new TextCompletionTruncatedException(options.MaxTokens);

        return text;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() =>
        _eager is not null ? _eager.DisposeAsync() : _handle!.DisposeAsync();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Generating text (maxTokens={MaxTokens}, temperature={Temperature}, prompt={PromptLength} chars)")]
    private static partial void LogGeneration(ILogger logger, int maxTokens, float temperature, int promptLength);
}
