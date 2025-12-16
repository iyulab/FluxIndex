using System.Text;
using FluxIndex.Core.Application.Interfaces;
using LocalAI;
using LocalAI.Generator;
using LocalAI.Generator.Abstractions;
using LocalAI.Generator.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.SDK.AI.Local.Services;

/// <summary>
/// Local ONNX-based implementation of ITextCompletionService using LocalAI.Generator.
/// Provides offline, GPU-accelerated text generation without external API calls.
/// </summary>
public sealed class LocalAITextCompletionService : ITextCompletionService, IAsyncDisposable
{
    private readonly LocalAITextCompletionOptions _options;
    private readonly ILogger<LocalAITextCompletionService> _logger;
    private IGeneratorModel? _model;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _disposed;

    public LocalAITextCompletionService(
        IOptions<LocalAITextCompletionOptions> options,
        ILogger<LocalAITextCompletionService> logger)
    {
        _options = options.Value;
        _logger = logger;

        _logger.LogInformation(
            "LocalAI Text Completion Service configured: Model={Model}, Provider={Provider}",
            _options.ModelId, _options.ExecutionProvider);
    }

    private async ValueTask<IGeneratorModel> GetModelAsync(CancellationToken cancellationToken = default)
    {
        if (_model != null)
            return _model;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_model != null)
                return _model;

            _logger.LogInformation("Loading LocalAI generator model: {Model}", _options.ModelId);

            var generatorOptions = new GeneratorOptions
            {
                MaxContextLength = _options.MaxContextLength,
                Provider = _options.ToExecutionProvider(),
                CacheDirectory = _options.CacheDirectory
            };

            _model = await LocalGenerator.LoadAsync(
                _options.ModelId,
                generatorOptions,
                new Progress<DownloadProgress>(p =>
                    _logger.LogDebug("Model loading: {File} - {Downloaded}/{Total}",
                        p.FileName, p.BytesDownloaded, p.TotalBytes)),
                cancellationToken);

            _logger.LogInformation(
                "LocalAI generator model loaded: {Model}",
                _model.ModelId);

            return _model;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<string> GenerateCompletionAsync(
        string prompt,
        int maxTokens = 500,
        float temperature = 0.7f,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(prompt))
        {
            _logger.LogWarning("Empty or null prompt provided for text completion");
            return string.Empty;
        }

        try
        {
            var model = await GetModelAsync(cancellationToken);
            _logger.LogDebug("Generating completion for prompt of length {Length}", prompt.Length);

            var options = new GenerationOptions
            {
                MaxTokens = maxTokens,
                Temperature = temperature,
                TopP = _options.TopP,
                TopK = _options.TopK,
                RepetitionPenalty = _options.RepetitionPenalty
            };

            var resultBuilder = new StringBuilder();
            await foreach (var token in model.GenerateAsync(prompt, options, cancellationToken))
            {
                resultBuilder.Append(token);
            }

            var result = resultBuilder.ToString();
            _logger.LogDebug("Completion generated: {Length} characters", result.Length);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate text completion");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<string> GenerateJsonCompletionAsync(
        string prompt,
        int maxTokens = 500,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(prompt))
        {
            _logger.LogWarning("Empty or null prompt provided for JSON completion");
            return "{}";
        }

        try
        {
            var model = await GetModelAsync(cancellationToken);

            // Add JSON instruction to prompt
            var jsonPrompt = $"{prompt}\n\nRespond with valid JSON only. No markdown, no explanation, just JSON:";

            var options = new GenerationOptions
            {
                MaxTokens = maxTokens,
                Temperature = 0.1f, // Lower temperature for structured output
                TopP = 0.9f,
                RepetitionPenalty = 1.1f
            };

            var resultBuilder = new StringBuilder();
            await foreach (var token in model.GenerateAsync(jsonPrompt, options, cancellationToken))
            {
                resultBuilder.Append(token);
            }

            // Try to extract JSON from response
            var result = ExtractJson(resultBuilder.ToString());

            _logger.LogDebug("JSON completion generated: {Length} characters", result.Length);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate JSON completion");
            return "{}";
        }
    }

    /// <inheritdoc />
    public int CountTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        // Rough approximation: 1 token ≈ 4 characters for English
        // More accurate for CJK characters
        var tokenCount = 0;
        foreach (var c in text)
        {
            if (IsCjkCharacter(c))
                tokenCount += 1; // CJK characters are typically 1 token each
            else
                tokenCount += 1; // Will be divided by 4 at the end
        }

        // Adjust for English (roughly 4 chars per token)
        var cjkCount = text.Count(IsCjkCharacter);
        var nonCjkCount = text.Length - cjkCount;

        return cjkCount + (nonCjkCount / 4) + 1; // +1 for rounding
    }

    /// <summary>
    /// Pre-loads the model to avoid cold start latency on first inference.
    /// </summary>
    public async Task WarmupAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Warming up LocalAI generator model...");
        var model = await GetModelAsync(cancellationToken);

        // Generate a small test completion
        var testOptions = new GenerationOptions { MaxTokens = 1 };
        await foreach (var _ in model.GenerateAsync("test", testOptions, cancellationToken))
        {
            break; // Just load the model and generate first token
        }

        _logger.LogInformation("LocalAI generator warmup completed");
    }

    private static string ExtractJson(string response)
    {
        // Try to find JSON object or array
        var start = response.IndexOf('{');
        var arrayStart = response.IndexOf('[');

        if (start < 0 && arrayStart < 0)
            return "{}";

        if (start < 0 || (arrayStart >= 0 && arrayStart < start))
            start = arrayStart;

        var isArray = response[start] == '[';
        var end = isArray
            ? response.LastIndexOf(']')
            : response.LastIndexOf('}');

        if (end <= start)
            return isArray ? "[]" : "{}";

        return response[start..(end + 1)];
    }

    private static bool IsCjkCharacter(char c)
    {
        return (c >= '\u4E00' && c <= '\u9FFF') ||   // CJK Unified Ideographs
               (c >= '\u3400' && c <= '\u4DBF') ||   // CJK Extension A
               (c >= '\uAC00' && c <= '\uD7AF') ||   // Hangul Syllables
               (c >= '\u3040' && c <= '\u309F') ||   // Hiragana
               (c >= '\u30A0' && c <= '\u30FF');     // Katakana
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        if (_model != null)
        {
            await _model.DisposeAsync();
            _model = null;
        }

        _initLock.Dispose();
        _disposed = true;
    }
}
