using FluxImprover.ChunkFiltering;
using FluxImprover.Models;
using FluxImprover.Options;
using FluxIndex.SDK.Extensions.FluxImprover.Adapters;
using FluxIndexChunk = FluxIndex.Core.Application.Interfaces.IEnrichedChunk;

namespace FluxIndex.SDK.Extensions.FluxImprover.Services;

/// <summary>
/// Wraps FluxImprover's IChunkFilteringService to work with FluxIndex IEnrichedChunk.
/// Provides LLM-based 3-stage chunk filtering for RAG retrieval quality improvement.
/// </summary>
public sealed class ChunkFilteringServiceWrapper
{
    private readonly IChunkFilteringService _filteringService;

    /// <summary>
    /// Creates a new wrapper around FluxImprover's ChunkFilteringService.
    /// </summary>
    /// <param name="filteringService">The FluxImprover filtering service to wrap.</param>
    /// <exception cref="ArgumentNullException">Thrown when filteringService is null.</exception>
    public ChunkFilteringServiceWrapper(IChunkFilteringService filteringService)
    {
        _filteringService = filteringService ?? throw new ArgumentNullException(nameof(filteringService));
    }

    /// <summary>
    /// Filters FluxIndex chunks based on relevance and quality using LLM assessment.
    /// Uses 3-stage evaluation: Initial → Self-Reflection → Critic Validation.
    /// </summary>
    /// <param name="chunks">FluxIndex chunks to filter.</param>
    /// <param name="query">Query or context for relevance assessment. Can be null for quality-only filtering.</param>
    /// <param name="options">Filtering options. Uses defaults if null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Filtered chunks with scores and detailed assessment.</returns>
    public async Task<IReadOnlyList<FilteredFluxIndexChunk>> FilterAsync(
        IEnumerable<FluxIndexChunk> chunks,
        string? query,
        ChunkFilteringOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        // Convert FluxIndex chunks to FluxImprover format
        var fluxImproverChunks = chunks.Select(c => CreateChunkFromFluxIndex(c)).ToList();

        // Filter using FluxImprover service
        var filteredResults = await _filteringService.FilterAsync(
            fluxImproverChunks, query, options, cancellationToken);

        // Map back with FluxIndex chunk references
        return MapFilteredResults(filteredResults, chunks.ToList());
    }

    /// <summary>
    /// Performs 3-stage assessment on a single FluxIndex chunk.
    /// </summary>
    /// <param name="chunk">FluxIndex chunk to assess.</param>
    /// <param name="query">Query context for relevance evaluation.</param>
    /// <param name="options">Assessment options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Detailed assessment result with scores and reasoning.</returns>
    public async Task<ChunkAssessmentResult> AssessAsync(
        FluxIndexChunk chunk,
        string? query,
        ChunkFilteringOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        var fluxImproverChunk = CreateChunkFromFluxIndex(chunk);
        var assessment = await _filteringService.AssessAsync(
            fluxImproverChunk, query, options, cancellationToken);

        return new ChunkAssessmentResult
        {
            ChunkId = chunk.ChunkId,
            SourceId = chunk.Source.SourceId,
            InitialScore = assessment.InitialScore,
            ReflectionScore = assessment.ReflectionScore,
            CriticScore = assessment.CriticScore,
            FinalScore = assessment.FinalScore,
            Confidence = assessment.Confidence,
            Factors = assessment.Factors.Select(f => new AssessmentFactorResult
            {
                Name = f.Name,
                Contribution = f.Contribution,
                Explanation = f.Explanation
            }).ToList(),
            Suggestions = assessment.Suggestions.ToList(),
            Reasoning = new Dictionary<string, string>(assessment.Reasoning)
        };
    }

    /// <summary>
    /// Filters chunks and returns only those that pass the minimum score threshold.
    /// </summary>
    /// <param name="chunks">FluxIndex chunks to filter.</param>
    /// <param name="query">Query context for relevance assessment.</param>
    /// <param name="minScore">Minimum score threshold (0.0 to 1.0). Default is 0.7.</param>
    /// <param name="maxResults">Maximum number of chunks to return. Null for no limit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Filtered chunks sorted by score descending.</returns>
    public async Task<IReadOnlyList<FluxIndexChunk>> FilterByScoreAsync(
        IEnumerable<FluxIndexChunk> chunks,
        string? query,
        double minScore = 0.7,
        int? maxResults = null,
        CancellationToken cancellationToken = default)
    {
        var options = new ChunkFilteringOptions
        {
            MinRelevanceScore = minScore,
            MaxChunks = maxResults
        };

        var filteredResults = await FilterAsync(chunks, query, options, cancellationToken);

        return filteredResults
            .Where(r => r.Passed)
            .OrderByDescending(r => r.CombinedScore)
            .Select(r => r.OriginalChunk)
            .ToList();
    }

    /// <summary>
    /// Creates a FluxImprover Chunk from a FluxIndex chunk.
    /// </summary>
    private static Chunk CreateChunkFromFluxIndex(FluxIndexChunk chunk)
    {
        var adapter = new EnrichedChunkAdapter(chunk);
        return new Chunk
        {
            Id = adapter.Id,
            Content = adapter.Text,
            Metadata = adapter.Metadata?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
        };
    }

