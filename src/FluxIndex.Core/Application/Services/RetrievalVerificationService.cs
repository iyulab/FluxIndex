using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// Retrieval Verification Service for real-time validation of retrieved documents.
/// Implements CRAG (Corrective RAG) patterns for hallucination detection,
/// relevance verification, factual grounding, and confidence-based filtering.
/// </summary>
public partial class RetrievalVerificationService : Interfaces.IRetrievalVerificationService
{
    private static readonly char[] TokenizeSeparators = [' ', ',', '.', '!', '?', ';', ':', '\n', '\r', '\t', '(', ')', '[', ']', '"'];
    private static readonly string[] ClaimSplitSeparators = [" and ", " or ", ",", ";"];
    private static readonly char[] SentenceSplitSeparators = ['.', '!', '?'];

    private readonly IEmbeddingService _embeddingService;
    private readonly ITextCompletionService? _completionService;
    private readonly RetrievalVerificationServiceOptions _options;
    private readonly ILogger<RetrievalVerificationService> _logger;

    // Cache for document embeddings
    private readonly ConcurrentDictionary<string, float[]> _embeddingCache;

    public RetrievalVerificationService(
        IEmbeddingService embeddingService,
        ITextCompletionService? completionService,
        IOptions<RetrievalVerificationServiceOptions> options,
        ILogger<RetrievalVerificationService> logger)
    {
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _completionService = completionService;
        _options = options?.Value ?? new RetrievalVerificationServiceOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _embeddingCache = new ConcurrentDictionary<string, float[]>();
    }

    /// <inheritdoc />
    public async Task<RetrievalVerificationResult> VerifyAsync(
        string query,
        IEnumerable<DocumentChunk> documents,
        VerificationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(documents);

        var opts = options ?? new VerificationOptions();
        var documentList = documents.Take(opts.MaxDocumentsToVerify).ToList();
        var stopwatch = Stopwatch.StartNew();

        if (documentList.Count == 0)
        {
            return CreateEmptyResult(query, "No documents to verify");
        }

        if (_logger.IsEnabled(LogLevel.Debug))
            LogRetrievalVerification4(_logger, documentList.Count, query);

        try
        {
            // Step 1: Grade all documents
            var gradedDocuments = await GradeDocumentsAsync(query, documentList, opts, cancellationToken);

            // Step 2: Detect hallucination risks
            HallucinationRiskAssessment? hallucinationRisk = null;
            if (opts.UseLlmVerification || _options.AlwaysCheckHallucination)
            {
                hallucinationRisk = await DetectHallucinationRisksAsync(query, documentList, cancellationToken);
            }

            // Step 3: Check factual grounding
            FactualGroundingResult? factualGrounding = null;
            if (opts.MinFactualGrounding > 0)
            {
                factualGrounding = await CheckFactualGroundingAsync(query, documentList, null, cancellationToken);
            }

            // Step 4: Identify issues
            var issues = IdentifyVerificationIssues(gradedDocuments, hallucinationRisk, factualGrounding, opts);

            // Step 5: Calculate overall confidence and determine status
            var overallConfidence = CalculateOverallConfidence(gradedDocuments);
            var status = DetermineVerificationStatus(gradedDocuments, issues, opts);

            // Step 6: Generate statistics
            var statistics = GenerateStatistics(gradedDocuments, hallucinationRisk, opts.UseLlmVerification);

            stopwatch.Stop();

            var result = new RetrievalVerificationResult
            {
                Query = query,
                GradedDocuments = gradedDocuments,
                Status = status,
                OverallConfidence = overallConfidence,
                HallucinationRisk = hallucinationRisk,
                FactualGrounding = factualGrounding,
                Issues = issues,
                Statistics = statistics,
                ProcessingTime = stopwatch.Elapsed
            };

            if (_logger.IsEnabled(LogLevel.Debug))
                LogRetrievalVerification3(_logger, status, overallConfidence, statistics.RelevantCount, statistics.TotalDocuments);

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                LogRetrievalVerification2(_logger, ex, query);
            return CreateFailedResult(query, ex.Message, stopwatch.Elapsed);
        }
    }

