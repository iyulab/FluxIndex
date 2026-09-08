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

        var generator = await GetGeneratorAsync(cancellationToken).ConfigureAwait(false);
        return await generator.GenerateCompleteAsync(prompt, genOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() =>
        _eager is not null ? _eager.DisposeAsync() : _handle!.DisposeAsync();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Generating text (maxTokens={MaxTokens}, temperature={Temperature}, prompt={PromptLength} chars)")]
    private static partial void LogGeneration(ILogger logger, int maxTokens, float temperature, int promptLength);
}
