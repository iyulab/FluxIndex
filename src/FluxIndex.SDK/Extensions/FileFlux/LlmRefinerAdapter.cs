using FileFlux.Core;
using Flux.Abstractions;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using TokenMeter.Abstractions;

namespace FluxIndex.SDK.Extensions.FileFlux;

/// <summary>
/// Adapter that bridges FluxIndex's ITextCompletionService to FileFlux's ILlmRefiner interface.
/// Enables FileFlux to use FluxIndex's text completion implementation for LLM-based content refinement.
/// </summary>
public partial class LlmRefinerAdapter : ILlmRefiner
{
    private readonly ITextCompletionService _completionService;
    private readonly ITokenCounter? _tokenCounter;
    private readonly ILogger<LlmRefinerAdapter> _logger;

    public LlmRefinerAdapter(
        ITextCompletionService completionService,
        ILogger<LlmRefinerAdapter> logger,
        ITokenCounter? tokenCounter = null)
    {
        _completionService = completionService ?? throw new ArgumentNullException(nameof(completionService));
        _tokenCounter = tokenCounter;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private int CountTokens(string text) =>
        _tokenCounter?.Count(text) ?? Math.Max(1, text.Length / 4);

    /// <inheritdoc />
    public string RefinerType => "FluxIndex.LlmRefinerAdapter";

    /// <inheritdoc />
    public bool IsAvailable => _completionService != null;

    /// <inheritdoc />
    public string? ModelName => "FluxIndex TextCompletion";

    /// <inheritdoc />
    public async Task<LlmRefinedContent> RefineAsync(
        RefinedContent refined,
        LlmRefineOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(refined);

        options ??= LlmRefineOptions.Default;

        // Skip if no improvements are enabled
        if (!options.HasAnyImprovementEnabled)
        {
            LogRefinementSkipped(_logger);
            return LlmRefinedContent.FromRefinedContent(refined);
        }

        var stopwatch = Stopwatch.StartNew();
        var improvements = new List<string>();
        var warnings = new List<string>();

        try
        {
            LogStartingRefinement(_logger, refined.CharCount);

            // Build the refinement prompt
            var prompt = BuildRefinementPrompt(refined.Text, options);
            var inputTokens = CountTokens(prompt);

            // Generate refined content
            var maxTokens = options.MaxTokens > 0 ? options.MaxTokens : 4000;
            var refinedText = await _completionService.CompleteAsync(
                prompt, new TextCompletionOptions { MaxTokens = maxTokens, Temperature = (float)options.Temperature }, cancellationToken);

            var outputTokens = CountTokens(refinedText);

            // Track improvements made
            if (options.RemoveNoise)
                improvements.Add("Noise removal applied");
            if (options.RestoreSentences)
                improvements.Add("Sentence restoration applied");
            if (options.RestructureSections)
                improvements.Add("Section restructuring applied");
            if (options.CorrectOcrErrors)
                improvements.Add("OCR error correction applied");
            if (options.MergeDuplicates)
                improvements.Add("Duplicate merging applied");

            stopwatch.Stop();

            // Calculate quality metrics
            var quality = new LlmRefinementQuality
            {
                InputCharCount = refined.CharCount,
                OutputCharCount = refinedText.Length,
                ImprovementScore = CalculateImprovementScore(refined.Text, refinedText),
                ConfidenceScore = 0.85, // Default confidence
                NoiseSegmentsRemoved = options.RemoveNoise ? EstimateNoiseRemoval(refined.Text, refinedText) : 0,
                SentencesRestored = options.RestoreSentences ? EstimateSentencesRestored(refined.Text, refinedText) : 0,
                StructureChanges = options.RestructureSections ? 1 : 0,
                OcrErrorsCorrected = options.CorrectOcrErrors ? EstimateOcrCorrections(refined.Text, refinedText) : 0,
                DuplicatesMerged = options.MergeDuplicates ? EstimateDuplicatesMerged(refined.Text, refinedText) : 0
            };

            var info = new LlmRefinementInfo
            {
                LlmWasUsed = true,
                Model = ModelName,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                Duration = stopwatch.Elapsed,
                Improvements = improvements,
                Warnings = warnings
            };

            LogRefinementCompleted(_logger, refined.CharCount, refinedText.Length, quality.CompressionRatio, stopwatch.ElapsedMilliseconds);

            return new LlmRefinedContent
            {
                RefinedId = refined.Id,
                RawId = refined.RawId,
                Text = refinedText,
                Sections = refined.Sections,
                Structures = refined.Structures,
                Metadata = refined.Metadata,
                Quality = quality,
                Info = info,
                Status = ProcessingStatus.Completed
            };
        }
        catch (Exception ex)
        {
            LogRefinementFailed(_logger, ex);
            warnings.Add($"LLM refinement failed: {ex.Message}");

            return new LlmRefinedContent
            {
                RefinedId = refined.Id,
                RawId = refined.RawId,
                Text = refined.Text,
                Sections = refined.Sections,
                Structures = refined.Structures,
                Metadata = refined.Metadata,
                Quality = new LlmRefinementQuality
                {
                    InputCharCount = refined.CharCount,
                    OutputCharCount = refined.CharCount,
                    ImprovementScore = 0.0,
                    ConfidenceScore = 0.0
                },
                Info = new LlmRefinementInfo
                {
                    LlmWasUsed = false,
                    SkipReason = $"LLM refinement failed: {ex.Message}",
                    Duration = stopwatch.Elapsed,
                    Warnings = warnings
                },
                Status = ProcessingStatus.Partial,
                Errors = [new ProcessingError
                {
                    Code = "LLM_REFINE_FAILED",
                    Message = ex.Message,
                    Stage = "LlmRefine"
                }]
            };
        }
    }

    private static string BuildRefinementPrompt(string content, LlmRefineOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a document refinement expert. Your task is to improve the following document content for RAG (Retrieval-Augmented Generation) optimization.");
        sb.AppendLine();
        sb.AppendLine("## Instructions:");

        if (options.RemoveNoise)
            sb.AppendLine("- Remove noise such as advertisements, legal notices, navigation elements, and irrelevant content");
        if (options.RestoreSentences)
            sb.AppendLine("- Restore broken sentences (often caused by PDF line breaks) by joining them properly");
        if (options.RestructureSections)
            sb.AppendLine("- Restructure document sections for better logical flow and hierarchy");
        if (options.CorrectOcrErrors)
            sb.AppendLine("- Correct obvious OCR errors and typos while preserving technical terms");
        if (options.MergeDuplicates)
            sb.AppendLine("- Merge semantically duplicate content while preserving unique information");
        if (options.PreserveFormatting)
            sb.AppendLine("- Preserve original formatting where possible (headings, lists, code blocks)");

        if (!string.IsNullOrEmpty(options.CustomInstructions))
        {
            sb.AppendLine();
            sb.AppendLine("## Additional Instructions:");
            sb.AppendLine(options.CustomInstructions);
        }

        if (options.DocumentType != DocumentTypeHint.Auto)
        {
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"## Document Type: {options.DocumentType}");
        }

        if (!string.IsNullOrEmpty(options.TargetLanguage))
        {
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"## Target Language: {options.TargetLanguage}");
        }

