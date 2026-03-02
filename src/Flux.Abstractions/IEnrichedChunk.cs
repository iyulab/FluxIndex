namespace Flux.Abstractions;

/// <summary>
/// Enriched chunk contract for RAG system integration.
/// Provides standardized metadata for document chunks across the Flux ecosystem.
/// </summary>
/// <remarks>
/// This is the canonical definition shared across FluxIndex, FileFlux, and WebFlux.
/// All properties represent the superset union of all Flux modules;
/// implementations return <see langword="null"/> or default for inapplicable fields.
/// </remarks>
public interface IEnrichedChunk
{
    /// <summary>
    /// Chunk content text.
    /// </summary>
    string Content { get; }

    /// <summary>
    /// Unique chunk identifier.
    /// </summary>
    string ChunkId { get; }

    /// <summary>
    /// Zero-based chunk index within the document.
    /// </summary>
    int ChunkIndex { get; }

    /// <summary>
    /// Hierarchical heading path from document root.
    /// Example: ["Chapter 1", "Section 1.2", "1.2.1 Purpose"]
    /// </summary>
    IReadOnlyList<string> HeadingPath { get; }

    /// <summary>
    /// Current section title (typically the last element of <see cref="HeadingPath"/>).
    /// </summary>
    string? SectionTitle { get; }

    /// <summary>
    /// Start page number (1-based). <see langword="null"/> if not applicable.
    /// </summary>
    int? StartPage { get; }

    /// <summary>
    /// End page number (for chunks spanning multiple pages).
    /// </summary>
    int? EndPage { get; }

    /// <summary>
    /// Overall chunk quality score (0.0 - 1.0).
    /// </summary>
    double Quality { get; }

    /// <summary>
    /// Context dependency score (0.0 - 1.0).
    /// Higher values indicate more reliance on surrounding context.
    /// Used to determine whether LLM-based contextual header generation is needed.
    /// </summary>
    double ContextDependency { get; }

    /// <summary>
    /// Estimated token count. <see langword="null"/> if not yet calculated.
    /// </summary>
    int? TokenCount { get; }

    /// <summary>
    /// Source document metadata.
    /// </summary>
    ISourceMetadata Source { get; }
}
