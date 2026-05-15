using Flux.Abstractions;
using FluxIndex.Core.Application.Services.Base;
using LMSupply.Generator;
using LMSupply.Generator.Abstractions;
using LMSupply.Generator.Models;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Providers.LMSupply.Services;

/// <summary>
/// Adapts LMSupply's <see cref="ITextGenerator"/> to FluxIndex's
/// <see cref="Flux.Abstractions.ITextCompletionService"/>
/// using the <see cref="TextCompletionServiceBase"/> template.
/// </summary>
/// <remarks>
/// Uses ONNX runtime for local text generation — no API key required.
/// Suitable for HyDE query expansion, metadata enrichment, and other lightweight generation tasks.
/// </remarks>
public sealed partial class LMSupplyTextCompletionService : TextCompletionServiceBase, IAsyncDisposable
{
    private readonly ITextGenerator _generator;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance wrapping the given <paramref name="generator"/>.
    /// </summary>
    /// <param name="generator">LMSupply text generator.</param>
    /// <param name="logger">Logger instance.</param>
    public LMSupplyTextCompletionService(ITextGenerator generator, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(logger);
        _generator = generator;
        _logger = logger;
    }

    /// <summary>
    /// Creates a text completion service by loading a local ONNX generator model.
    /// </summary>
    /// <param name="modelId">Model ID or "default".</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A ready-to-use text completion service.</returns>
    public static async Task<LMSupplyTextCompletionService> CreateAsync(
        string modelId = "default",
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var generator = await LocalGenerator.LoadAsync(modelId, cancellationToken: cancellationToken);
        return new LMSupplyTextCompletionService(generator, logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
    }

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

        return await _generator.GenerateCompleteAsync(prompt, genOptions, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _generator.DisposeAsync();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Generating text (maxTokens={MaxTokens}, temperature={Temperature}, prompt={PromptLength} chars)")]
    private static partial void LogGeneration(ILogger logger, int maxTokens, float temperature, int promptLength);
}
