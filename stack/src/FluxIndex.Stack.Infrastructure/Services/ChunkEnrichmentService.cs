using System.Diagnostics;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Extensions.FluxImprover.Services;
using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Domain.Entities;
using FluxImprover.Options;
using Microsoft.Extensions.Logging;

// Type alias for FluxImprover's enriched chunk interface
using FluxImproverEnrichedChunk = FluxImprover.Models.IEnrichedChunk;

namespace FluxIndex.Stack.Infrastructure.Services;

/// <summary>
/// Implementation of IChunkEnrichmentService using FluxImprover pipeline.
/// Enriches chunks with AI-generated QA pairs, keywords, and summaries.
/// </summary>
public class ChunkEnrichmentService : IChunkEnrichmentService
{
    private readonly FluxImproverPipeline? _pipeline;
    private readonly ITextCompletionService? _textCompletionService;
    private readonly ILogger<ChunkEnrichmentService> _logger;

    public ChunkEnrichmentService(
        ILogger<ChunkEnrichmentService> logger,
        FluxImproverPipeline? pipeline = null,
        ITextCompletionService? textCompletionService = null)
    {
        _logger = logger;
        _pipeline = pipeline;
        _textCompletionService = textCompletionService;
    }

    /// <inheritdoc />
    public bool IsAvailable =>
        _pipeline != null &&
        _textCompletionService != null &&
        (_pipeline.Capabilities.CanEnrich || _pipeline.Capabilities.CanGenerateQA);

    /// <inheritdoc />
    public async Task<ChunkEnrichmentResult> EnrichChunksAsync(
        IReadOnlyList<DocumentChunk> chunks,
        Document document,
        ChunkEnrichmentOptions? options = null,
        Action<int, int>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ChunkEnrichmentOptions();
        var result = new ChunkEnrichmentResult { TotalChunks = chunks.Count };
        var sw = Stopwatch.StartNew();

        if (!IsAvailable)
        {
            _logger.LogWarning(
                "Chunk enrichment skipped: FluxImprover pipeline or LLM service not available. " +
                "Pipeline: {HasPipeline}, LLM: {HasLLM}",
                _pipeline != null, _textCompletionService != null);

            result.Errors.Add("Enrichment service not available - LLM service not configured");
            result.DurationMs = sw.ElapsedMilliseconds;
            return result;
        }

        _logger.LogInformation(
            "Starting chunk enrichment for document {DocumentId} ({Title}): {ChunkCount} chunks",
            document.Id, document.Title, chunks.Count);

        var processedCount = 0;
        var totalQualityScore = 0.0;
        var qualityScoreCount = 0;

        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Convert Stack chunk to FluxIndex Core's IEnrichedChunk
                var enrichedChunk = new StackDocumentChunkAdapter(chunk, document, chunks.Count);

                // Build pipeline options
                // Note: QAGenerationOptions properties depend on FluxImprover package version
                // Use null to accept FluxImprover defaults for QA generation
                var pipelineOptions = new PipelineOptions
                {
                    EnableEnrichment = options.ExtractKeywords || options.GenerateSummary,
                    EnableQAGeneration = options.GenerateQAPairs,
                    EnableEvaluation = options.EvaluateQuality,
                    QAGenerationOptions = null // Use FluxImprover defaults
                };

                // Process through pipeline
                var pipelineResult = await _pipeline!.ProcessChunkAsync(
                    enrichedChunk,
                    pipelineOptions,
                    cancellationToken);

                if (pipelineResult.Success)
                {
                    // Update chunk metadata with enrichment results
                    UpdateChunkMetadata(chunk, pipelineResult, options);

                    result.EnrichedChunks++;
                    result.TotalQAPairs += pipelineResult.GeneratedQAPairs?.Count ?? 0;

                    // Calculate average quality score
                    if (pipelineResult.EvaluatedQAPairs?.Count > 0)
                    {
                        foreach (var qa in pipelineResult.EvaluatedQAPairs)
                        {
                            totalQualityScore += qa.Evaluation.OverallScore;
                            qualityScoreCount++;
                        }
                    }

                    _logger.LogDebug(
                        "Enriched chunk {ChunkIndex}/{Total}: {QACount} QA pairs",
                        chunk.ChunkIndex, chunks.Count, pipelineResult.GeneratedQAPairs?.Count ?? 0);
                }
                else
                {
                    result.FailedChunks++;
                    result.Errors.Add($"Chunk {chunk.ChunkIndex}: {pipelineResult.ErrorMessage}");

                    _logger.LogWarning(
                        "Failed to enrich chunk {ChunkIndex}: {Error}",
                        chunk.ChunkIndex, pipelineResult.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                result.FailedChunks++;
                result.Errors.Add($"Chunk {chunk.ChunkIndex}: {ex.Message}");

                _logger.LogError(ex,
                    "Exception while enriching chunk {ChunkIndex} of document {DocumentId}",
                    chunk.ChunkIndex, document.Id);
            }

            processedCount++;
            progressCallback?.Invoke(processedCount, chunks.Count);
        }

        sw.Stop();
        result.DurationMs = sw.ElapsedMilliseconds;
        result.AverageQualityScore = qualityScoreCount > 0 ? totalQualityScore / qualityScoreCount : null;

        _logger.LogInformation(
            "Chunk enrichment completed for document {DocumentId}: " +
            "{Enriched}/{Total} chunks, {QAPairs} QA pairs, {Duration}ms",
            document.Id, result.EnrichedChunks, result.TotalChunks,
            result.TotalQAPairs, result.DurationMs);

        return result;
    }

    /// <summary>
    /// Updates chunk metadata with enrichment results.
    /// </summary>
    private void UpdateChunkMetadata(
        DocumentChunk chunk,
        PipelineResult pipelineResult,
        ChunkEnrichmentOptions options)
    {
        // Store QA pairs in metadata
        if (pipelineResult.GeneratedQAPairs?.Count > 0)
        {
            var qaPairs = pipelineResult.EvaluatedQAPairs != null && options.EvaluateQuality
                ? pipelineResult.EvaluatedQAPairs
                    .Where(qa => qa.Evaluation.OverallScore >= options.MinQualityScore)
                    .Select(qa => new Dictionary<string, object>
                    {
                        ["question"] = qa.Question,
                        ["answer"] = qa.Answer,
                        ["quality_score"] = qa.Evaluation.OverallScore
                    })
                    .ToList()
                : pipelineResult.GeneratedQAPairs
                    .Select(qa => new Dictionary<string, object>
                    {
                        ["question"] = qa.Question,
                        ["answer"] = qa.Answer
                    })
                    .ToList();

            chunk.Metadata["qa_pairs"] = qaPairs;
        }

        // Store enriched metadata (keywords, summary, etc.)
        if (pipelineResult.EnrichedChunk != null)
        {
            var enriched = pipelineResult.EnrichedChunk;

            if (enriched.Keywords?.Count > 0)
            {
                chunk.Metadata["keywords"] = enriched.Keywords.ToList();
            }

            if (!string.IsNullOrEmpty(enriched.Summary))
            {
                chunk.Metadata["summary"] = enriched.Summary;
            }

            // Note: FluxImprover.Models.IEnrichedChunk doesn't have Extracts property.
            // Additional extracted data can be stored in Metadata if available.
        }

        // Mark chunk as enriched
        chunk.Metadata["enriched"] = true;
        chunk.Metadata["enriched_at"] = DateTime.UtcNow.ToString("O");
    }
}