        sb.AppendLine();
        sb.AppendLine("## Original Content:");
        sb.AppendLine("```");
        sb.AppendLine(content);
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("## Refined Content:");
        sb.AppendLine("Provide only the refined content without any explanation or commentary.");

        return sb.ToString();
    }

    private static double CalculateImprovementScore(string original, string refined)
    {
        if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(refined))
            return 0.0;

        // Simple heuristic: measure character difference ratio
        var lengthDiff = Math.Abs(original.Length - refined.Length);
        var maxLength = Math.Max(original.Length, refined.Length);

        // Score based on reasonable modification (not too little, not too much)
        var changeRatio = (double)lengthDiff / maxLength;

        // Optimal change ratio is around 5-20%
        if (changeRatio < 0.01)
            return 0.1; // Almost no change
        if (changeRatio < 0.05)
            return 0.3 + changeRatio * 4; // Small improvements
        if (changeRatio < 0.20)
            return 0.5 + (changeRatio - 0.05) * 3; // Good improvements
        if (changeRatio < 0.50)
            return 0.9 - (changeRatio - 0.20); // Heavy modifications
        return 0.5; // Too much change might indicate issues
    }

    private static int EstimateNoiseRemoval(string original, string refined)
    {
        // Estimate based on character reduction
        var reduction = original.Length - refined.Length;
        return reduction > 0 ? Math.Max(1, reduction / 500) : 0;
    }

    private static int EstimateSentencesRestored(string original, string refined)
    {
        // Count line breaks that were removed (potential sentence joins)
        var originalLineBreaks = original.Count(c => c == '\n');
        var refinedLineBreaks = refined.Count(c => c == '\n');
        var reduction = originalLineBreaks - refinedLineBreaks;
        return reduction > 0 ? Math.Max(1, reduction / 3) : 0;
    }

    private static int EstimateOcrCorrections(string original, string refined)
    {
        // Simple heuristic: look for common OCR error patterns
        // This is a rough estimate
        return Math.Max(0, (original.Length - refined.Length) / 100);
    }

    private static int EstimateDuplicatesMerged(string original, string refined)
    {
        // Estimate based on significant content reduction
        var reduction = original.Length - refined.Length;
        return reduction > 200 ? Math.Max(1, reduction / 500) : 0;
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "LLM refinement skipped: no improvements enabled")]
    private static partial void LogRefinementSkipped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting LLM refinement for content ({CharCount} chars)")]
    private static partial void LogStartingRefinement(ILogger logger, int charCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "LLM refinement completed: {InputChars} -> {OutputChars} chars ({Ratio:P1}), {Duration}ms")]
    private static partial void LogRefinementCompleted(ILogger logger, int inputChars, int outputChars, double ratio, long duration);

    [LoggerMessage(Level = LogLevel.Error, Message = "LLM refinement failed, falling back to original content")]
    private static partial void LogRefinementFailed(ILogger logger, Exception exception);

    #endregion
}