    /// <inheritdoc />
    public async Task<DocumentGrade> GradeDocumentAsync(
        string query,
        DocumentChunk document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(document);

        try
        {
            // Get embeddings
            var queryEmbedding = await GetOrCreateEmbeddingAsync(query, cancellationToken);
            var docEmbedding = await GetDocumentEmbeddingAsync(document, cancellationToken);

            // Calculate component scores
            var semanticSimilarity = CalculateCosineSimilarity(queryEmbedding, docEmbedding);
            var keywordMatch = CalculateKeywordMatch(query, document.Content);
            var entityOverlap = CalculateEntityOverlap(query, document.Content);
            var contextualFit = CalculateContextualFit(query, document.Content);

            // Weighted combination
            var criteria = _options.DefaultCriteria;
            var confidenceScore =
                (semanticSimilarity * criteria.SemanticRelevanceWeight) +
                (keywordMatch * criteria.KeywordMatchWeight) +
                (entityOverlap * criteria.EntityOverlapWeight) +
                (contextualFit * criteria.ContextualFitWeight);

            // Determine relevance grade
            var relevance = DetermineRelevanceGrade(confidenceScore);

            // Detect issues
            var issues = DetectDocumentIssues(query, document, semanticSimilarity, keywordMatch);

            // Calculate hallucination risk for this document
            var hallucinationRisk = CalculateDocumentHallucinationRisk(document, semanticSimilarity);

            // Get LLM explanation if enabled
            string? llmExplanation = null;
            if (_completionService != null && _options.UseLlmForGrading)
            {
                llmExplanation = await GetLlmGradingExplanationAsync(query, document, cancellationToken);
            }

            return new DocumentGrade
            {
                Relevance = relevance,
                ConfidenceScore = confidenceScore,
                SemanticSimilarity = semanticSimilarity,
                KeywordMatch = keywordMatch,
                EntityOverlap = entityOverlap,
                ContextualFit = contextualFit,
                FactualSupport = confidenceScore, // Simplified for non-claim-specific checks
                HallucinationRisk = hallucinationRisk,
                Issues = issues,
                LlmExplanation = llmExplanation
            };
        }
        catch (Exception ex)
        {
            LogRetrievalVerification1(_logger, ex, document.Id);
            return new DocumentGrade
            {
                Relevance = Interfaces.RelevanceGrade.Unknown,
                ConfidenceScore = 0,
                Issues = new[] { $"Grading failed: {ex.Message}" }
            };
        }
    }

    /// <inheritdoc />
    public async Task<HallucinationRiskAssessment> DetectHallucinationRisksAsync(
        string query,
        IEnumerable<DocumentChunk> documents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(documents);

        var documentList = documents.ToList();
        if (documentList.Count == 0)
        {
            return new HallucinationRiskAssessment
            {
                OverallRisk = 1.0,
                RiskLevel = HallucinationRiskLevel.VeryHigh,
                RiskFactors = new[] { new HallucinationRiskFactor
                {
                    Type = HallucinationRiskType.InsufficientEvidence,
                    Severity = 1.0,
                    Description = "No documents to verify against"
                }},
                AssessmentConfidence = 1.0
            };
        }

        var riskFactors = new List<HallucinationRiskFactor>();
        var highRiskDocIds = new List<string>();

        // 1. Check for contradictory information
        var contradictionFactor = await CheckForContradictionsAsync(documentList, cancellationToken);
        if (contradictionFactor != null)
        {
            riskFactors.Add(contradictionFactor);
        }

        // 2. Check for insufficient evidence
        var evidenceFactor = CheckForInsufficientEvidence(query, documentList);
        if (evidenceFactor != null)
        {
            riskFactors.Add(evidenceFactor);
        }

        // 3. Check for lack of specificity
        var specificityFactor = CheckForLackOfSpecificity(query, documentList);
        if (specificityFactor != null)
        {
            riskFactors.Add(specificityFactor);
        }

        // 4. Check for entity confusion
        var entityFactor = CheckForEntityConfusion(query, documentList);
        if (entityFactor != null)
        {
            riskFactors.Add(entityFactor);
            highRiskDocIds.AddRange(entityFactor.AffectedDocuments);
        }

        // 5. Check individual document risks
        foreach (var doc in documentList)
        {
            var docRisk = await CalculateDocumentRiskAsync(query, doc, cancellationToken);
            if (docRisk > _options.HighHallucinationRiskThreshold)
            {
                highRiskDocIds.Add(doc.Id.ToString());
            }
        }

        // Calculate overall risk
        var overallRisk = riskFactors.Count == 0
            ? 0.1
            : Math.Min(1.0, riskFactors.Average(f => f.Severity) + (riskFactors.Count * 0.05));

        var riskLevel = ClassifyRiskLevel(overallRisk);

        // Generate mitigation suggestions
        var suggestions = GenerateMitigationSuggestions(riskFactors, riskLevel);

        return new HallucinationRiskAssessment
        {
            OverallRisk = overallRisk,
            RiskLevel = riskLevel,
            RiskFactors = riskFactors,
            HighRiskDocumentIds = highRiskDocIds.Distinct().ToList(),
            MitigationSuggestions = suggestions,
            AssessmentConfidence = documentList.Count >= 3 ? 0.85 : 0.65
        };
    }

