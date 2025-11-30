using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// Contextual Embedding Service implementing Anthropic's Contextual Retrieval approach.
/// Prepends LLM-generated context to chunks before embedding, improving retrieval by up to 67%.
/// </summary>
public class ContextualEmbeddingService : IContextualEmbeddingService
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IContextualHeaderGenerator _headerGenerator;
    private readonly ContextualEmbeddingOptions _options;
    private readonly ILogger<ContextualEmbeddingService> _logger;

    public ContextualEmbeddingService(
        IEmbeddingService embeddingService,
        IContextualHeaderGenerator headerGenerator,
        IOptions<ContextualEmbeddingOptions> options,
        ILogger<ContextualEmbeddingService> logger)
    {
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _headerGenerator = headerGenerator ?? throw new ArgumentNullException(nameof(headerGenerator));
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ContextualEmbeddingResult> GenerateContextualEmbeddingAsync(
        IEnrichedChunk chunk,
        string? documentSummary = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        _logger.LogDebug("Generating contextual embedding for chunk {ChunkId}", chunk.ChunkId);

        // Step 1: Generate contextual header
        var contextualHeader = await _headerGenerator.GenerateAsync(chunk, documentSummary, cancellationToken);

        // Step 2: Combine context with chunk content
        var contextualContent = CombineContextWithContent(contextualHeader, chunk.Content);

        // Step 3: Generate embedding for contextualized content
        var embeddingValues = await _embeddingService.GenerateEmbeddingAsync(contextualContent, cancellationToken);
        var embedding = new EmbeddingVector(embeddingValues, _embeddingService.GetModelName());

        _logger.LogDebug(
            "Generated contextual embedding for chunk {ChunkId}: context length {ContextLength}, total length {TotalLength}",
            chunk.ChunkId, contextualHeader.Length, contextualContent.Length);

        return new ContextualEmbeddingResult
        {
            ChunkId = chunk.ChunkId,
            OriginalContent = chunk.Content,
            ContextualHeader = contextualHeader,
            ContextualContent = contextualContent,
            Embedding = embedding,
            ContextSource = DetermineContextSource(chunk)
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContextualEmbeddingResult>> GenerateContextualEmbeddingsBatchAsync(
        IEnumerable<IEnrichedChunk> chunks,
        string? documentSummary = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        var chunkList = chunks.ToList();
        if (chunkList.Count == 0)
            return Array.Empty<ContextualEmbeddingResult>();

        _logger.LogInformation("Generating contextual embeddings for {Count} chunks", chunkList.Count);

        // Step 1: Generate all contextual headers
        var headers = await _headerGenerator.GenerateBatchAsync(chunkList, documentSummary, cancellationToken);

        // Step 2: Combine contexts with content
        var contextualContents = new List<(IEnrichedChunk Chunk, string Header, string Content)>();
        foreach (var chunk in chunkList)
        {
            var header = headers.GetValueOrDefault(chunk.ChunkId, string.Empty);
            var contextualContent = CombineContextWithContent(header, chunk.Content);
            contextualContents.Add((chunk, header, contextualContent));
        }

        // Step 3: Generate embeddings in batch
        var textsToEmbed = contextualContents.Select(c => c.Content).ToList();
        var embeddingsList = await _embeddingService.GenerateEmbeddingsBatchAsync(textsToEmbed, cancellationToken);
        var embeddings = embeddingsList.Select(e => new EmbeddingVector(e, _embeddingService.GetModelName())).ToList();

        // Step 4: Combine results
        var results = new List<ContextualEmbeddingResult>();
        for (int i = 0; i < contextualContents.Count; i++)
        {
            var (chunk, header, content) = contextualContents[i];
            results.Add(new ContextualEmbeddingResult
            {
                ChunkId = chunk.ChunkId,
                OriginalContent = chunk.Content,
                ContextualHeader = header,
                ContextualContent = content,
                Embedding = embeddings[i],
                ContextSource = DetermineContextSource(chunk)
            });
        }

        _logger.LogInformation(
            "Generated {Count} contextual embeddings (LLM: {LlmCount}, Rule: {RuleCount})",
            results.Count,
            results.Count(r => r.ContextSource == ContextSource.LlmGenerated),
            results.Count(r => r.ContextSource == ContextSource.RuleBased));

        return results.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<DualEmbeddingResult> GenerateDualEmbeddingAsync(
        IEnrichedChunk chunk,
        string? documentSummary = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        _logger.LogDebug("Generating dual embeddings for chunk {ChunkId}", chunk.ChunkId);

        // Generate contextual embedding
        var contextualResult = await GenerateContextualEmbeddingAsync(chunk, documentSummary, cancellationToken);

        // Generate standard embedding (without context)
        var standardEmbeddingValues = await _embeddingService.GenerateEmbeddingAsync(chunk.Content, cancellationToken);
        var standardEmbedding = new EmbeddingVector(standardEmbeddingValues, _embeddingService.GetModelName());

        return new DualEmbeddingResult
        {
            ChunkId = chunk.ChunkId,
            ContextualEmbedding = contextualResult.Embedding,
            StandardEmbedding = standardEmbedding,
            ContextualHeader = contextualResult.ContextualHeader,
            ContextSource = contextualResult.ContextSource
        };
    }

    private string CombineContextWithContent(string contextualHeader, string content)
    {
        if (string.IsNullOrWhiteSpace(contextualHeader))
            return content;

        return _options.ContextPosition switch
        {
            ContextPosition.Prepend => $"{contextualHeader}\n\n{content}",
            ContextPosition.Append => $"{content}\n\n{contextualHeader}",
            ContextPosition.PrependWithSeparator => $"[Context: {contextualHeader}]\n---\n{content}",
            _ => $"{contextualHeader}\n\n{content}"
        };
    }

    private ContextSource DetermineContextSource(IEnrichedChunk chunk)
    {
        // If ContextDependency is above threshold, LLM was likely used
        return chunk.ContextDependency >= _options.LlmThreshold
            ? ContextSource.LlmGenerated
            : ContextSource.RuleBased;
    }
}

/// <summary>
/// Interface for contextual embedding generation
/// </summary>
public interface IContextualEmbeddingService
{
    /// <summary>
    /// Generates embedding with contextual header prepended to chunk content
    /// </summary>
    Task<ContextualEmbeddingResult> GenerateContextualEmbeddingAsync(
        IEnrichedChunk chunk,
        string? documentSummary = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates contextual embeddings for multiple chunks
    /// </summary>
    Task<IReadOnlyList<ContextualEmbeddingResult>> GenerateContextualEmbeddingsBatchAsync(
        IEnumerable<IEnrichedChunk> chunks,
        string? documentSummary = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates both contextual and standard embeddings for hybrid retrieval
    /// </summary>
    Task<DualEmbeddingResult> GenerateDualEmbeddingAsync(
        IEnrichedChunk chunk,
        string? documentSummary = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of contextual embedding generation
/// </summary>
public class ContextualEmbeddingResult
{
    /// <summary>
    /// Chunk identifier
    /// </summary>
    public string ChunkId { get; init; } = string.Empty;

    /// <summary>
    /// Original chunk content
    /// </summary>
    public string OriginalContent { get; init; } = string.Empty;

    /// <summary>
    /// Generated contextual header
    /// </summary>
    public string ContextualHeader { get; init; } = string.Empty;

    /// <summary>
    /// Combined content (context + original)
    /// </summary>
    public string ContextualContent { get; init; } = string.Empty;

    /// <summary>
    /// Embedding vector
    /// </summary>
    public EmbeddingVector? Embedding { get; init; }

    /// <summary>
    /// Source of the contextual header
    /// </summary>
    public ContextSource ContextSource { get; init; }
}

/// <summary>
/// Result containing both contextual and standard embeddings
/// </summary>
public class DualEmbeddingResult
{
    /// <summary>
    /// Chunk identifier
    /// </summary>
    public string ChunkId { get; init; } = string.Empty;

    /// <summary>
    /// Embedding with contextual header
    /// </summary>
    public EmbeddingVector? ContextualEmbedding { get; init; }

    /// <summary>
    /// Standard embedding without context
    /// </summary>
    public EmbeddingVector? StandardEmbedding { get; init; }

    /// <summary>
    /// The contextual header that was used
    /// </summary>
    public string ContextualHeader { get; init; } = string.Empty;

    /// <summary>
    /// Source of the contextual header
    /// </summary>
    public ContextSource ContextSource { get; init; }
}

/// <summary>
/// Source of contextual header generation
/// </summary>
public enum ContextSource
{
    /// <summary>
    /// Generated using rule-based approach
    /// </summary>
    RuleBased,

    /// <summary>
    /// Generated using LLM
    /// </summary>
    LlmGenerated,

    /// <summary>
    /// No context added
    /// </summary>
    None
}

/// <summary>
/// Position where context is added
/// </summary>
public enum ContextPosition
{
    /// <summary>
    /// Prepend context before content
    /// </summary>
    Prepend,

    /// <summary>
    /// Append context after content
    /// </summary>
    Append,

    /// <summary>
    /// Prepend with clear separator
    /// </summary>
    PrependWithSeparator
}

/// <summary>
/// Options for contextual embedding generation
/// </summary>
public class ContextualEmbeddingOptions
{
    /// <summary>
    /// LLM usage threshold based on ContextDependency
    /// </summary>
    public double LlmThreshold { get; set; } = 0.7;

    /// <summary>
    /// Where to position the contextual header
    /// </summary>
    public ContextPosition ContextPosition { get; set; } = ContextPosition.Prepend;

    /// <summary>
    /// Whether to generate dual embeddings (contextual + standard)
    /// </summary>
    public bool GenerateDualEmbeddings { get; set; } = false;

    /// <summary>
    /// Maximum combined content length before truncation
    /// </summary>
    public int MaxCombinedLength { get; set; } = 8192;
}
