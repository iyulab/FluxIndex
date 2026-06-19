using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Extensions.FileVault.Domain.Entities;

namespace FluxIndex.Extensions.FileVault.Interfaces;

/// <summary>
/// Pipeline service for processing vault entries.
/// Stages: Source → Extracted → Refined → Memorized
/// </summary>
public interface IVaultPipeline
{
    /// <summary>
    /// Full memorize pipeline: extract → refine → chunk → embed → commit.
    /// Used for new files or when source content has changed.
    /// </summary>
    Task<MemorizeResult> MemorizeAsync(VaultEntry entry, MemorizeOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Refresh pipeline: chunk → embed → commit (skips extraction and refinement).
    /// Used when only vault/ files have been edited (append-text.md, qa.md).
    /// </summary>
    Task<MemorizeResult> RefreshAsync(VaultEntry entry, MemorizeOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Extract content from source file to extracted.md (not git-tracked).
    /// </summary>
    Task ExtractAsync(VaultEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Refine extracted content to vault/refined.md (git-tracked).
    /// Applies LLM processing, image descriptions, etc.
    /// </summary>
    Task RefineAsync(VaultEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Removes chunks from vector store for the given entry.
    /// </summary>
    Task RemoveAsync(VaultEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Searches indexed content using the requested strategy.
    /// </summary>
    /// <param name="query">Search query text.</param>
    /// <param name="documentIds">Optional filter by document IDs (filepath hashes).</param>
    /// <param name="topK">Maximum results to return.</param>
    /// <param name="minScore">Minimum score threshold.</param>
    /// <param name="strategy">Requested search strategy. A <see cref="VaultSearchStrategy.Hybrid"/>
    /// request degrades to vector when no <c>IHybridSearchService</c> is available; the response
    /// reports the strategy actually executed.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Search results plus the strategy that was actually executed.</returns>
    Task<VaultPipelineSearchResponse> SearchAsync(
        string query,
        IEnumerable<string>? documentIds = null,
        int topK = 10,
        float minScore = 0.0f,
        VaultSearchStrategy strategy = VaultSearchStrategy.Vector,
        CancellationToken ct = default);
}

/// <summary>
/// Result of a pipeline search: the matched chunks plus the strategy that was actually executed
/// (which may differ from the requested strategy when hybrid degrades to vector).
/// </summary>
/// <param name="Results">Matched chunks ordered by score.</param>
/// <param name="ExecutedStrategy">The strategy the pipeline actually ran.</param>
public sealed record VaultPipelineSearchResponse(
    IReadOnlyList<PipelineSearchResult> Results,
    VaultSearchStrategy ExecutedStrategy);

/// <summary>
/// Options for memorize/refresh operations.
/// </summary>
public sealed class MemorizeOptions
{
    /// <summary>
    /// Maximum chunk size in tokens.
    /// </summary>
    public int MaxChunkSize { get; set; } = 1024;

    /// <summary>
    /// Overlap size between chunks in tokens.
    /// </summary>
    public int OverlapSize { get; set; } = 128;

    /// <summary>
    /// Chunking strategy (e.g., "Auto", "Semantic", "Paragraph").
    /// </summary>
    public string Strategy { get; set; } = "Auto";

    /// <summary>
    /// Language code for language-aware chunking.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Commit message for git.
    /// </summary>
    public string? CommitMessage { get; set; }

    /// <summary>
    /// Skip git commit after operation.
    /// </summary>
    public bool SkipCommit { get; set; }

    /// <summary>
    /// Preserve existing qa.md content during re-memorize.
    /// When true, existing QA content is backed up and restored after extraction/refinement.
    /// </summary>
    public bool PreserveQaContent { get; set; } = true;

    /// <summary>
    /// Preserve existing append-text.md content during re-memorize.
    /// When true, existing user-added text is backed up and restored after extraction/refinement.
    /// </summary>
    public bool PreserveAppendText { get; set; } = true;

    /// <summary>
    /// When >= 0, the pipeline skips chunks at index 0..StartFromChunkIndex on the assumption
    /// they are already committed in the vector store (from a previous run that was interrupted).
    /// Combined with CheckpointCallback, this enables resumable indexing after host restarts.
    /// Default: -1 (process all chunks, no skip).
    /// </summary>
    public int StartFromChunkIndex { get; set; } = -1;

    /// <summary>
    /// Optional callback invoked after each chunk is fully embedded AND stored.
    /// Receives the 0-based chunk index. When set, the pipeline switches to per-chunk
    /// processing (1 embedding call + 1 store transaction per chunk) instead of batch,
    /// trading ~5-10% normal-path throughput for crash-resilient checkpointing.
    /// Default: null (use batch path, no checkpoint).
    /// </summary>
    public Func<int, CancellationToken, Task>? CheckpointCallback { get; set; }

    /// <summary>
    /// GraphRAG indexing toggle for this memorize/refresh call. Mirrors
    /// <c>IndexingOptions.EnableGraphRAG</c> on the SDK direct-index path so that the FileVault
    /// memorize entry point has indexing semantics equivalent to <c>Indexer.IndexAsync</c>.
    /// <list type="bullet">
    /// <item><description><c>null</c> (default): auto-enable when an <see cref="IGraphRAGService"/> is registered.</description></item>
    /// <item><description><c>true</c>: force enable (throws if no <see cref="IGraphRAGService"/> is wired).</description></item>
    /// <item><description><c>false</c>: force disable even when the service is registered.</description></item>
    /// </list>
    /// </summary>
    public bool? EnableGraphRAG { get; set; }

    /// <summary>
    /// GraphRAG build options applied when GraphRAG is enabled for this call.
    /// Ignored when GraphRAG is disabled. Passed through to
    /// <see cref="IGraphRAGService.BuildIndexAsync"/>.
    /// </summary>
    public GraphRAGBuildOptions? GraphRAGOptions { get; set; }
}

/// <summary>
/// Result of a memorize/refresh operation.
/// </summary>
public sealed class MemorizeResult
{
    /// <summary>
    /// Number of chunks created and indexed.
    /// </summary>
    public int ChunkCount { get; init; }

    /// <summary>
    /// Total content length in characters.
    /// </summary>
    public int ContentLength { get; init; }

    /// <summary>
    /// Processing duration.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Git commit hash (if committed).
    /// </summary>
    public string? CommitHash { get; init; }

    /// <summary>
    /// Whether the operation was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    public static MemorizeResult Succeeded(int chunkCount, int contentLength, TimeSpan duration, string? commitHash = null) => new()
    {
        Success = true,
        ChunkCount = chunkCount,
        ContentLength = contentLength,
        Duration = duration,
        CommitHash = commitHash
    };

    public static MemorizeResult Failed(string errorMessage, TimeSpan duration) => new()
    {
        Success = false,
        ErrorMessage = errorMessage,
        Duration = duration
    };
}

/// <summary>
/// Search result from pipeline search.
/// </summary>
public sealed class PipelineSearchResult
{
    /// <summary>
    /// Document ID (filepath hash).
    /// </summary>
    public string DocumentId { get; init; } = string.Empty;

    /// <summary>
    /// Chunk ID.
    /// </summary>
    public string ChunkId { get; init; } = string.Empty;

    /// <summary>
    /// Chunk index within the document.
    /// </summary>
    public int ChunkIndex { get; init; }

    /// <summary>
    /// Chunk content.
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Similarity score.
    /// </summary>
    public float Score { get; init; }

    /// <summary>
    /// Chunk metadata.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }
}
