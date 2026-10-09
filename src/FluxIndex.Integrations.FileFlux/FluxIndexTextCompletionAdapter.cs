using FileFlux;
using FileFlux.Core;
using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Logging;
using IFileFluxDocumentAnalysisService = FileFlux.IDocumentAnalysisService;


namespace FluxIndex.Integrations.FileFlux;

/// <summary>
/// Adapter that bridges FluxIndex's ITextCompletionService to FileFlux's IDocumentAnalysisService interface
/// Enables FileFlux to use FluxIndex's OpenAI text completion implementation
/// </summary>
public partial class FluxIndexTextCompletionAdapter : IFileFluxDocumentAnalysisService
{
    private readonly ITextCompletionService _fluxIndexService;
    private readonly ITokenCounter? _tokenCounter;
    private readonly ILogger<FluxIndexTextCompletionAdapter> _logger;

    public FluxIndexTextCompletionAdapter(
        ITextCompletionService fluxIndexService,
        ILogger<FluxIndexTextCompletionAdapter> logger,
        ITokenCounter? tokenCounter = null)
    {
        _fluxIndexService = fluxIndexService ?? throw new ArgumentNullException(nameof(fluxIndexService));
        _tokenCounter = tokenCounter;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private int CountTokens(string text) =>
        _tokenCounter?.Count(text) ?? Math.Max(1, text.Length / 4);

    /// <summary>
    /// Provider information for FileFlux integration
    /// Updated January 2025 with GPT-5 series (released August 2025)
    /// </summary>
    public DocumentAnalysisServiceInfo ProviderInfo => new()
    {
        Name = "FluxIndex OpenAI Adapter",
        Type = DocumentAnalysisProviderType.OpenAI,
        SupportedModels = new[]
        {
            // GPT-5 Series (Released August 2025) - Advanced reasoning capabilities
            "gpt-5-nano",            // ⭐ Most cost-effective: $0.05/1M input, $0.40/1M output (8x cheaper than gpt-4o-mini)
            "gpt-5-mini",            // Balanced: $0.25/1M input, $2.00/1M output
            "gpt-5",                 // Flagship reasoning: $1.25/1M input, $10.00/1M output

            // GPT-4o Series (Legacy - for backward compatibility)
            "gpt-4o-mini",           // Legacy: $0.15/1M input, $0.60/1M output
            "gpt-4o",                // Legacy flagship: $2.50/1M input, $10.00/1M output
            "gpt-4o-2024-08-06"      // Legacy stable: $2.50/1M input, $10.00/1M output
        },
        // 0 = not declared: the wrapped ITextCompletionService may be any model (a local one included), and FileFlux
        // reads this value to decide whether a prompt fits — a made-up figure would let prompts through that do not.
        MaxContextLength = 0,
        InputTokenCost = 0.00005m, // gpt-5-nano pricing (most cost-effective)
        OutputTokenCost = 0.0004m,
        ApiVersion = "2025-08-07"  // GPT-5 release date
    };

    /// <summary>
    /// Check if the FluxIndex text completion service is available
    /// </summary>
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        // FluxIndex service is available if it's been registered in DI
        return Task.FromResult(_fluxIndexService != null);
    }

    /// <summary>
    /// Generate text completion using FluxIndex's service
    /// </summary>
    public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
        => GenerateAsync(prompt, GenerationSettings.Default, cancellationToken);

    /// <summary>
    /// Generate text completion with the caller's sampling settings; an unset value falls back to 2000 output tokens
    /// and temperature 0.7.
    /// </summary>
    public async Task<string> GenerateAsync(string prompt, GenerationSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        try
        {
            return await _fluxIndexService.CompleteAsync(
                prompt,
                new Flux.Abstractions.TextCompletionOptions
                {
                    MaxTokens = settings.MaxTokens is > 0 ? settings.MaxTokens.Value : 2000,
                    Temperature = settings.Temperature is { } temperature ? (float)temperature : 0.7f,
                    // FileFlux's GenerateAsync contract reports truncation; the port only does when asked.
                    ThrowOnTruncation = true,
                },
                cancellationToken);
        }
        catch (TextCompletionTruncatedException ex) when (ex is not GenerationTruncatedException)
        {
            // The port reports truncation with its own exception; FileFlux's contract names GenerationTruncatedException.
            throw ex.MaxTokens is { } maxTokens
                ? new GenerationTruncatedException(maxTokens, ex)
                : new GenerationTruncatedException(ex.Message, ex);
        }
        catch (Exception ex)
        {
            LogFailedToGenerateTextCompletion(_logger, ex);
            throw;
        }
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to generate text completion")]
    private static partial void LogFailedToGenerateTextCompletion(ILogger logger, Exception exception);

    #endregion
}
