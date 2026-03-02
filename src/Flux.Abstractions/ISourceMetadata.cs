namespace Flux.Abstractions;

/// <summary>
/// Source document metadata for traceability and filtering.
/// Provides origin information about the document from which chunks were extracted.
/// </summary>
/// <remarks>
/// This is the canonical definition shared across the Flux ecosystem
/// (FluxIndex, FileFlux, WebFlux). All properties are the superset union;
/// implementations return <see langword="null"/> or default for inapplicable fields.
/// </remarks>
public interface ISourceMetadata
{
    /// <summary>
    /// Unique source document identifier.
    /// </summary>
    string SourceId { get; }

    /// <summary>
    /// Document type (e.g. "PDF", "DOCX", "MD", "url", "html").
    /// </summary>
    string SourceType { get; }

    /// <summary>
    /// Document title.
    /// </summary>
    string Title { get; }

    /// <summary>
    /// Original file path (when the source is a local file).
    /// </summary>
    string? FilePath { get; }

    /// <summary>
    /// Source URL (when the source is a web document).
    /// </summary>
    string? Url { get; }

    /// <summary>
    /// Document creation or processing timestamp.
    /// </summary>
    DateTime CreatedAt { get; }

    /// <summary>
    /// Detected language (ISO 639-1 code, e.g. "ko", "en", "ja").
    /// </summary>
    string Language { get; }

    /// <summary>
    /// Language detection confidence (0.0 - 1.0).
    /// <see langword="null"/> if confidence is not available.
    /// </summary>
    double? LanguageConfidence { get; }

    /// <summary>
    /// Total word count of the source document.
    /// </summary>
    int WordCount { get; }

    /// <summary>
    /// Total number of chunks generated from this source.
    /// </summary>
    int ChunkCount { get; }

    /// <summary>
    /// Total page count (when applicable, e.g. PDF documents).
    /// </summary>
    int? PageCount { get; }

    /// <summary>
    /// Content publication date (e.g. article:published_time for web documents).
    /// </summary>
    DateTime? PublishedAt { get; }

    /// <summary>
    /// Content author.
    /// </summary>
    string? Author { get; }

    /// <summary>
    /// Keyword list extracted from the source.
    /// </summary>
    IReadOnlyList<string>? Keywords { get; }
}