    /// <summary>
    /// Maps FluxImprover filtered results back to FluxIndex chunks.
    /// </summary>
    private static IReadOnlyList<FilteredFluxIndexChunk> MapFilteredResults(
        IReadOnlyList<FilteredChunk> filteredResults,
        IReadOnlyList<FluxIndexChunk> originalChunks)
    {
        var chunkMap = originalChunks.ToDictionary(c => c.ChunkId, c => c);

        return filteredResults.Select(fr =>
        {
            var originalChunk = chunkMap.GetValueOrDefault(fr.Chunk.Id);
            return new FilteredFluxIndexChunk
            {
                OriginalChunk = originalChunk!,
                ChunkId = fr.Chunk.Id,
                RelevanceScore = fr.RelevanceScore,
                QualityScore = fr.QualityScore,
                CombinedScore = fr.CombinedScore,
                Passed = fr.Passed,
                Reason = fr.Reason,
                Assessment = fr.Assessment != null ? new ChunkAssessmentResult
                {
                    ChunkId = fr.Chunk.Id,
                    SourceId = originalChunk?.Source.SourceId ?? string.Empty,
                    InitialScore = fr.Assessment.InitialScore,
                    ReflectionScore = fr.Assessment.ReflectionScore,
                    CriticScore = fr.Assessment.CriticScore,
                    FinalScore = fr.Assessment.FinalScore,
                    Confidence = fr.Assessment.Confidence,
                    Factors = fr.Assessment.Factors.Select(f => new AssessmentFactorResult
                    {
                        Name = f.Name,
                        Contribution = f.Contribution,
                        Explanation = f.Explanation
                    }).ToList(),
                    Suggestions = fr.Assessment.Suggestions.ToList(),
                    Reasoning = new Dictionary<string, string>(fr.Assessment.Reasoning)
                } : null
            };
        }).ToList();
    }
}

/// <summary>
/// Filtered FluxIndex chunk with scores and assessment details.
/// </summary>
public sealed class FilteredFluxIndexChunk
{
    /// <summary>
    /// The original FluxIndex chunk.
    /// </summary>
    public required FluxIndexChunk OriginalChunk { get; init; }

    /// <summary>
    /// Chunk ID.
    /// </summary>
    public required string ChunkId { get; init; }

    /// <summary>
    /// Relevance score (0.0 to 1.0).
    /// </summary>
    public double RelevanceScore { get; init; }

    /// <summary>
    /// Quality score (0.0 to 1.0).
    /// </summary>
    public double QualityScore { get; init; }

    /// <summary>
    /// Combined score considering relevance and quality weights.
    /// </summary>
    public double CombinedScore { get; init; }

    /// <summary>
    /// Whether the chunk passed the filtering criteria.
    /// </summary>
    public bool Passed { get; init; }

    /// <summary>
    /// Human-readable reason for the filtering decision.
    /// </summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// Detailed assessment information.
    /// </summary>
    public ChunkAssessmentResult? Assessment { get; init; }
}

/// <summary>
/// Detailed assessment result of a FluxIndex chunk from 3-stage evaluation.
/// </summary>
public sealed class ChunkAssessmentResult
{
    /// <summary>
    /// Chunk ID.
    /// </summary>
    public required string ChunkId { get; init; }

    /// <summary>
    /// Source document ID.
    /// </summary>
    public required string SourceId { get; init; }

    /// <summary>
    /// Initial assessment score from Stage 1.
    /// </summary>
    public double InitialScore { get; init; }

    /// <summary>
    /// Score after self-reflection (Stage 2). Null if reflection was skipped.
    /// </summary>
    public double? ReflectionScore { get; init; }

    /// <summary>
    /// Score after critic validation (Stage 3). Null if validation was skipped.
    /// </summary>
    public double? CriticScore { get; init; }

    /// <summary>
    /// Final consolidated score across all stages.
    /// </summary>
    public double FinalScore { get; init; }

    /// <summary>
    /// Confidence in the assessment (0.0 to 1.0).
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Key factors contributing to the assessment.
    /// </summary>
    public IReadOnlyList<AssessmentFactorResult> Factors { get; init; } = [];

    /// <summary>
    /// Suggestions for improving chunk quality.
    /// </summary>
    public IReadOnlyList<string> Suggestions { get; init; } = [];

    /// <summary>
    /// Stage-by-stage reasoning explanations.
    /// </summary>
    public IReadOnlyDictionary<string, string> Reasoning { get; init; } =
        new Dictionary<string, string>();
}

/// <summary>
/// Factor contributing to chunk assessment.
/// </summary>
public sealed class AssessmentFactorResult
{
    /// <summary>
    /// Name of the factor.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Score contribution (-1.0 to 1.0).
    /// </summary>
    public double Contribution { get; init; }

    /// <summary>
    /// Explanation of the factor's evaluation.
    /// </summary>
    public string Explanation { get; init; } = string.Empty;
}
