using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Domain.Entities;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Retrieval Verification Service for real-time validation of retrieved documents.
/// Provides document-level relevance grading, hallucination detection, factual grounding,
/// and confidence-based filtering during the retrieval pipeline.
/// </summary>
public interface IRetrievalVerificationService
{
    /// <summary>
    /// Verifies retrieved documents against the query and returns graded results.
    /// </summary>
    /// <param name="query">The original search query</param>
    /// <param name="documents">Retrieved documents to verify</param>
    /// <param name="options">Verification options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Verification result with graded documents</returns>
    Task<RetrievalVerificationResult> VerifyAsync(
        string query,
        IEnumerable<DocumentChunk> documents,
        VerificationOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Grades a single document for relevance to the query.
    /// </summary>
    /// <param name="query">The search query</param>
    /// <param name="document">Document to grade</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Document grade with relevance score</returns>
    Task<DocumentGrade> GradeDocumentAsync(
        string query,
        DocumentChunk document,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects potential hallucination risks in retrieved documents.
    /// </summary>
    /// <param name="query">The search query</param>
    /// <param name="documents">Documents to analyze</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Hallucination risk assessment</returns>
    Task<HallucinationRiskAssessment> DetectHallucinationRisksAsync(
        string query,
        IEnumerable<DocumentChunk> documents,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if documents provide sufficient factual grounding for the query.
    /// </summary>
    /// <param name="query">The search query</param>
    /// <param name="documents">Retrieved documents</param>
    /// <param name="claims">Optional specific claims to verify</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Factual grounding assessment</returns>
    Task<FactualGroundingResult> CheckFactualGroundingAsync(
        string query,
        IEnumerable<DocumentChunk> documents,
        IEnumerable<string>? claims = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Filters documents based on verification confidence threshold.
    /// </summary>
    /// <param name="gradedDocuments">Documents with grades</param>
    /// <param name="threshold">Minimum confidence threshold (0.0-1.0)</param>
    /// <returns>Filtered documents meeting the threshold</returns>
    IEnumerable<GradedDocument> FilterByConfidence(
        IEnumerable<GradedDocument> gradedDocuments,
        double threshold = 0.5);

    /// <summary>
    /// Calculates support scores for claims against retrieved documents.
    /// </summary>
    /// <param name="claims">Claims to verify support for</param>
    /// <param name="documents">Documents to check for support</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Support scores for each claim</returns>
    Task<ClaimSupportResult> CalculateClaimSupportAsync(
        IEnumerable<string> claims,
        IEnumerable<DocumentChunk> documents,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Provides verification recommendation based on current results.
    /// </summary>
    /// <param name="verificationResult">Current verification result</param>
    /// <returns>Recommendation for next steps</returns>
    VerificationRecommendation GetRecommendation(RetrievalVerificationResult verificationResult);
}

/// <summary>
/// Options for retrieval verification.
/// </summary>
public class VerificationOptions
{
    /// <summary>
    /// Minimum relevance score threshold (0.0-1.0). Default: 0.5
    /// </summary>
    public double RelevanceThreshold { get; init; } = 0.5;

    /// <summary>
    /// Maximum hallucination risk tolerance (0.0-1.0). Default: 0.3
    /// </summary>
    public double MaxHallucinationRisk { get; init; } = 0.3;

    /// <summary>
    /// Minimum factual grounding score (0.0-1.0). Default: 0.6
    /// </summary>
    public double MinFactualGrounding { get; init; } = 0.6;

    /// <summary>
    /// Enable LLM-based verification (more accurate but slower).
    /// </summary>
    public bool UseLlmVerification { get; init; } = true;

    /// <summary>
    /// Maximum number of documents to verify (for performance).
    /// </summary>
    public int MaxDocumentsToVerify { get; init; } = 20;

    /// <summary>
    /// Enable parallel verification for performance.
    /// </summary>
    public bool EnableParallelVerification { get; init; } = true;

    /// <summary>
    /// Verification timeout per document.
    /// </summary>
    public TimeSpan PerDocumentTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Include detailed reasoning in verification results.
    /// </summary>
    public bool IncludeDetailedReasoning { get; init; } = false;

    /// <summary>
    /// Strict mode - fail on any verification concern.
    /// </summary>
    public bool StrictMode { get; init; } = false;

    /// <summary>
    /// Custom grading criteria.
    /// </summary>
    public GradingCriteria? CustomCriteria { get; init; }
}

/// <summary>
/// Custom grading criteria for domain-specific verification.
/// </summary>
public class GradingCriteria
{
    /// <summary>
    /// Weight for semantic relevance (0.0-1.0).
    /// </summary>
    public double SemanticRelevanceWeight { get; init; } = 0.4;

    /// <summary>
    /// Weight for keyword match (0.0-1.0).
    /// </summary>
    public double KeywordMatchWeight { get; init; } = 0.2;

    /// <summary>
    /// Weight for entity overlap (0.0-1.0).
    /// </summary>
    public double EntityOverlapWeight { get; init; } = 0.2;

    /// <summary>
    /// Weight for contextual fit (0.0-1.0).
    /// </summary>
    public double ContextualFitWeight { get; init; } = 0.2;

    /// <summary>
    /// Required entities that must be present.
    /// </summary>
    public IReadOnlyList<string> RequiredEntities { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Prohibited content patterns.
    /// </summary>
    public IReadOnlyList<string> ProhibitedPatterns { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Result of retrieval verification.
/// </summary>
public class RetrievalVerificationResult
{
    /// <summary>
    /// Original query.
    /// </summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// Documents with grades.
    /// </summary>
    public IReadOnlyList<GradedDocument> GradedDocuments { get; init; } = Array.Empty<GradedDocument>();

    /// <summary>
    /// Overall verification status.
    /// </summary>
    public VerificationStatus Status { get; init; }

    /// <summary>
    /// Overall confidence score (0.0-1.0).
    /// </summary>
    public double OverallConfidence { get; init; }

    /// <summary>
    /// Hallucination risk assessment.
    /// </summary>
    public HallucinationRiskAssessment? HallucinationRisk { get; init; }

    /// <summary>
    /// Factual grounding assessment.
    /// </summary>
    public FactualGroundingResult? FactualGrounding { get; init; }

    /// <summary>
    /// Verification issues found.
    /// </summary>
    public IReadOnlyList<VerificationIssue> Issues { get; init; } = Array.Empty<VerificationIssue>();

    /// <summary>
    /// Statistics about the verification.
    /// </summary>
    public VerificationStatistics Statistics { get; init; } = new();

    /// <summary>
    /// Verification timestamp.
    /// </summary>
    public DateTimeOffset VerifiedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Processing duration.
    /// </summary>
    public TimeSpan ProcessingTime { get; init; }

    /// <summary>
    /// Gets documents that passed verification.
    /// </summary>
    public IEnumerable<GradedDocument> GetPassedDocuments() =>
        GradedDocuments.Where(d => d.Grade.Relevance == RelevanceGrade.Relevant);

    /// <summary>
    /// Gets the top-k verified documents.
    /// </summary>
    public IEnumerable<GradedDocument> GetTopK(int k) =>
        GradedDocuments.OrderByDescending(d => d.Grade.ConfidenceScore).Take(k);
}

/// <summary>
/// A document with its verification grade.
/// </summary>
public class GradedDocument
{
    /// <summary>
    /// The original document chunk.
    /// </summary>
    public DocumentChunk Document { get; init; } = null!;

    /// <summary>
    /// The assigned grade.
    /// </summary>
    public DocumentGrade Grade { get; init; } = new();

    /// <summary>
    /// Position in original result set.
    /// </summary>
    public int OriginalRank { get; init; }

    /// <summary>
    /// New rank after verification (if reranked).
    /// </summary>
    public int? VerifiedRank { get; init; }
}

/// <summary>
/// Grade assigned to a document.
/// </summary>
public class DocumentGrade
{
    /// <summary>
    /// Relevance classification.
    /// </summary>
    public RelevanceGrade Relevance { get; init; } = RelevanceGrade.Unknown;

    /// <summary>
    /// Confidence score (0.0-1.0).
    /// </summary>
    public double ConfidenceScore { get; init; }

    /// <summary>
    /// Semantic similarity score (0.0-1.0).
    /// </summary>
    public double SemanticSimilarity { get; init; }

    /// <summary>
    /// Keyword match score (0.0-1.0).
    /// </summary>
    public double KeywordMatch { get; init; }

    /// <summary>
    /// Entity overlap score (0.0-1.0).
    /// </summary>
    public double EntityOverlap { get; init; }

    /// <summary>
    /// Contextual fit score (0.0-1.0).
    /// </summary>
    public double ContextualFit { get; init; }

    /// <summary>
    /// Factual support score (0.0-1.0).
    /// </summary>
    public double FactualSupport { get; init; }

    /// <summary>
    /// Hallucination risk for this document (0.0-1.0).
    /// </summary>
    public double HallucinationRisk { get; init; }

    /// <summary>
    /// Reasoning for the grade (if detailed reasoning enabled).
    /// </summary>
    public string? Reasoning { get; init; }

    /// <summary>
    /// Specific issues with this document.
    /// </summary>
    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();

    /// <summary>
    /// LLM-generated explanation (if LLM verification used).
    /// </summary>
    public string? LlmExplanation { get; init; }
}

/// <summary>
/// Relevance grade classification.
/// </summary>
public enum RelevanceGrade
{
    /// <summary>
    /// Grade not yet determined.
    /// </summary>
    Unknown,

    /// <summary>
    /// Document is clearly relevant to the query.
    /// </summary>
    Relevant,

    /// <summary>
    /// Document is partially relevant or tangentially related.
    /// </summary>
    PartiallyRelevant,

    /// <summary>
    /// Document is not relevant to the query.
    /// </summary>
    NotRelevant,

    /// <summary>
    /// Relevance is ambiguous or uncertain.
    /// </summary>
    Ambiguous
}

/// <summary>
/// Verification status.
/// </summary>
public enum VerificationStatus
{
    /// <summary>
    /// All documents passed verification.
    /// </summary>
    Passed,

    /// <summary>
    /// Some documents passed verification.
    /// </summary>
    PartiallyPassed,

    /// <summary>
    /// Verification failed - insufficient relevant documents.
    /// </summary>
    Failed,

    /// <summary>
    /// Verification raised warnings but passed.
    /// </summary>
    Warning,

    /// <summary>
    /// Verification could not be completed.
    /// </summary>
    Inconclusive
}

/// <summary>
/// Hallucination risk assessment.
/// </summary>
public class HallucinationRiskAssessment
{
    /// <summary>
    /// Overall hallucination risk score (0.0-1.0).
    /// </summary>
    public double OverallRisk { get; init; }

    /// <summary>
    /// Risk level classification.
    /// </summary>
    public HallucinationRiskLevel RiskLevel { get; init; }

    /// <summary>
    /// Risk factors identified.
    /// </summary>
    public IReadOnlyList<HallucinationRiskFactor> RiskFactors { get; init; } = Array.Empty<HallucinationRiskFactor>();

    /// <summary>
    /// Documents with high hallucination risk.
    /// </summary>
    public IReadOnlyList<string> HighRiskDocumentIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Mitigation suggestions.
    /// </summary>
    public IReadOnlyList<string> MitigationSuggestions { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Confidence in the assessment (0.0-1.0).
    /// </summary>
    public double AssessmentConfidence { get; init; }
}

/// <summary>
/// Hallucination risk level.
/// </summary>
public enum HallucinationRiskLevel
{
    /// <summary>
    /// Very low risk of hallucination.
    /// </summary>
    VeryLow,

    /// <summary>
    /// Low risk of hallucination.
    /// </summary>
    Low,

    /// <summary>
    /// Moderate risk of hallucination.
    /// </summary>
    Moderate,

    /// <summary>
    /// High risk of hallucination.
    /// </summary>
    High,

    /// <summary>
    /// Very high risk of hallucination.
    /// </summary>
    VeryHigh
}

/// <summary>
/// A specific hallucination risk factor.
/// </summary>
public class HallucinationRiskFactor
{
    /// <summary>
    /// Type of risk factor.
    /// </summary>
    public HallucinationRiskType Type { get; init; }

    /// <summary>
    /// Severity (0.0-1.0).
    /// </summary>
    public double Severity { get; init; }

    /// <summary>
    /// Description of the risk.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Affected document IDs.
    /// </summary>
    public IReadOnlyList<string> AffectedDocuments { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Evidence for this risk factor.
    /// </summary>
    public string? Evidence { get; init; }
}

/// <summary>
/// Types of hallucination risks.
/// </summary>
public enum HallucinationRiskType
{
    /// <summary>
    /// Documents contain contradictory information.
    /// </summary>
    ContradictoryInformation,

    /// <summary>
    /// Insufficient evidence for the query.
    /// </summary>
    InsufficientEvidence,

    /// <summary>
    /// Documents are outdated.
    /// </summary>
    OutdatedInformation,

    /// <summary>
    /// Documents lack specificity for the query.
    /// </summary>
    LackOfSpecificity,

    /// <summary>
    /// Entity confusion or mismatch.
    /// </summary>
    EntityConfusion,

    /// <summary>
    /// Temporal inconsistency.
    /// </summary>
    TemporalInconsistency,

    /// <summary>
    /// Unreliable source indicators.
    /// </summary>
    UnreliableSource,

    /// <summary>
    /// Missing critical context.
    /// </summary>
    MissingContext
}

/// <summary>
/// Result of factual grounding check.
/// </summary>
public class FactualGroundingResult
{
    /// <summary>
    /// Overall grounding score (0.0-1.0).
    /// </summary>
    public double OverallScore { get; init; }

    /// <summary>
    /// Whether the grounding is sufficient.
    /// </summary>
    public bool IsSufficient { get; init; }

    /// <summary>
    /// Coverage of query aspects (0.0-1.0).
    /// </summary>
    public double QueryCoverage { get; init; }

    /// <summary>
    /// Evidence quality score (0.0-1.0).
    /// </summary>
    public double EvidenceQuality { get; init; }

    /// <summary>
    /// Source diversity score (0.0-1.0).
    /// </summary>
    public double SourceDiversity { get; init; }

    /// <summary>
    /// Grounded claims.
    /// </summary>
    public IReadOnlyList<GroundedClaim> GroundedClaims { get; init; } = Array.Empty<GroundedClaim>();

    /// <summary>
    /// Ungrounded aspects of the query.
    /// </summary>
    public IReadOnlyList<string> UngroundedAspects { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Suggestions for improving grounding.
    /// </summary>
    public IReadOnlyList<string> ImprovementSuggestions { get; init; } = Array.Empty<string>();
}

/// <summary>
/// A claim with its grounding evidence.
/// </summary>
public class GroundedClaim
{
    /// <summary>
    /// The claim text.
    /// </summary>
    public string Claim { get; init; } = string.Empty;

    /// <summary>
    /// Grounding score (0.0-1.0).
    /// </summary>
    public double GroundingScore { get; init; }

    /// <summary>
    /// Supporting document IDs.
    /// </summary>
    public IReadOnlyList<string> SupportingDocuments { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Supporting evidence excerpts.
    /// </summary>
    public IReadOnlyList<string> EvidenceExcerpts { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Confidence in the grounding (0.0-1.0).
    /// </summary>
    public double Confidence { get; init; }
}

/// <summary>
/// Result of claim support calculation.
/// </summary>
public class ClaimSupportResult
{
    /// <summary>
    /// Support scores for each claim.
    /// </summary>
    public IReadOnlyList<ClaimSupport> Claims { get; init; } = Array.Empty<ClaimSupport>();

    /// <summary>
    /// Overall support score (0.0-1.0).
    /// </summary>
    public double OverallSupport { get; init; }

    /// <summary>
    /// Claims with full support.
    /// </summary>
    public int FullySupportedCount { get; init; }

    /// <summary>
    /// Claims with partial support.
    /// </summary>
    public int PartiallySupportedCount { get; init; }

    /// <summary>
    /// Claims with no support.
    /// </summary>
    public int UnsupportedCount { get; init; }
}

/// <summary>
/// Support information for a single claim.
/// </summary>
public class ClaimSupport
{
    /// <summary>
    /// The claim text.
    /// </summary>
    public string Claim { get; init; } = string.Empty;

    /// <summary>
    /// Support level.
    /// </summary>
    public SupportLevel Level { get; init; }

    /// <summary>
    /// Support score (0.0-1.0).
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// Documents supporting this claim.
    /// </summary>
    public IReadOnlyList<DocumentSupport> SupportingDocuments { get; init; } = Array.Empty<DocumentSupport>();
}

/// <summary>
/// Support level for a claim.
/// </summary>
public enum SupportLevel
{
    /// <summary>
    /// Claim is fully supported by documents.
    /// </summary>
    FullySupported,

    /// <summary>
    /// Claim is partially supported.
    /// </summary>
    PartiallySupported,

    /// <summary>
    /// Claim has no support in documents.
    /// </summary>
    NotSupported,

    /// <summary>
    /// Documents contradict the claim.
    /// </summary>
    Contradicted
}

/// <summary>
/// Document's support for a claim.
/// </summary>
public class DocumentSupport
{
    /// <summary>
    /// Document ID.
    /// </summary>
    public string DocumentId { get; init; } = string.Empty;

    /// <summary>
    /// Support score from this document (0.0-1.0).
    /// </summary>
    public double SupportScore { get; init; }

    /// <summary>
    /// Relevant excerpt from the document.
    /// </summary>
    public string? RelevantExcerpt { get; init; }

    /// <summary>
    /// Type of support (direct, indirect, implied).
    /// </summary>
    public SupportType Type { get; init; }
}

/// <summary>
/// Type of support a document provides.
/// </summary>
public enum SupportType
{
    /// <summary>
    /// Direct, explicit support.
    /// </summary>
    Direct,

    /// <summary>
    /// Indirect support through related information.
    /// </summary>
    Indirect,

    /// <summary>
    /// Implied support that can be inferred.
    /// </summary>
    Implied,

    /// <summary>
    /// Contradicts the claim.
    /// </summary>
    Contradiction
}

/// <summary>
/// Issues found during verification.
/// </summary>
public class VerificationIssue
{
    /// <summary>
    /// Issue type.
    /// </summary>
    public VerificationIssueType Type { get; init; }

    /// <summary>
    /// Severity level (0.0-1.0).
    /// </summary>
    public double Severity { get; init; }

    /// <summary>
    /// Description of the issue.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Affected document IDs.
    /// </summary>
    public IReadOnlyList<string> AffectedDocuments { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Suggested resolution.
    /// </summary>
    public string? SuggestedResolution { get; init; }
}

/// <summary>
/// Types of verification issues.
/// </summary>
public enum VerificationIssueType
{
    /// <summary>
    /// Relevance below threshold.
    /// </summary>
    LowRelevance,

    /// <summary>
    /// High hallucination risk.
    /// </summary>
    HighHallucinationRisk,

    /// <summary>
    /// Insufficient factual grounding.
    /// </summary>
    InsufficientGrounding,

    /// <summary>
    /// Contradictory information.
    /// </summary>
    ContradictoryInformation,

    /// <summary>
    /// Missing required entities.
    /// </summary>
    MissingRequiredEntities,

    /// <summary>
    /// Contains prohibited content.
    /// </summary>
    ProhibitedContent,

    /// <summary>
    /// Too few verified documents.
    /// </summary>
    InsufficientDocuments,

    /// <summary>
    /// Verification timeout.
    /// </summary>
    VerificationTimeout
}

/// <summary>
/// Statistics about the verification process.
/// </summary>
public class VerificationStatistics
{
    /// <summary>
    /// Total documents evaluated.
    /// </summary>
    public int TotalDocuments { get; init; }

    /// <summary>
    /// Documents graded as relevant.
    /// </summary>
    public int RelevantCount { get; init; }

    /// <summary>
    /// Documents graded as partially relevant.
    /// </summary>
    public int PartiallyRelevantCount { get; init; }

    /// <summary>
    /// Documents graded as not relevant.
    /// </summary>
    public int NotRelevantCount { get; init; }

    /// <summary>
    /// Documents with ambiguous relevance.
    /// </summary>
    public int AmbiguousCount { get; init; }

    /// <summary>
    /// Average confidence score.
    /// </summary>
    public double AverageConfidence { get; init; }

    /// <summary>
    /// Average hallucination risk.
    /// </summary>
    public double AverageHallucinationRisk { get; init; }

    /// <summary>
    /// Pass rate (relevant / total).
    /// </summary>
    public double PassRate => TotalDocuments > 0 ? (double)RelevantCount / TotalDocuments : 0;

    /// <summary>
    /// Whether LLM verification was used.
    /// </summary>
    public bool LlmVerificationUsed { get; init; }

    /// <summary>
    /// Number of documents skipped due to timeout.
    /// </summary>
    public int TimeoutCount { get; init; }
}

/// <summary>
/// Recommendation based on verification results.
/// </summary>
public class VerificationRecommendation
{
    /// <summary>
    /// Recommended action.
    /// </summary>
    public RecommendedAction Action { get; init; }

    /// <summary>
    /// Confidence in recommendation (0.0-1.0).
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Reasoning for the recommendation.
    /// </summary>
    public string Reasoning { get; init; } = string.Empty;

    /// <summary>
    /// Specific suggestions.
    /// </summary>
    public IReadOnlyList<string> Suggestions { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Whether to proceed with current results.
    /// </summary>
    public bool ShouldProceed { get; init; }

    /// <summary>
    /// Suggested query modifications if retry recommended.
    /// </summary>
    public IReadOnlyList<string> SuggestedQueryModifications { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Recommended actions based on verification.
/// </summary>
public enum RecommendedAction
{
    /// <summary>
    /// Proceed with current results.
    /// </summary>
    Proceed,

    /// <summary>
    /// Use only top verified results.
    /// </summary>
    UseTopVerified,

    /// <summary>
    /// Retry with modified query.
    /// </summary>
    RetryWithModifiedQuery,

    /// <summary>
    /// Expand search scope.
    /// </summary>
    ExpandSearch,

    /// <summary>
    /// Add more context to query.
    /// </summary>
    AddContext,

    /// <summary>
    /// Warn user about limitations.
    /// </summary>
    WarnUser,

    /// <summary>
    /// Abort - results too unreliable.
    /// </summary>
    Abort
}
