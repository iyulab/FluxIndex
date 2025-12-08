using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Domain.Entities;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Corrective RAG (CRAG) service interface.
/// Implements the Corrective Retrieval Augmented Generation pattern that evaluates
/// and corrects retrieved documents based on relevance grading.
/// </summary>
public interface ICorrectiveRAGService
{
    /// <summary>
    /// Performs corrective retrieval with automatic relevance grading and correction.
    /// </summary>
    /// <param name="query">The search query</param>
    /// <param name="options">Corrective RAG options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Corrected retrieval result</returns>
    Task<CorrectiveRAGResult> RetrieveWithCorrectionAsync(
        string query,
        CorrectiveRAGOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Grades retrieved documents for relevance to the query.
    /// </summary>
    /// <param name="query">The search query</param>
    /// <param name="documents">Retrieved documents to grade</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Grading result for each document</returns>
    Task<DocumentGradingResult> GradeDocumentsAsync(
        string query,
        IEnumerable<DocumentChunk> documents,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs knowledge refinement on documents to extract relevant information.
    /// </summary>
    /// <param name="query">The search query</param>
    /// <param name="documents">Documents to refine</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Refined knowledge from documents</returns>
    Task<KnowledgeRefinementResult> RefineKnowledgeAsync(
        string query,
        IEnumerable<DocumentChunk> documents,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs alternative retrieval when initial documents are not relevant.
    /// This may include web search or alternative retrieval strategies.
    /// </summary>
    /// <param name="query">The search query</param>
    /// <param name="originalDocuments">Original (irrelevant) documents for context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Alternative retrieval result</returns>
    Task<AlternativeRetrievalResult> PerformAlternativeRetrievalAsync(
        string query,
        IEnumerable<DocumentChunk> originalDocuments,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for Corrective RAG processing.
/// </summary>
public class CorrectiveRAGOptions
{
    /// <summary>
    /// Maximum number of documents to retrieve initially.
    /// </summary>
    public int MaxInitialDocuments { get; set; } = 10;

    /// <summary>
    /// Threshold for considering a document as "correct" (relevant).
    /// Documents with relevance score above this are used directly.
    /// </summary>
    public double CorrectThreshold { get; set; } = 0.7;

    /// <summary>
    /// Threshold for considering a document as "ambiguous".
    /// Documents between this and CorrectThreshold need supplementation.
    /// </summary>
    public double AmbiguousThreshold { get; set; } = 0.4;

    /// <summary>
    /// Enable web search as alternative retrieval source.
    /// </summary>
    public bool EnableWebSearch { get; set; } = false;

    /// <summary>
    /// Enable query transformation for alternative retrieval.
    /// </summary>
    public bool EnableQueryTransformation { get; set; } = true;

    /// <summary>
    /// Enable knowledge refinement step.
    /// </summary>
    public bool EnableKnowledgeRefinement { get; set; } = true;

    /// <summary>
    /// Maximum documents from alternative retrieval.
    /// </summary>
    public int MaxAlternativeDocuments { get; set; } = 5;

    /// <summary>
    /// Timeout for the entire corrective process.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Enable detailed logging of the correction process.
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = false;

    /// <summary>
    /// Retry count for failed retrievals.
    /// </summary>
    public int RetryCount { get; set; } = 2;
}

/// <summary>
/// Result of Corrective RAG retrieval.
/// </summary>
public class CorrectiveRAGResult
{
    /// <summary>
    /// Final corrected documents after the correction process.
    /// </summary>
    public IReadOnlyList<CorrectedDocument> Documents { get; init; } = Array.Empty<CorrectedDocument>();

    /// <summary>
    /// The correction action that was taken.
    /// </summary>
    public CorrectionAction ActionTaken { get; init; }

    /// <summary>
    /// Grading results for original documents.
    /// </summary>
    public DocumentGradingResult GradingResult { get; init; } = new();

    /// <summary>
    /// Whether alternative retrieval was performed.
    /// </summary>
    public bool UsedAlternativeRetrieval { get; init; }

    /// <summary>
    /// Whether knowledge refinement was applied.
    /// </summary>
    public bool AppliedKnowledgeRefinement { get; init; }

    /// <summary>
    /// Total processing time.
    /// </summary>
    public TimeSpan ProcessingTime { get; init; }

    /// <summary>
    /// Confidence score for the final result (0.0-1.0).
    /// </summary>
    public double ConfidenceScore { get; init; }

    /// <summary>
    /// Whether the correction process was successful.
    /// </summary>
    public bool IsSuccessful { get; init; }

    /// <summary>
    /// Error message if the process failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Detailed correction steps performed.
    /// </summary>
    public IReadOnlyList<CorrectionStep> CorrectionSteps { get; init; } = Array.Empty<CorrectionStep>();

    /// <summary>
    /// Metadata about the correction process.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// A document that has been through the correction process.
/// </summary>
public class CorrectedDocument
{
    /// <summary>
    /// The original document chunk.
    /// </summary>
    public DocumentChunk Chunk { get; init; } = new();

    /// <summary>
    /// The relevance grade assigned to this document.
    /// </summary>
    public DocumentRelevanceGrade Grade { get; init; }

    /// <summary>
    /// The relevance score (0.0-1.0).
    /// </summary>
    public double RelevanceScore { get; init; }

    /// <summary>
    /// Source of this document (original retrieval, alternative, web search).
    /// </summary>
    public DocumentSource Source { get; init; }

    /// <summary>
    /// Refined content after knowledge refinement (if applied).
    /// </summary>
    public string? RefinedContent { get; init; }

    /// <summary>
    /// Key concepts extracted from this document.
    /// </summary>
    public IReadOnlyList<string> KeyConcepts { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Explanation of why this document was included.
    /// </summary>
    public string? InclusionReason { get; init; }
}

/// <summary>
/// Relevance grade for a document.
/// </summary>
public enum DocumentRelevanceGrade
{
    /// <summary>
    /// Document is highly relevant to the query.
    /// </summary>
    Correct,

    /// <summary>
    /// Document is partially relevant, may need supplementation.
    /// </summary>
    Ambiguous,

    /// <summary>
    /// Document is not relevant to the query.
    /// </summary>
    Incorrect
}

/// <summary>
/// Source of a corrected document.
/// </summary>
public enum DocumentSource
{
    /// <summary>
    /// From the initial retrieval.
    /// </summary>
    OriginalRetrieval,

    /// <summary>
    /// From alternative retrieval using different strategy.
    /// </summary>
    AlternativeRetrieval,

    /// <summary>
    /// From web search.
    /// </summary>
    WebSearch,

    /// <summary>
    /// From query transformation retry.
    /// </summary>
    QueryTransformation
}

/// <summary>
/// The correction action taken based on document grading.
/// </summary>
public enum CorrectionAction
{
    /// <summary>
    /// Documents were correct, used directly.
    /// </summary>
    None,

    /// <summary>
    /// Documents were ambiguous, supplemented with additional retrieval.
    /// </summary>
    Supplemented,

    /// <summary>
    /// Documents were incorrect, replaced with alternative retrieval.
    /// </summary>
    Replaced,

    /// <summary>
    /// Mixed action - some documents correct, some replaced.
    /// </summary>
    Mixed
}

/// <summary>
/// Result of document grading.
/// </summary>
public class DocumentGradingResult
{
    /// <summary>
    /// Graded documents with their relevance scores.
    /// </summary>
    public IReadOnlyList<GradedDocumentInfo> GradedDocuments { get; init; } = Array.Empty<GradedDocumentInfo>();

    /// <summary>
    /// Overall assessment of the document set.
    /// </summary>
    public OverallAssessment Assessment { get; init; }

    /// <summary>
    /// Average relevance score across all documents.
    /// </summary>
    public double AverageRelevanceScore { get; init; }

    /// <summary>
    /// Count of correct documents.
    /// </summary>
    public int CorrectCount { get; init; }

    /// <summary>
    /// Count of ambiguous documents.
    /// </summary>
    public int AmbiguousCount { get; init; }

    /// <summary>
    /// Count of incorrect documents.
    /// </summary>
    public int IncorrectCount { get; init; }

    /// <summary>
    /// Processing time for grading.
    /// </summary>
    public TimeSpan ProcessingTime { get; init; }
}

/// <summary>
/// Information about a graded document.
/// </summary>
public class GradedDocumentInfo
{
    /// <summary>
    /// The document chunk.
    /// </summary>
    public DocumentChunk Document { get; init; } = new();

    /// <summary>
    /// The assigned grade.
    /// </summary>
    public DocumentRelevanceGrade Grade { get; init; }

    /// <summary>
    /// The relevance score (0.0-1.0).
    /// </summary>
    public double RelevanceScore { get; init; }

    /// <summary>
    /// Explanation of the grading decision.
    /// </summary>
    public string? Explanation { get; init; }

    /// <summary>
    /// Key terms that contributed to relevance.
    /// </summary>
    public IReadOnlyList<string> MatchingTerms { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Semantic similarity score.
    /// </summary>
    public double SemanticSimilarity { get; init; }

    /// <summary>
    /// Keyword match score.
    /// </summary>
    public double KeywordMatchScore { get; init; }
}

/// <summary>
/// Overall assessment of the document set.
/// </summary>
public enum OverallAssessment
{
    /// <summary>
    /// Most documents are correct - proceed with original retrieval.
    /// </summary>
    Correct,

    /// <summary>
    /// Mixed results - supplement with additional retrieval.
    /// </summary>
    Ambiguous,

    /// <summary>
    /// Most documents are incorrect - use alternative retrieval.
    /// </summary>
    Incorrect
}

/// <summary>
/// Result of knowledge refinement.
/// </summary>
public class KnowledgeRefinementResult
{
    /// <summary>
    /// Refined documents with extracted knowledge.
    /// </summary>
    public IReadOnlyList<RefinedDocumentKnowledge> RefinedDocuments { get; init; } = Array.Empty<RefinedDocumentKnowledge>();

    /// <summary>
    /// Overall summary of the extracted knowledge.
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// Key facts extracted from all documents.
    /// </summary>
    public IReadOnlyList<string> KeyFacts { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Processing time for refinement.
    /// </summary>
    public TimeSpan ProcessingTime { get; init; }

    /// <summary>
    /// Whether refinement was successful.
    /// </summary>
    public bool IsSuccessful { get; init; }
}

/// <summary>
/// Refined knowledge from a single document.
/// </summary>
public class RefinedDocumentKnowledge
{
    /// <summary>
    /// Original document ID.
    /// </summary>
    public string DocumentId { get; init; } = string.Empty;

    /// <summary>
    /// Refined content with only relevant information.
    /// </summary>
    public string RefinedContent { get; init; } = string.Empty;

    /// <summary>
    /// Key concepts from this document.
    /// </summary>
    public IReadOnlyList<string> KeyConcepts { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Facts extracted from this document.
    /// </summary>
    public IReadOnlyList<string> ExtractedFacts { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Confidence in the refined content.
    /// </summary>
    public double Confidence { get; init; }
}

/// <summary>
/// Result of alternative retrieval.
/// </summary>
public class AlternativeRetrievalResult
{
    /// <summary>
    /// Documents from alternative retrieval.
    /// </summary>
    public IReadOnlyList<DocumentChunk> Documents { get; init; } = Array.Empty<DocumentChunk>();

    /// <summary>
    /// The strategy used for alternative retrieval.
    /// </summary>
    public AlternativeRetrievalStrategy Strategy { get; init; }

    /// <summary>
    /// Transformed query used (if query transformation was applied).
    /// </summary>
    public string? TransformedQuery { get; init; }

    /// <summary>
    /// Processing time.
    /// </summary>
    public TimeSpan ProcessingTime { get; init; }

    /// <summary>
    /// Whether alternative retrieval was successful.
    /// </summary>
    public bool IsSuccessful { get; init; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Strategy for alternative retrieval.
/// </summary>
public enum AlternativeRetrievalStrategy
{
    /// <summary>
    /// Retry with transformed query.
    /// </summary>
    QueryTransformation,

    /// <summary>
    /// Use different search strategy (e.g., keyword vs semantic).
    /// </summary>
    DifferentStrategy,

    /// <summary>
    /// Web search.
    /// </summary>
    WebSearch,

    /// <summary>
    /// Expand search to broader scope.
    /// </summary>
    ScopeExpansion,

    /// <summary>
    /// Use multiple strategies combined.
    /// </summary>
    Combined
}

/// <summary>
/// A step in the correction process.
/// </summary>
public class CorrectionStep
{
    /// <summary>
    /// Step number.
    /// </summary>
    public int StepNumber { get; init; }

    /// <summary>
    /// Type of step.
    /// </summary>
    public CorrectionStepType Type { get; init; }

    /// <summary>
    /// Description of what was done.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Input document count.
    /// </summary>
    public int InputCount { get; init; }

    /// <summary>
    /// Output document count.
    /// </summary>
    public int OutputCount { get; init; }

    /// <summary>
    /// Time taken for this step.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Whether this step was successful.
    /// </summary>
    public bool IsSuccessful { get; init; }
}

/// <summary>
/// Type of correction step.
/// </summary>
public enum CorrectionStepType
{
    /// <summary>
    /// Initial retrieval.
    /// </summary>
    InitialRetrieval,

    /// <summary>
    /// Document grading.
    /// </summary>
    Grading,

    /// <summary>
    /// Alternative retrieval.
    /// </summary>
    AlternativeRetrieval,

    /// <summary>
    /// Knowledge refinement.
    /// </summary>
    KnowledgeRefinement,

    /// <summary>
    /// Document filtering.
    /// </summary>
    Filtering,

    /// <summary>
    /// Result merging.
    /// </summary>
    Merging
}
