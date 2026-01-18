using FluxIndex.Extensions.FileVault.Domain.Entities;
using FluxIndex.Extensions.FileVault.Domain.Enums;

namespace FluxIndex.Extensions.FileVault.Interfaces;

/// <summary>
/// Pipeline service for processing vault entries through stages:
/// Source → Extract → Refine → Chunks → Memorize
/// </summary>
public interface IVaultPipeline
{
    /// <summary>
    /// Extracts content from source file.
    /// Stage 1 → Stage 2
    /// </summary>
    Task ExtractAsync(VaultEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Refines extracted content (auto-processing).
    /// Stage 2 → Stage 3
    /// </summary>
    Task RefineAsync(VaultEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Chunks refined content into segments.
    /// Stage 3 → Stage 4
    /// </summary>
    Task ChunkAsync(VaultEntry entry, ChunkingOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Memorizes chunks (indexes to FluxIndex).
    /// Stage 4 → Stage 5
    /// </summary>
    Task MemorizeAsync(VaultEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Processes entry up to the specified stage.
    /// </summary>
    Task ProcessToStageAsync(VaultEntry entry, ProcessingStage targetStage, CancellationToken ct = default);

    /// <summary>
    /// Reprocesses from a specific stage (when changes detected).
    /// </summary>
    Task ReprocessFromStageAsync(VaultEntry entry, ProcessingStage fromStage, CancellationToken ct = default);
}

/// <summary>
/// Options for chunking stage.
/// </summary>
public sealed class ChunkingOptions
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
    /// Chunking strategy (e.g., "Intelligent", "Semantic", "Paragraph").
    /// </summary>
    public string Strategy { get; set; } = "Intelligent";

    /// <summary>
    /// Language code for language-aware chunking.
    /// </summary>
    public string? Language { get; set; }
}
