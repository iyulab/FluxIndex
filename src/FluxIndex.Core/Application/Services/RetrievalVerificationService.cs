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

    #region Private Helper Methods

    private async Task<IReadOnlyList<GradedDocument>> GradeDocumentsAsync(
        string query,
        List<DocumentChunk> documents,
        VerificationOptions options,
        CancellationToken cancellationToken)
    {
        var gradedDocuments = new List<GradedDocument>();
        var rank = 1;

        if (options.EnableParallelVerification && documents.Count > 1)
        {
            var tasks = documents.Select(async (doc, index) =>
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(options.PerDocumentTimeout);

                try
                {
                    var grade = await GradeDocumentAsync(query, doc, cts.Token);
                    return (Index: index, Document: doc, Grade: grade, Success: true);
                }
                catch (OperationCanceledException)
                {
                    return (Index: index, Document: doc, Grade: new DocumentGrade
                    {
                        Relevance = Interfaces.RelevanceGrade.Unknown,
                        Issues = ["Verification timeout"]
                    }, Success: false);
                }
            });

            var results = await Task.WhenAll(tasks);

            foreach (var result in results.OrderBy(r => r.Index))
            {
                gradedDocuments.Add(new GradedDocument
                {
                    Document = result.Document,
                    Grade = result.Grade,
                    OriginalRank = rank++
                });
            }
        }
        else
        {
            foreach (var doc in documents)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var grade = await GradeDocumentAsync(query, doc, cancellationToken);
                gradedDocuments.Add(new GradedDocument
                {
                    Document = doc,
                    Grade = grade,
                    OriginalRank = rank++
                });
            }
        }

        // Assign verified ranks based on confidence
        var sortedByConfidence = gradedDocuments
            .OrderByDescending(d => d.Grade.ConfidenceScore)
            .ToList();

        for (int i = 0; i < sortedByConfidence.Count; i++)
        {
            // Create new instance with verified rank (GradedDocument is immutable-like)
            var original = sortedByConfidence[i];
            var index = gradedDocuments.IndexOf(original);
            gradedDocuments[index] = new GradedDocument
            {
                Document = original.Document,
                Grade = original.Grade,
                OriginalRank = original.OriginalRank,
                VerifiedRank = i + 1
            };
        }

        return gradedDocuments;
    }

    private async Task<float[]> GetOrCreateEmbeddingAsync(string text, CancellationToken cancellationToken)
    {
        var key = ComputeHash(text);
        if (_embeddingCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var embedding = await _embeddingService.GenerateEmbeddingAsync(text, cancellationToken);
        _embeddingCache.TryAdd(key, embedding);
        return embedding;
    }

    private async Task<float[]> GetDocumentEmbeddingAsync(DocumentChunk document, CancellationToken cancellationToken)
    {
        // If document has embedding, use it
        if (document.Embedding?.Length > 0)
        {
            return document.Embedding;
        }

        // Otherwise generate
        return await GetOrCreateEmbeddingAsync(document.Content, cancellationToken);
    }

    private static string ComputeHash(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }

    private static double CalculateCosineSimilarity(float[] a, float[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return 0;

        double dotProduct = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denominator > 0 ? dotProduct / denominator : 0;
    }

    private static double CalculateKeywordMatch(string query, string content)
    {
        var queryTokens = Tokenize(query);
        var contentTokens = Tokenize(content);

        if (queryTokens.Count == 0) return 0;

        var overlap = queryTokens.Intersect(contentTokens).Count();
        return (double)overlap / queryTokens.Count;
    }

    private static HashSet<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new HashSet<string>();

        return text.ToLowerInvariant()
            .Split(TokenizeSeparators,
                StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2)
            .ToHashSet();
    }

    private static double CalculateEntityOverlap(string query, string content)
    {
        // Simple entity extraction using capitalized words and patterns
        var queryEntities = ExtractSimpleEntities(query);
        var contentEntities = ExtractSimpleEntities(content);

        if (queryEntities.Count == 0) return 0.5; // Neutral if no entities

        var overlap = queryEntities.Intersect(contentEntities, StringComparer.OrdinalIgnoreCase).Count();
        return (double)overlap / queryEntities.Count;
    }

    private static HashSet<string> ExtractSimpleEntities(string text)
    {
        var entities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Extract capitalized words (likely entities)
        var capitalizedPattern = new Regex(@"\b[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*\b");
        foreach (Match match in capitalizedPattern.Matches(text))
        {
            entities.Add(match.Value);
        }

        // Extract numbers with units
        var numberPattern = new Regex(@"\d+(?:\.\d+)?(?:\s*(?:km|m|kg|g|%|°C|°F|USD|EUR|\$|€))?");
        foreach (Match match in numberPattern.Matches(text))
        {
            entities.Add(match.Value);
        }

        return entities;
    }

    private static double CalculateContextualFit(string query, string content)
    {
        // Check for contextual indicators
        var score = 0.5; // Base score

        // Check for question-answer pattern
        if (IsQuestion(query))
        {
            if (ContainsAnswerIndicators(content))
            {
                score += 0.2;
            }
        }

        // Check for topic consistency
        var queryBigrams = GetBigrams(query);
        var contentBigrams = GetBigrams(content);
        var bigramOverlap = queryBigrams.Intersect(contentBigrams).Count();
        if (queryBigrams.Count > 0)
        {
            score += 0.3 * ((double)bigramOverlap / queryBigrams.Count);
        }

        return Math.Min(1.0, score);
    }

    private static bool IsQuestion(string text)
    {
        var questionWords = new[] { "what", "where", "when", "why", "how", "who", "which", "is", "are", "can", "does" };
        var lowerText = text.ToLowerInvariant();
        return text.Contains('?') || questionWords.Any(w => lowerText.StartsWith(w + " ", StringComparison.Ordinal));
    }

    private static bool ContainsAnswerIndicators(string content)
    {
        var indicators = new[] { "is", "are", "was", "were", "because", "therefore", "means", "refers to" };
        var lowerContent = content.ToLowerInvariant();
        return indicators.Any(i => lowerContent.Contains(i));
    }

    private static HashSet<string> GetBigrams(string text)
    {
        var tokens = Tokenize(text).ToList();
        var bigrams = new HashSet<string>();

        for (int i = 0; i < tokens.Count - 1; i++)
        {
            bigrams.Add($"{tokens[i]} {tokens[i + 1]}");
        }

        return bigrams;
    }

    private static Interfaces.RelevanceGrade DetermineRelevanceGrade(double confidenceScore)
    {
        return confidenceScore switch
        {
            >= 0.7 => Interfaces.RelevanceGrade.Relevant,
            >= 0.4 => Interfaces.RelevanceGrade.PartiallyRelevant,
            >= 0.2 => Interfaces.RelevanceGrade.Ambiguous,
            _ => Interfaces.RelevanceGrade.NotRelevant
        };
    }

    private static List<string> DetectDocumentIssues(
        string query, DocumentChunk document,
        double semanticSimilarity, double keywordMatch)
    {
        var issues = new List<string>();

        if (semanticSimilarity < 0.3)
            issues.Add("Low semantic relevance");

        if (keywordMatch < 0.2)
            issues.Add("Few matching keywords");

        if (semanticSimilarity > 0.7 && keywordMatch < 0.2)
            issues.Add("High semantic but low keyword match - potential topic drift");

        if (document.Content.Length < 50)
            issues.Add("Very short content");

        if (document.Content.Length > 5000)
            issues.Add("Very long content - may lack specificity");

        return issues;
    }

    private static double CalculateDocumentHallucinationRisk(DocumentChunk document, double semanticSimilarity)
    {
        var risk = 0.0;

        // Lower semantic similarity increases risk
        risk += (1 - semanticSimilarity) * 0.4;

        // Very short documents increase risk
        if (document.Content.Length < 100)
            risk += 0.2;

        // Check for uncertainty language
        var uncertaintyWords = new[] { "maybe", "possibly", "might", "could be", "uncertain", "unclear" };
        var lowerContent = document.Content.ToLowerInvariant();
        if (uncertaintyWords.Any(w => lowerContent.Contains(w)))
            risk += 0.1;

        return Math.Min(1.0, risk);
    }

    private async Task<string?> GetLlmGradingExplanationAsync(
        string query, DocumentChunk document,
        CancellationToken cancellationToken)
    {
        if (_completionService == null) return null;

        try
        {
            var prompt = $"""
                Rate the relevance of this document to the query.
                Query: {query}
                Document excerpt: {document.Content.Substring(0, Math.Min(500, document.Content.Length))}

                Provide a brief explanation (1-2 sentences) of why this document is or isn't relevant.
                """;

            return await _completionService.CompleteAsync(
                prompt, new Flux.Abstractions.TextCompletionOptions { MaxTokens = 100, Temperature = 0.3f }, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<HallucinationRiskFactor?> CheckForContradictionsAsync(
        List<DocumentChunk> documents,
        CancellationToken cancellationToken)
    {
        if (documents.Count < 2) return null;

        var contradictions = new List<string>();

        // Simple pairwise contradiction check
        for (int i = 0; i < Math.Min(documents.Count, 5); i++)
        {
            for (int j = i + 1; j < Math.Min(documents.Count, 5); j++)
            {
                var content1 = documents[i].Content.ToLowerInvariant();
                var content2 = documents[j].Content.ToLowerInvariant();

                // Check for negation patterns indicating contradiction
                if (HasPotentialContradiction(content1, content2))
                {
                    contradictions.Add($"{documents[i].Id} vs {documents[j].Id}");
                }
            }
        }

        if (contradictions.Count == 0) return null;

        return new HallucinationRiskFactor
        {
            Type = HallucinationRiskType.ContradictoryInformation,
            Severity = Math.Min(1.0, 0.3 + (contradictions.Count * 0.2)),
            Description = $"Found {contradictions.Count} potential contradiction(s) between documents",
            AffectedDocuments = contradictions
        };
    }

    private static bool HasPotentialContradiction(string content1, string content2)
    {
        var negationPatterns = new[] { "not ", "never ", "no ", "don't", "doesn't", "won't", "isn't", "aren't", "false", "incorrect" };

        foreach (var pattern in negationPatterns)
        {
            var hasInFirst = content1.Contains(pattern);
            var hasInSecond = content2.Contains(pattern);

            if (hasInFirst != hasInSecond)
            {
                // One has negation, one doesn't - potential contradiction
                // Check if they're discussing similar topics (naive check via shared words)
                var words1 = Tokenize(content1);
                var words2 = Tokenize(content2);
                var sharedWords = words1.Intersect(words2).Count();

                if (sharedWords > 5) // Significant overlap
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static HallucinationRiskFactor? CheckForInsufficientEvidence(string query, List<DocumentChunk> documents)
    {
        var queryComplexity = EstimateQueryComplexity(query);
        var expectedMinDocs = queryComplexity switch
        {
            > 0.7 => 5,
            > 0.4 => 3,
            _ => 2
        };

        if (documents.Count < expectedMinDocs)
        {
            return new HallucinationRiskFactor
            {
                Type = HallucinationRiskType.InsufficientEvidence,
                Severity = 0.3 + (0.1 * (expectedMinDocs - documents.Count)),
                Description = $"Only {documents.Count} documents found, expected at least {expectedMinDocs} for query complexity",
                AffectedDocuments = documents.Select(d => d.Id.ToString()).ToList()
            };
        }

        return null;
    }

    private static double EstimateQueryComplexity(string query)
    {
        var complexity = 0.0;

        // Length factor
        if (query.Length > 100) complexity += 0.2;
        if (query.Length > 200) complexity += 0.2;

        // Question complexity
        if (query.Contains("why") || query.Contains("how")) complexity += 0.2;
        if (query.Contains(" and ") || query.Contains(" or ")) complexity += 0.1;

        // Multiple entities
        var entities = ExtractSimpleEntities(query);
        complexity += Math.Min(0.3, entities.Count * 0.1);

        return Math.Min(1.0, complexity);
    }

    private static HallucinationRiskFactor? CheckForLackOfSpecificity(string query, List<DocumentChunk> documents)
    {
        var queryEntities = ExtractSimpleEntities(query);
        if (queryEntities.Count == 0) return null;

        var docsWithAllEntities = documents.Count(doc =>
        {
            var docEntities = ExtractSimpleEntities(doc.Content);
            return queryEntities.All(qe => docEntities.Contains(qe));
        });

        if (docsWithAllEntities == 0)
        {
            return new HallucinationRiskFactor
            {
                Type = HallucinationRiskType.LackOfSpecificity,
                Severity = 0.5,
                Description = "No document contains all query entities",
                AffectedDocuments = documents.Select(d => d.Id.ToString()).ToList()
            };
        }

        return null;
    }

    private static HallucinationRiskFactor? CheckForEntityConfusion(string query, List<DocumentChunk> documents)
    {
        var queryEntities = ExtractSimpleEntities(query);
        if (queryEntities.Count == 0) return null;

        var confusedDocs = new List<string>();

        foreach (var doc in documents)
        {
            var docEntities = ExtractSimpleEntities(doc.Content);

            // Check for similar but different entities (potential confusion)
            foreach (var queryEntity in queryEntities)
            {
                var similarEntities = docEntities.Where(de =>
                    !de.Equals(queryEntity, StringComparison.OrdinalIgnoreCase) &&
                    IsSimilarEntity(queryEntity, de));

                if (similarEntities.Any())
                {
                    confusedDocs.Add(doc.Id.ToString());
                    break;
                }
            }
        }

        if (confusedDocs.Count == 0) return null;

        return new HallucinationRiskFactor
        {
            Type = HallucinationRiskType.EntityConfusion,
            Severity = 0.4,
            Description = $"{confusedDocs.Count} document(s) may contain similar but different entities",
            AffectedDocuments = confusedDocs
        };
    }

    private static bool IsSimilarEntity(string entity1, string entity2)
    {
        // Simple similarity check
        var e1 = entity1.ToLowerInvariant();
        var e2 = entity2.ToLowerInvariant();

        // Check for substring relationship
        if (e1.Contains(e2) || e2.Contains(e1)) return true;

        // Check for high character overlap (Jaccard-like)
        var chars1 = new HashSet<char>(e1);
        var chars2 = new HashSet<char>(e2);
        var intersection = chars1.Intersect(chars2).Count();
        var union = chars1.Union(chars2).Count();

        return union > 0 && (double)intersection / union > 0.7;
    }

    private async Task<double> CalculateDocumentRiskAsync(
        string query, DocumentChunk document,
        CancellationToken cancellationToken)
    {
        var queryEmbedding = await GetOrCreateEmbeddingAsync(query, cancellationToken);
        var docEmbedding = await GetDocumentEmbeddingAsync(document, cancellationToken);
        var similarity = CalculateCosineSimilarity(queryEmbedding, docEmbedding);

        return CalculateDocumentHallucinationRisk(document, similarity);
    }

    private static HallucinationRiskLevel ClassifyRiskLevel(double risk)
    {
        return risk switch
        {
            <= 0.15 => HallucinationRiskLevel.VeryLow,
            <= 0.3 => HallucinationRiskLevel.Low,
            <= 0.5 => HallucinationRiskLevel.Moderate,
            <= 0.7 => HallucinationRiskLevel.High,
            _ => HallucinationRiskLevel.VeryHigh
        };
    }

    private static List<string> GenerateMitigationSuggestions(
        List<HallucinationRiskFactor> factors,
        HallucinationRiskLevel level)
    {
        var suggestions = new List<string>();

        if (level >= HallucinationRiskLevel.Moderate)
        {
            suggestions.Add("Cross-verify critical facts with multiple sources");
        }

        foreach (var factor in factors)
        {
            suggestions.Add(factor.Type switch
            {
                HallucinationRiskType.ContradictoryInformation =>
                    "Resolve contradictions by checking source recency and authority",
                HallucinationRiskType.InsufficientEvidence =>
                    "Retrieve additional documents to strengthen evidence",
                HallucinationRiskType.LackOfSpecificity =>
                    "Add more specific search terms to find targeted content",
                HallucinationRiskType.EntityConfusion =>
                    "Verify entity names and disambiguate similar terms",
                HallucinationRiskType.OutdatedInformation =>
                    "Check for more recent sources",
                HallucinationRiskType.TemporalInconsistency =>
                    "Verify dates and temporal references",
                HallucinationRiskType.UnreliableSource =>
                    "Prefer authoritative and verified sources",
                HallucinationRiskType.MissingContext =>
                    "Expand search to include related context",
                _ => "Review results carefully before using"
            });
        }

        return suggestions.Distinct().ToList();
    }

    private static List<string> ExtractClaimsFromQuery(string query)
    {
        // Simple claim extraction - split on conjunctions
        var claims = new List<string>();

        var parts = query.Split(ClaimSplitSeparators,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            if (part.Length > 10)
            {
                claims.Add(part);
            }
        }

        if (claims.Count == 0)
        {
            claims.Add(query);
        }

        return claims;
    }

    private async Task<GroundedClaim> CheckClaimGroundingAsync(
        string claim,
        List<DocumentChunk> documents,
        CancellationToken cancellationToken)
    {
        var claimEmbedding = await GetOrCreateEmbeddingAsync(claim, cancellationToken);
        var supportingDocs = new List<string>();
        var evidenceExcerpts = new List<string>();
        var maxSimilarity = 0.0;

        foreach (var doc in documents)
        {
            var docEmbedding = await GetDocumentEmbeddingAsync(doc, cancellationToken);
            var similarity = CalculateCosineSimilarity(claimEmbedding, docEmbedding);

            if (similarity > maxSimilarity)
            {
                maxSimilarity = similarity;
            }

            if (similarity >= _options.MinGroundingScore)
            {
                supportingDocs.Add(doc.Id.ToString());

                // Extract relevant excerpt
                var excerpt = ExtractRelevantExcerpt(claim, doc.Content);
                if (!string.IsNullOrEmpty(excerpt))
                {
                    evidenceExcerpts.Add(excerpt);
                }
            }
        }

        return new GroundedClaim
        {
            Claim = claim,
            GroundingScore = maxSimilarity,
            SupportingDocuments = supportingDocs,
            EvidenceExcerpts = evidenceExcerpts.Take(3).ToList(),
            Confidence = supportingDocs.Count > 1 ? 0.9 : (supportingDocs.Count == 1 ? 0.7 : 0.3)
        };
    }

    private static string? ExtractRelevantExcerpt(string claim, string content)
    {
        var claimWords = Tokenize(claim).Take(5).ToList();
        if (claimWords.Count == 0) return null;

        var sentences = content.Split(SentenceSplitSeparators,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var sentence in sentences)
        {
            var sentenceWords = Tokenize(sentence);
            var overlap = claimWords.Intersect(sentenceWords).Count();

            if (overlap >= 2)
            {
                return sentence.Length > 200 ? string.Concat(sentence.AsSpan(0, 200), "...") : sentence;
            }
        }

        return null;
    }

    private static double CalculateQueryCoverage(string query, List<DocumentChunk> documents)
    {
        var queryWords = Tokenize(query);
        if (queryWords.Count == 0) return 0;

        var coveredWords = new HashSet<string>();

        foreach (var doc in documents)
        {
            var docWords = Tokenize(doc.Content);
            foreach (var qw in queryWords)
            {
                if (docWords.Contains(qw))
                {
                    coveredWords.Add(qw);
                }
            }
        }

        return (double)coveredWords.Count / queryWords.Count;
    }

    private async Task<double> CalculateEvidenceQualityAsync(
        string query,
        List<DocumentChunk> documents,
        CancellationToken cancellationToken)
    {
        var queryEmbedding = await GetOrCreateEmbeddingAsync(query, cancellationToken);
        var similarities = new List<double>();

        foreach (var doc in documents)
        {
            var docEmbedding = await GetDocumentEmbeddingAsync(doc, cancellationToken);
            var similarity = CalculateCosineSimilarity(queryEmbedding, docEmbedding);
            similarities.Add(similarity);
        }

        return similarities.Count > 0 ? similarities.Average() : 0;
    }

    private static double CalculateSourceDiversity(List<DocumentChunk> documents)
    {
        if (documents.Count <= 1) return 0;

        // Calculate diversity based on unique sources
        var uniqueDocIds = documents.Select(d => d.DocumentId).Distinct().Count();
        var diversityFromSources = Math.Min(1.0, uniqueDocIds / 5.0);

        // Calculate content diversity
        var contentHashes = documents
            .Select(d => ComputeHash(d.Content.Substring(0, Math.Min(100, d.Content.Length))))
            .Distinct()
            .Count();
        var diversityFromContent = (double)contentHashes / documents.Count;

        return (diversityFromSources + diversityFromContent) / 2;
    }

    private static List<string> GenerateGroundingImprovements(
        double overallScore, double coverage, double diversity,
        List<string> ungroundedAspects)
    {
        var suggestions = new List<string>();

        if (coverage < 0.5)
        {
            suggestions.Add("Add more specific search terms to improve query coverage");
        }

        if (diversity < 0.3)
        {
            suggestions.Add("Retrieve from more diverse sources");
        }

        if (ungroundedAspects.Count > 0)
        {
            suggestions.Add($"Search specifically for: {string.Join(", ", ungroundedAspects.Take(3))}");
        }

        if (overallScore < 0.5)
        {
            suggestions.Add("Consider rephrasing the query for better matches");
        }

        return suggestions;
    }

    private async Task<ClaimSupport> CalculateSingleClaimSupportAsync(
        string claim,
        List<DocumentChunk> documents,
        CancellationToken cancellationToken)
    {
        var claimEmbedding = await GetOrCreateEmbeddingAsync(claim, cancellationToken);
        var documentSupports = new List<DocumentSupport>();
        var maxScore = 0.0;

        foreach (var doc in documents)
        {
            var docEmbedding = await GetDocumentEmbeddingAsync(doc, cancellationToken);
            var similarity = CalculateCosineSimilarity(claimEmbedding, docEmbedding);

            if (similarity > maxScore)
            {
                maxScore = similarity;
            }

            if (similarity >= 0.3)
            {
                var supportType = DetermineSupportType(claim, doc.Content, similarity);
                documentSupports.Add(new DocumentSupport
                {
                    DocumentId = doc.Id.ToString(),
                    SupportScore = similarity,
                    RelevantExcerpt = ExtractRelevantExcerpt(claim, doc.Content),
                    Type = supportType
                });
            }
        }

        var level = maxScore switch
        {
            >= 0.7 => SupportLevel.FullySupported,
            >= 0.4 => SupportLevel.PartiallySupported,
            _ => SupportLevel.NotSupported
        };

        // Check for contradictions
        if (documentSupports.Any(ds => ds.Type == SupportType.Contradiction))
        {
            level = SupportLevel.Contradicted;
        }

        return new ClaimSupport
        {
            Claim = claim,
            Level = level,
            Score = maxScore,
            SupportingDocuments = documentSupports.OrderByDescending(ds => ds.SupportScore).ToList()
        };
    }

    private static SupportType DetermineSupportType(string claim, string content, double similarity)
    {
        // Check for contradiction indicators
        var claimLower = claim.ToLowerInvariant();
        var contentLower = content.ToLowerInvariant();

        var negationWords = new[] { "not", "never", "no ", "doesn't", "don't", "isn't", "aren't", "false" };

        var claimHasNegation = negationWords.Any(n => claimLower.Contains(n));
        var contentHasNegation = negationWords.Any(n => contentLower.Contains(n));

        if (claimHasNegation != contentHasNegation && similarity > 0.5)
        {
            return SupportType.Contradiction;
        }

        return similarity switch
        {
            >= 0.7 => SupportType.Direct,
            >= 0.5 => SupportType.Indirect,
            _ => SupportType.Implied
        };
    }

    private static List<VerificationIssue> IdentifyVerificationIssues(
        IReadOnlyList<GradedDocument> gradedDocs,
        HallucinationRiskAssessment? hallucinationRisk,
        FactualGroundingResult? factualGrounding,
        VerificationOptions options)
    {
        var issues = new List<VerificationIssue>();

        // Check relevance issues
        var lowRelevanceDocs = gradedDocs
            .Where(d => d.Grade.ConfidenceScore < options.RelevanceThreshold)
            .ToList();

        if (lowRelevanceDocs.Count > gradedDocs.Count / 2)
        {
            issues.Add(new VerificationIssue
            {
                Type = VerificationIssueType.LowRelevance,
                Severity = 0.6,
                Description = $"{lowRelevanceDocs.Count} of {gradedDocs.Count} documents have low relevance",
                AffectedDocuments = lowRelevanceDocs.Select(d => d.Document.Id.ToString()).ToList(),
                SuggestedResolution = "Refine search query for more relevant results"
            });
        }

        // Check hallucination issues
        if (hallucinationRisk?.RiskLevel >= HallucinationRiskLevel.Moderate)
        {
            issues.Add(new VerificationIssue
            {
                Type = VerificationIssueType.HighHallucinationRisk,
                Severity = hallucinationRisk.OverallRisk,
                Description = $"Hallucination risk: {hallucinationRisk.RiskLevel}",
                AffectedDocuments = hallucinationRisk.HighRiskDocumentIds.ToList(),
                SuggestedResolution = "Cross-verify with additional authoritative sources"
            });
        }

        // Check grounding issues
        if (factualGrounding != null && !factualGrounding.IsSufficient)
        {
            issues.Add(new VerificationIssue
            {
                Type = VerificationIssueType.InsufficientGrounding,
                Severity = 1 - factualGrounding.OverallScore,
                Description = $"Factual grounding score: {factualGrounding.OverallScore:F2}",
                SuggestedResolution = factualGrounding.ImprovementSuggestions.Count > 0 ? factualGrounding.ImprovementSuggestions[0] : null
            });
        }

        // Check for insufficient documents
        if (gradedDocs.Count(d => d.Grade.Relevance == Interfaces.RelevanceGrade.Relevant) < 2)
        {
            issues.Add(new VerificationIssue
            {
                Type = VerificationIssueType.InsufficientDocuments,
                Severity = 0.5,
                Description = "Too few relevant documents for reliable verification",
                SuggestedResolution = "Expand search scope to retrieve more documents"
            });
        }

        return issues;
    }

    private static double CalculateOverallConfidence(IReadOnlyList<GradedDocument> gradedDocs)
    {
        if (gradedDocs.Count == 0) return 0;

        var relevantDocs = gradedDocs
            .Where(d => d.Grade.Relevance == Interfaces.RelevanceGrade.Relevant ||
                       d.Grade.Relevance == Interfaces.RelevanceGrade.PartiallyRelevant)
            .ToList();

        if (relevantDocs.Count == 0) return 0;

        // Weighted average favoring higher scores
        var weightedSum = relevantDocs.Sum(d => d.Grade.ConfidenceScore * d.Grade.ConfidenceScore);
        var weightSum = relevantDocs.Sum(d => d.Grade.ConfidenceScore);

        var avgScore = weightSum > 0 ? weightedSum / weightSum : 0;

        // Factor in coverage
        var coverage = (double)relevantDocs.Count / gradedDocs.Count;

        return (avgScore * 0.7) + (coverage * 0.3);
    }

    private static Interfaces.VerificationStatus DetermineVerificationStatus(
        IReadOnlyList<GradedDocument> gradedDocs,
        List<VerificationIssue> issues,
        VerificationOptions options)
    {
        var relevantCount = gradedDocs.Count(d =>
            d.Grade.Relevance == Interfaces.RelevanceGrade.Relevant);
        var partialCount = gradedDocs.Count(d =>
            d.Grade.Relevance == Interfaces.RelevanceGrade.PartiallyRelevant);
        var totalRelevant = relevantCount + partialCount;

        var criticalIssues = issues.Count(i => i.Severity >= 0.7);
        var avgConfidence = gradedDocs.Count > 0
            ? gradedDocs.Average(d => d.Grade.ConfidenceScore)
            : 0;

        // Strict mode checks
        if (options.StrictMode && issues.Count > 0)
        {
            return Interfaces.VerificationStatus.Failed;
        }

        if (criticalIssues > 0)
        {
            return totalRelevant > 0
                ? Interfaces.VerificationStatus.Warning
                : Interfaces.VerificationStatus.Failed;
        }

        if (relevantCount >= 3 && avgConfidence >= options.RelevanceThreshold)
        {
            return issues.Count > 0
                ? Interfaces.VerificationStatus.PartiallyPassed
                : Interfaces.VerificationStatus.Passed;
        }

        if (totalRelevant >= 2)
        {
            return Interfaces.VerificationStatus.PartiallyPassed;
        }

        if (totalRelevant >= 1)
        {
            return Interfaces.VerificationStatus.Warning;
        }

        return Interfaces.VerificationStatus.Failed;
    }

    private static VerificationStatistics GenerateStatistics(
        IReadOnlyList<GradedDocument> gradedDocs,
        HallucinationRiskAssessment? hallucinationRisk,
        bool llmUsed)
    {
        return new VerificationStatistics
        {
            TotalDocuments = gradedDocs.Count,
            RelevantCount = gradedDocs.Count(d => d.Grade.Relevance == Interfaces.RelevanceGrade.Relevant),
            PartiallyRelevantCount = gradedDocs.Count(d => d.Grade.Relevance == Interfaces.RelevanceGrade.PartiallyRelevant),
            NotRelevantCount = gradedDocs.Count(d => d.Grade.Relevance == Interfaces.RelevanceGrade.NotRelevant),
            AmbiguousCount = gradedDocs.Count(d => d.Grade.Relevance == Interfaces.RelevanceGrade.Ambiguous),
            AverageConfidence = gradedDocs.Count > 0 ? gradedDocs.Average(d => d.Grade.ConfidenceScore) : 0,
            AverageHallucinationRisk = hallucinationRisk?.OverallRisk ?? 0,
            LlmVerificationUsed = llmUsed
        };
    }

    private static string GenerateRecommendationReasoning(
        RetrievalVerificationResult result,
        RecommendedAction action)
    {
        var reasons = new List<string>();

        reasons.Add($"Verification status: {result.Status}");
        reasons.Add($"Overall confidence: {result.OverallConfidence:F2}");
        reasons.Add($"Relevant documents: {result.Statistics.RelevantCount}/{result.Statistics.TotalDocuments}");

        if (result.HallucinationRisk != null)
        {
            reasons.Add($"Hallucination risk: {result.HallucinationRisk.RiskLevel}");
        }

        if (result.Issues.Count > 0)
        {
            reasons.Add($"Issues found: {result.Issues.Count}");
        }

        reasons.Add($"Recommended action: {action}");

        return string.Join(". ", reasons);
    }

    private static RetrievalVerificationResult CreateEmptyResult(string query, string message)
    {
        return new RetrievalVerificationResult
        {
            Query = query,
            Status = Interfaces.VerificationStatus.Failed,
            OverallConfidence = 0,
            GradedDocuments = Array.Empty<GradedDocument>(),
            Issues = new[]
            {
                new VerificationIssue
                {
                    Type = VerificationIssueType.InsufficientDocuments,
                    Severity = 1.0,
                    Description = message
                }
            },
            Statistics = new VerificationStatistics()
        };
    }

    private static RetrievalVerificationResult CreateFailedResult(string query, string error, TimeSpan processingTime)
    {
        return new RetrievalVerificationResult
        {
            Query = query,
            Status = Interfaces.VerificationStatus.Inconclusive,
            OverallConfidence = 0,
            GradedDocuments = Array.Empty<GradedDocument>(),
            Issues = new[]
            {
                new VerificationIssue
                {
                    Type = VerificationIssueType.VerificationTimeout,
                    Severity = 1.0,
                    Description = $"Verification failed: {error}"
                }
            },
            Statistics = new VerificationStatistics(),
            ProcessingTime = processingTime
        };
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Verifying {Count} documents for query: {Query}")]
    private static partial void LogRetrievalVerification4(ILogger logger, int count, string query);
    [LoggerMessage(Level = LogLevel.Information, Message = "Verification complete: {Status}, confidence: {Confidence:F3}, {RelevantCount}/{TotalCount} relevant")]
    private static partial void LogRetrievalVerification3(ILogger logger, VerificationStatus status, double confidence, double relevantCount, int totalCount);
    [LoggerMessage(Level = LogLevel.Error, Message = "Verification failed for query: {Query}")]
    private static partial void LogRetrievalVerification2(ILogger logger, Exception exception, string query);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to grade document {DocumentId}")]
    private static partial void LogRetrievalVerification1(ILogger logger, Exception exception, string documentId);

    #endregion
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