    /// <inheritdoc />
    public async Task<FactualGroundingResult> CheckFactualGroundingAsync(
        string query,
        IEnumerable<DocumentChunk> documents,
        IEnumerable<string>? claims = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(documents);

        var documentList = documents.ToList();
        if (documentList.Count == 0)
        {
            return new FactualGroundingResult
            {
                OverallScore = 0,
                IsSufficient = false,
                QueryCoverage = 0,
                EvidenceQuality = 0,
                SourceDiversity = 0,
                UngroundedAspects = new[] { "No documents available for grounding" },
                ImprovementSuggestions = new[] { "Retrieve more documents" }
            };
        }

        // Extract claims from query if not provided
        var claimList = claims?.ToList() ?? ExtractClaimsFromQuery(query);
        var groundedClaims = new List<GroundedClaim>();
        var ungroundedAspects = new List<string>();

        // Check grounding for each claim
        foreach (var claim in claimList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var groundingResult = await CheckClaimGroundingAsync(claim, documentList, cancellationToken);
            groundedClaims.Add(groundingResult);

            if (groundingResult.GroundingScore < _options.MinGroundingScore)
            {
                ungroundedAspects.Add(claim);
            }
        }

        // Calculate query coverage
        var queryCoverage = claimList.Count > 0
            ? (double)groundedClaims.Count(c => c.GroundingScore >= _options.MinGroundingScore) / claimList.Count
            : CalculateQueryCoverage(query, documentList);

        // Calculate evidence quality
        var evidenceQuality = groundedClaims.Count > 0
            ? groundedClaims.Average(c => c.GroundingScore)
            : await CalculateEvidenceQualityAsync(query, documentList, cancellationToken);

        // Calculate source diversity
        var sourceDiversity = CalculateSourceDiversity(documentList);

        // Overall grounding score
        var overallScore = (queryCoverage * 0.4) + (evidenceQuality * 0.4) + (sourceDiversity * 0.2);

        // Determine if sufficient
        var isSufficient = overallScore >= _options.MinFactualGroundingScore &&
                          queryCoverage >= _options.MinQueryCoverage;

        // Generate improvement suggestions
        var suggestions = GenerateGroundingImprovements(overallScore, queryCoverage, sourceDiversity, ungroundedAspects);

        return new FactualGroundingResult
        {
            OverallScore = overallScore,
            IsSufficient = isSufficient,
            QueryCoverage = queryCoverage,
            EvidenceQuality = evidenceQuality,
            SourceDiversity = sourceDiversity,
            GroundedClaims = groundedClaims,
            UngroundedAspects = ungroundedAspects,
            ImprovementSuggestions = suggestions
        };
    }

    /// <inheritdoc />
    public IEnumerable<GradedDocument> FilterByConfidence(
        IEnumerable<GradedDocument> gradedDocuments,
        double threshold = 0.5)
    {
        ArgumentNullException.ThrowIfNull(gradedDocuments);
        threshold = Math.Clamp(threshold, 0.0, 1.0);

        return gradedDocuments
            .Where(d => d.Grade.ConfidenceScore >= threshold)
            .OrderByDescending(d => d.Grade.ConfidenceScore);
    }

    /// <inheritdoc />
    public async Task<ClaimSupportResult> CalculateClaimSupportAsync(
        IEnumerable<string> claims,
        IEnumerable<DocumentChunk> documents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(documents);

        var claimList = claims.ToList();
        var documentList = documents.ToList();

        if (claimList.Count == 0 || documentList.Count == 0)
        {
            return new ClaimSupportResult
            {
                Claims = Array.Empty<ClaimSupport>(),
                OverallSupport = 0,
                FullySupportedCount = 0,
                PartiallySupportedCount = 0,
                UnsupportedCount = claimList.Count
            };
        }

        var claimSupports = new List<ClaimSupport>();

        foreach (var claim in claimList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var support = await CalculateSingleClaimSupportAsync(claim, documentList, cancellationToken);
            claimSupports.Add(support);
        }

        var fullSupported = claimSupports.Count(c => c.Level == SupportLevel.FullySupported);
        var partialSupported = claimSupports.Count(c => c.Level == SupportLevel.PartiallySupported);
        var unsupported = claimSupports.Count(c => c.Level == SupportLevel.NotSupported);

        var overallSupport = claimSupports.Count > 0
            ? claimSupports.Average(c => c.Score)
            : 0;

        return new ClaimSupportResult
        {
            Claims = claimSupports,
            OverallSupport = overallSupport,
            FullySupportedCount = fullSupported,
            PartiallySupportedCount = partialSupported,
            UnsupportedCount = unsupported
        };
    }

    /// <inheritdoc />
    public VerificationRecommendation GetRecommendation(RetrievalVerificationResult verificationResult)
    {
        ArgumentNullException.ThrowIfNull(verificationResult);

        var action = RecommendedAction.Proceed;
        var suggestions = new List<string>();
        var queryModifications = new List<string>();
        var shouldProceed = true;

        // Analyze verification result
        var relevantDocs = verificationResult.GradedDocuments
            .Count(d => d.Grade.Relevance == Interfaces.RelevanceGrade.Relevant);
        var totalDocs = verificationResult.GradedDocuments.Count;

        // Determine action based on status
        switch (verificationResult.Status)
        {
            case Interfaces.VerificationStatus.Passed:
                action = RecommendedAction.Proceed;
                shouldProceed = true;
                break;

            case Interfaces.VerificationStatus.PartiallyPassed:
                action = RecommendedAction.UseTopVerified;
                shouldProceed = true;
                suggestions.Add($"Consider using only the top {relevantDocs} verified documents");
                break;

            case Interfaces.VerificationStatus.Warning:
                action = verificationResult.OverallConfidence >= 0.5
                    ? RecommendedAction.UseTopVerified
                    : RecommendedAction.AddContext;
                shouldProceed = verificationResult.OverallConfidence >= 0.4;
                suggestions.Add("Results have quality warnings - review before using");

                if (verificationResult.HallucinationRisk?.RiskLevel >= HallucinationRiskLevel.Moderate)
                {
                    suggestions.Add("Hallucination risk detected - verify critical facts");
                }
                break;

            case Interfaces.VerificationStatus.Failed:
                action = relevantDocs > 0
                    ? RecommendedAction.RetryWithModifiedQuery
                    : RecommendedAction.ExpandSearch;
                shouldProceed = false;

                if (relevantDocs == 0)
                {
                    queryModifications.Add("Try broader search terms");
                    queryModifications.Add("Consider alternative phrasings");
                }
                else
                {
                    queryModifications.Add("Add more specific terms");
                }
                break;

            case Interfaces.VerificationStatus.Inconclusive:
                action = RecommendedAction.WarnUser;
                shouldProceed = verificationResult.OverallConfidence >= 0.3;
                suggestions.Add("Verification was inconclusive - use results with caution");
                break;
        }

        // Add issue-specific suggestions
        foreach (var issue in verificationResult.Issues)
        {
            if (!string.IsNullOrEmpty(issue.SuggestedResolution))
            {
                suggestions.Add(issue.SuggestedResolution);
            }
        }

        // Generate reasoning
        var reasoning = GenerateRecommendationReasoning(verificationResult, action);

        return new VerificationRecommendation
        {
            Action = action,
            Confidence = verificationResult.OverallConfidence,
            Reasoning = reasoning,
            Suggestions = suggestions.Distinct().ToList(),
            ShouldProceed = shouldProceed,
            SuggestedQueryModifications = queryModifications
        };
    }

}

/// <summary>
/// Options for the retrieval verification service.
/// </summary>
public partial class RetrievalVerificationServiceOptions
{
    /// <summary>
    /// Default grading criteria.
    /// </summary>
    public GradingCriteria DefaultCriteria { get; set; } = new();

    /// <summary>
    /// Whether to always check for hallucination.
    /// </summary>
    public bool AlwaysCheckHallucination { get; set; }

    /// <summary>
    /// Whether to use LLM for document grading.
    /// </summary>
    public bool UseLlmForGrading { get; set; }

    /// <summary>
    /// Minimum grounding score for claims.
    /// </summary>
    public double MinGroundingScore { get; set; } = 0.5;

    /// <summary>
    /// Minimum factual grounding score.
    /// </summary>
    public double MinFactualGroundingScore { get; set; } = 0.6;

    /// <summary>
    /// Minimum query coverage.
    /// </summary>
    public double MinQueryCoverage { get; set; } = 0.5;

    /// <summary>
    /// High hallucination risk threshold.
    /// </summary>
    public double HighHallucinationRiskThreshold { get; set; } = 0.6;
}
