using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// Corrective RAG (CRAG) service implementation.
/// Evaluates retrieved documents for relevance and performs corrective actions
/// based on the grading results following the CRAG paper methodology.
/// </summary>
public partial class CorrectiveRAGService : ICorrectiveRAGService
{
    private readonly IHybridSearchService _searchService;
    private readonly IEmbeddingService _embeddingService;
    private readonly IRetrievalVerificationService? _verificationService;
    private readonly ITextCompletionService? _completionService;
    private readonly CorrectiveRAGServiceOptions _options;
    private readonly ILogger<CorrectiveRAGService> _logger;

    public CorrectiveRAGService(
        IHybridSearchService searchService,
        IEmbeddingService embeddingService,
        IRetrievalVerificationService? verificationService,
        ITextCompletionService? completionService,
        IOptions<CorrectiveRAGServiceOptions> options,
        ILogger<CorrectiveRAGService> logger)
    {
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _verificationService = verificationService;
        _completionService = completionService;
        _options = options?.Value ?? new CorrectiveRAGServiceOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<CorrectiveRAGResult> RetrieveWithCorrectionAsync(
        string query,
        CorrectiveRAGOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var opts = options ?? new CorrectiveRAGOptions();
        var stopwatch = Stopwatch.StartNew();
        var correctionSteps = new List<CorrectionStep>();
        var stepNumber = 0;

        try
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                LogCorrectiveRAG6(_logger, query);

            // Step 1: Initial Retrieval
            var retrievalStopwatch = Stopwatch.StartNew();
            var initialDocuments = await PerformInitialRetrievalAsync(query, opts, cancellationToken);
            retrievalStopwatch.Stop();

            correctionSteps.Add(new CorrectionStep
            {
                StepNumber = ++stepNumber,
                Type = CorrectionStepType.InitialRetrieval,
                Description = $"Retrieved {initialDocuments.Count} initial documents",
                InputCount = 0,
                OutputCount = initialDocuments.Count,
                Duration = retrievalStopwatch.Elapsed,
                IsSuccessful = initialDocuments.Count > 0
            });

            if (initialDocuments.Count == 0)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                    LogCorrectiveRAG5(_logger, query);
                return CreateEmptyResult(stopwatch.Elapsed, correctionSteps);
            }

            // Step 2: Grade Documents
            var gradingStopwatch = Stopwatch.StartNew();
            var gradingResult = await GradeDocumentsAsync(query, initialDocuments, cancellationToken);
            gradingStopwatch.Stop();

            correctionSteps.Add(new CorrectionStep
            {
                StepNumber = ++stepNumber,
                Type = CorrectionStepType.Grading,
                Description = $"Graded {gradingResult.GradedDocuments.Count} documents: {gradingResult.CorrectCount} correct, {gradingResult.AmbiguousCount} ambiguous, {gradingResult.IncorrectCount} incorrect",
                InputCount = initialDocuments.Count,
                OutputCount = gradingResult.GradedDocuments.Count,
                Duration = gradingStopwatch.Elapsed,
                IsSuccessful = true
            });

            // Step 3: Determine Correction Action
            var action = DetermineAction(gradingResult);
            if (_logger.IsEnabled(LogLevel.Debug))
                LogCorrectiveRAG4(_logger, action);

            var correctedDocuments = new List<CorrectedDocument>();
            bool usedAlternativeRetrieval = false;
            bool appliedKnowledgeRefinement = false;

            // Step 4: Execute Correction Based on Assessment
            switch (gradingResult.Assessment)
            {
                case OverallAssessment.Correct:
                    // Use correct documents directly
                    correctedDocuments.AddRange(
                        gradingResult.GradedDocuments
                            .Where(d => d.Grade == DocumentRelevanceGrade.Correct)
                            .Select(d => CreateCorrectedDocument(d, DocumentSource.OriginalRetrieval)));
                    break;

                case OverallAssessment.Ambiguous:
                    // Use correct documents and supplement with alternative retrieval
                    correctedDocuments.AddRange(
                        gradingResult.GradedDocuments
                            .Where(d => d.Grade != DocumentRelevanceGrade.Incorrect)
                            .Select(d => CreateCorrectedDocument(d, DocumentSource.OriginalRetrieval)));

                    if (opts.EnableQueryTransformation)
                    {
                        var altStopwatch = Stopwatch.StartNew();
                        var alternativeResult = await PerformAlternativeRetrievalAsync(
                            query, initialDocuments, cancellationToken);
                        altStopwatch.Stop();

                        if (alternativeResult.IsSuccessful && alternativeResult.Documents.Count > 0)
                        {
                            usedAlternativeRetrieval = true;
                            var altDocuments = alternativeResult.Documents.Take(opts.MaxAlternativeDocuments);

                            foreach (var doc in altDocuments)
                            {
                                if (!correctedDocuments.Any(cd => cd.Chunk.Id == doc.Id))
                                {
                                    correctedDocuments.Add(new CorrectedDocument
                                    {
                                        Chunk = doc,
                                        Grade = DocumentRelevanceGrade.Ambiguous,
                                        RelevanceScore = 0.5,
                                        Source = DocumentSource.AlternativeRetrieval,
                                        InclusionReason = "Added from alternative retrieval to supplement ambiguous results"
                                    });
                                }
                            }

                            correctionSteps.Add(new CorrectionStep
                            {
                                StepNumber = ++stepNumber,
                                Type = CorrectionStepType.AlternativeRetrieval,
                                Description = $"Supplemented with {alternativeResult.Documents.Count} alternative documents using {alternativeResult.Strategy}",
                                InputCount = gradingResult.AmbiguousCount,
                                OutputCount = alternativeResult.Documents.Count,
                                Duration = altStopwatch.Elapsed,
                                IsSuccessful = true
                            });
                        }
                    }
                    break;

                case OverallAssessment.Incorrect:
                    // Discard original documents and use alternative retrieval
                    var replaceStopwatch = Stopwatch.StartNew();
                    var replacementResult = await PerformAlternativeRetrievalAsync(
                        query, initialDocuments, cancellationToken);
                    replaceStopwatch.Stop();

                    if (replacementResult.IsSuccessful && replacementResult.Documents.Count > 0)
                    {
                        usedAlternativeRetrieval = true;
                        correctedDocuments.AddRange(
                            replacementResult.Documents.Select(doc => new CorrectedDocument
                            {
                                Chunk = doc,
                                Grade = DocumentRelevanceGrade.Ambiguous,
                                RelevanceScore = 0.6,
                                Source = DocumentSource.AlternativeRetrieval,
                                InclusionReason = "Replacement document from alternative retrieval"
                            }));

                        correctionSteps.Add(new CorrectionStep
                        {
                            StepNumber = ++stepNumber,
                            Type = CorrectionStepType.AlternativeRetrieval,
                            Description = $"Replaced incorrect documents with {replacementResult.Documents.Count} alternatives",
                            InputCount = gradingResult.IncorrectCount,
                            OutputCount = replacementResult.Documents.Count,
                            Duration = replaceStopwatch.Elapsed,
                            IsSuccessful = true
                        });
                    }
                    else
                    {
                        // Fallback: use best available from original even if not ideal
                        correctedDocuments.AddRange(
                            gradingResult.GradedDocuments
                                .OrderByDescending(d => d.RelevanceScore)
                                .Take(opts.MaxAlternativeDocuments)
                                .Select(d => CreateCorrectedDocument(d, DocumentSource.OriginalRetrieval)));
                    }
                    break;
            }

            // Step 5: Knowledge Refinement (if enabled)
            if (opts.EnableKnowledgeRefinement && correctedDocuments.Count > 0)
            {
                var refineStopwatch = Stopwatch.StartNew();
                var refinementResult = await RefineKnowledgeAsync(
                    query,
                    correctedDocuments.Select(d => d.Chunk),
                    cancellationToken);
                refineStopwatch.Stop();

                if (refinementResult.IsSuccessful)
                {
                    appliedKnowledgeRefinement = true;

                    // Update corrected documents with refined content
                    foreach (var refinedDoc in refinementResult.RefinedDocuments)
                    {
                        var correctedDoc = correctedDocuments.FirstOrDefault(
                            d => d.Chunk.Id == refinedDoc.DocumentId);
                        if (correctedDoc != null)
                        {
                            var index = correctedDocuments.IndexOf(correctedDoc);
                            correctedDocuments[index] = new CorrectedDocument
                            {
                                Chunk = correctedDoc.Chunk,
                                Grade = correctedDoc.Grade,
                                RelevanceScore = correctedDoc.RelevanceScore,
                                Source = correctedDoc.Source,
                                RefinedContent = refinedDoc.RefinedContent,
                                KeyConcepts = refinedDoc.KeyConcepts,
                                InclusionReason = correctedDoc.InclusionReason
                            };
                        }
                    }

                    correctionSteps.Add(new CorrectionStep
                    {
                        StepNumber = ++stepNumber,
                        Type = CorrectionStepType.KnowledgeRefinement,
                        Description = $"Refined knowledge from {refinementResult.RefinedDocuments.Count} documents",
                        InputCount = correctedDocuments.Count,
                        OutputCount = refinementResult.RefinedDocuments.Count,
                        Duration = refineStopwatch.Elapsed,
                        IsSuccessful = true
                    });
                }
            }

            stopwatch.Stop();

            // Calculate confidence score
            var confidenceScore = CalculateConfidenceScore(correctedDocuments, gradingResult, action);

            return new CorrectiveRAGResult
            {
                Documents = correctedDocuments.AsReadOnly(),
                ActionTaken = action,
                GradingResult = gradingResult,
                UsedAlternativeRetrieval = usedAlternativeRetrieval,
                AppliedKnowledgeRefinement = appliedKnowledgeRefinement,
                ProcessingTime = stopwatch.Elapsed,
                ConfidenceScore = confidenceScore,
                IsSuccessful = true,
                CorrectionSteps = correctionSteps.AsReadOnly(),
                Metadata = new Dictionary<string, object>
                {
                    ["query"] = query,
                    ["initialDocumentCount"] = initialDocuments.Count,
                    ["finalDocumentCount"] = correctedDocuments.Count,
                    ["overallAssessment"] = gradingResult.Assessment.ToString()
                }
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                LogCorrectiveRAG3(_logger, ex, query);
            stopwatch.Stop();

            return new CorrectiveRAGResult
            {
                Documents = Array.Empty<CorrectedDocument>(),
                ActionTaken = CorrectionAction.None,
                GradingResult = new DocumentGradingResult(),
                ProcessingTime = stopwatch.Elapsed,
                ConfidenceScore = 0,
                IsSuccessful = false,
                ErrorMessage = ex.Message,
                CorrectionSteps = correctionSteps.AsReadOnly()
            };
        }
    }

    /// <inheritdoc />
    public async Task<DocumentGradingResult> GradeDocumentsAsync(
        string query,
        IEnumerable<DocumentChunk> documents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(documents);
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        var documentList = documents.ToList();
        var gradedDocuments = new List<GradedDocumentInfo>();

        if (documentList.Count == 0)
        {
            return new DocumentGradingResult
            {
                GradedDocuments = Array.Empty<GradedDocumentInfo>(),
                Assessment = OverallAssessment.Incorrect,
                AverageRelevanceScore = 0,
                ProcessingTime = TimeSpan.Zero
            };
        }

        // Generate query embedding for semantic similarity
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
        var queryTerms = ExtractQueryTerms(query);

        foreach (var doc in documentList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Calculate semantic similarity
            double semanticSimilarity = 0;
            if (doc.Embedding != null && doc.Embedding.Length > 0)
            {
                semanticSimilarity = CalculateCosineSimilarity(queryEmbedding, doc.Embedding);
            }
            else
            {
                // Generate embedding if not available
                var docEmbedding = await _embeddingService.GenerateEmbeddingAsync(
                    doc.Content ?? string.Empty, cancellationToken);
                semanticSimilarity = CalculateCosineSimilarity(queryEmbedding, docEmbedding);
            }

            // Calculate keyword match score
            var keywordScore = CalculateKeywordMatchScore(queryTerms, doc.Content ?? string.Empty);
            var matchingTerms = FindMatchingTerms(queryTerms, doc.Content ?? string.Empty);

            // Combined relevance score
            var relevanceScore = (_options.SemanticWeight * semanticSimilarity) +
                               (_options.KeywordWeight * keywordScore);

            // Determine grade
            var grade = DetermineGrade(relevanceScore);

            gradedDocuments.Add(new GradedDocumentInfo
            {
                Document = doc,
                Grade = grade,
                RelevanceScore = relevanceScore,
                SemanticSimilarity = semanticSimilarity,
                KeywordMatchScore = keywordScore,
                MatchingTerms = matchingTerms,
                Explanation = GenerateGradeExplanation(grade, relevanceScore, semanticSimilarity, keywordScore)
            });
        }

        stopwatch.Stop();

        // Calculate counts
        var correctCount = gradedDocuments.Count(d => d.Grade == DocumentRelevanceGrade.Correct);
        var ambiguousCount = gradedDocuments.Count(d => d.Grade == DocumentRelevanceGrade.Ambiguous);
        var incorrectCount = gradedDocuments.Count(d => d.Grade == DocumentRelevanceGrade.Incorrect);
        var avgScore = gradedDocuments.Average(d => d.RelevanceScore);

        // Determine overall assessment
        var assessment = DetermineOverallAssessment(correctCount, ambiguousCount, incorrectCount, documentList.Count);

        return new DocumentGradingResult
        {
            GradedDocuments = gradedDocuments.OrderByDescending(d => d.RelevanceScore).ToList().AsReadOnly(),
            Assessment = assessment,
            AverageRelevanceScore = avgScore,
            CorrectCount = correctCount,
            AmbiguousCount = ambiguousCount,
            IncorrectCount = incorrectCount,
            ProcessingTime = stopwatch.Elapsed
        };
    }

    /// <inheritdoc />
    public async Task<KnowledgeRefinementResult> RefineKnowledgeAsync(
        string query,
        IEnumerable<DocumentChunk> documents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(documents);
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        var documentList = documents.ToList();
        var refinedDocuments = new List<RefinedDocumentKnowledge>();

        if (documentList.Count == 0)
        {
            return new KnowledgeRefinementResult
            {
                RefinedDocuments = Array.Empty<RefinedDocumentKnowledge>(),
                IsSuccessful = true,
                ProcessingTime = TimeSpan.Zero
            };
        }

        var queryTerms = ExtractQueryTerms(query);
        var allKeyFacts = new List<string>();

        foreach (var doc in documentList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Extract relevant sentences
            var relevantContent = ExtractRelevantContent(doc.Content ?? string.Empty, queryTerms);
            var keyConcepts = ExtractKeyConcepts(doc.Content ?? string.Empty, queryTerms);
            var facts = ExtractFacts(doc.Content ?? string.Empty, query);

            refinedDocuments.Add(new RefinedDocumentKnowledge
            {
                DocumentId = doc.Id,
                RefinedContent = relevantContent,
                KeyConcepts = keyConcepts,
                ExtractedFacts = facts,
                Confidence = CalculateRefinementConfidence(relevantContent, doc.Content ?? string.Empty)
            });

            allKeyFacts.AddRange(facts);
        }

        stopwatch.Stop();

        // Generate summary if LLM is available
        string? summary = null;
        if (_completionService != null && _options.UseLlmForRefinement)
        {
            summary = await GenerateSummaryAsync(query, refinedDocuments, cancellationToken);
        }

        return new KnowledgeRefinementResult
        {
            RefinedDocuments = refinedDocuments.AsReadOnly(),
            Summary = summary,
            KeyFacts = allKeyFacts.Distinct().Take(10).ToList().AsReadOnly(),
            ProcessingTime = stopwatch.Elapsed,
            IsSuccessful = true
        };
    }

    /// <inheritdoc />
    public async Task<AlternativeRetrievalResult> PerformAlternativeRetrievalAsync(
        string query,
        IEnumerable<DocumentChunk> originalDocuments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(originalDocuments);
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Strategy 1: Query Transformation
            var transformedQuery = TransformQuery(query, originalDocuments);

            // Perform search with transformed query
            var searchOptions = new HybridSearchOptions
            {
                MaxResults = 10,
                FusionMethod = FusionMethod.RelativeScoreFusion
            };

            var results = await _searchService.SearchAsync(
                transformedQuery,
                searchOptions,
                cancellationToken);

            stopwatch.Stop();

            var documents = results
                .Select(r => r.Chunk)
                .Where(c => !originalDocuments.Any(od => od.Id == c.Id))
                .ToList();

            return new AlternativeRetrievalResult
            {
                Documents = documents.AsReadOnly(),
                Strategy = AlternativeRetrievalStrategy.QueryTransformation,
                TransformedQuery = transformedQuery,
                ProcessingTime = stopwatch.Elapsed,
                IsSuccessful = true
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            if (_logger.IsEnabled(LogLevel.Debug))
                LogCorrectiveRAG2(_logger, ex, query);

            return new AlternativeRetrievalResult
            {
                Documents = Array.Empty<DocumentChunk>(),
                Strategy = AlternativeRetrievalStrategy.QueryTransformation,
                ProcessingTime = stopwatch.Elapsed,
                IsSuccessful = false,
                ErrorMessage = ex.Message
            };
        }
    }

    #region Private Helper Methods

    private async Task<List<DocumentChunk>> PerformInitialRetrievalAsync(
        string query,
        CorrectiveRAGOptions options,
        CancellationToken cancellationToken)
    {
        var searchOptions = new HybridSearchOptions
        {
            MaxResults = options.MaxInitialDocuments,
            FusionMethod = FusionMethod.RelativeScoreFusion
        };

        var results = await _searchService.SearchAsync(query, searchOptions, cancellationToken);
        return results.Select(r => r.Chunk).ToList();
    }

    private static CorrectionAction DetermineAction(DocumentGradingResult gradingResult)
    {
        return gradingResult.Assessment switch
        {
            OverallAssessment.Correct => CorrectionAction.None,
            OverallAssessment.Ambiguous => CorrectionAction.Supplemented,
            OverallAssessment.Incorrect => CorrectionAction.Replaced,
            _ => CorrectionAction.None
        };
    }

    private static CorrectedDocument CreateCorrectedDocument(GradedDocumentInfo gradedDoc, DocumentSource source)
    {
        return new CorrectedDocument
        {
            Chunk = gradedDoc.Document,
            Grade = gradedDoc.Grade,
            RelevanceScore = gradedDoc.RelevanceScore,
            Source = source,
            KeyConcepts = gradedDoc.MatchingTerms,
            InclusionReason = gradedDoc.Explanation
        };
    }

    private DocumentRelevanceGrade DetermineGrade(double relevanceScore)
    {
        if (relevanceScore >= _options.CorrectThreshold)
            return DocumentRelevanceGrade.Correct;
        if (relevanceScore >= _options.AmbiguousThreshold)
            return DocumentRelevanceGrade.Ambiguous;
        return DocumentRelevanceGrade.Incorrect;
    }

    private static OverallAssessment DetermineOverallAssessment(
        int correctCount, int ambiguousCount, int incorrectCount, int total)
    {
        if (total == 0) return OverallAssessment.Incorrect;

        var correctRatio = (double)correctCount / total;
        var incorrectRatio = (double)incorrectCount / total;

        if (correctRatio >= 0.5) return OverallAssessment.Correct;
        if (incorrectRatio >= 0.5) return OverallAssessment.Incorrect;
        return OverallAssessment.Ambiguous;
    }

    private static List<string> ExtractQueryTerms(string query)
    {
        // Simple tokenization - split by whitespace and common punctuation
        var terms = Regex.Split(query.ToLowerInvariant(), @"[\s\.,;:!?\-\(\)\[\]{}]+")
            .Where(t => t.Length > 2)
            .Distinct()
            .ToList();
        return terms;
    }

    private static double CalculateKeywordMatchScore(List<string> queryTerms, string content)
    {
        if (queryTerms.Count == 0 || string.IsNullOrEmpty(content))
            return 0;

        var contentLower = content.ToLowerInvariant();
        var matchCount = queryTerms.Count(term => contentLower.Contains(term));
        return (double)matchCount / queryTerms.Count;
    }

    private static List<string> FindMatchingTerms(List<string> queryTerms, string content)
    {
        var contentLower = content.ToLowerInvariant();
        return queryTerms.Where(term => contentLower.Contains(term)).ToList();
    }

    private static double CalculateCosineSimilarity(float[] v1, float[] v2)
    {
        if (v1.Length != v2.Length || v1.Length == 0)
            return 0;

        double dotProduct = 0;
        double mag1 = 0;
        double mag2 = 0;

        for (int i = 0; i < v1.Length; i++)
        {
            dotProduct += v1[i] * v2[i];
            mag1 += v1[i] * v1[i];
            mag2 += v2[i] * v2[i];
        }

        var magnitude = Math.Sqrt(mag1) * Math.Sqrt(mag2);
        return magnitude > 0 ? dotProduct / magnitude : 0;
    }

    private static string GenerateGradeExplanation(
        DocumentRelevanceGrade grade, double relevanceScore, double semanticSimilarity, double keywordScore)
    {
        var explanation = grade switch
        {
            DocumentRelevanceGrade.Correct => "Document is highly relevant",
            DocumentRelevanceGrade.Ambiguous => "Document is partially relevant",
            DocumentRelevanceGrade.Incorrect => "Document is not relevant",
            _ => "Document relevance unknown"
        };

        return $"{explanation} (score: {relevanceScore:F2}, semantic: {semanticSimilarity:F2}, keyword: {keywordScore:F2})";
    }

    private static string TransformQuery(string query, IEnumerable<DocumentChunk> originalDocuments)
    {
        // Simple query transformation: expand with related terms
        var terms = ExtractQueryTerms(query);

        // Extract common terms from documents that might be relevant
        var docTerms = originalDocuments
            .SelectMany(d => ExtractQueryTerms(d.Content ?? string.Empty))
            .GroupBy(t => t)
            .OrderByDescending(g => g.Count())
            .Take(3)
            .Select(g => g.Key)
            .Where(t => !terms.Contains(t));

        var expandedQuery = string.Join(" ", terms.Concat(docTerms).Distinct());
        return expandedQuery;
    }

    private static string ExtractRelevantContent(string content, List<string> queryTerms)
    {
        if (string.IsNullOrEmpty(content) || queryTerms.Count == 0)
            return content;

        // Split into sentences
        var sentences = Regex.Split(content, @"(?<=[.!?])\s+");

        // Score each sentence
        var relevantSentences = sentences
            .Select(s => new
            {
                Sentence = s,
                Score = queryTerms.Count(t => s.Contains(t, StringComparison.OrdinalIgnoreCase))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(5)
            .Select(x => x.Sentence);

        return string.Join(" ", relevantSentences);
    }

    private static List<string> ExtractKeyConcepts(string content, List<string> queryTerms)
    {
        // Extract noun phrases or key terms that appear near query terms
        var contentLower = content.ToLowerInvariant();
        var concepts = new HashSet<string>();

        foreach (var term in queryTerms)
        {
            var index = contentLower.IndexOf(term, StringComparison.Ordinal);
            if (index >= 0)
            {
                // Extract surrounding words
                var start = Math.Max(0, index - 50);
                var length = Math.Min(100, content.Length - start);
                var context = content.Substring(start, length);

                var words = Regex.Split(context, @"\s+")
                    .Where(w => w.Length > 3 && !queryTerms.Contains(w.ToLowerInvariant()))
                    .Take(3);

                foreach (var word in words)
                {
                    concepts.Add(word);
                }
            }
        }

        return concepts.Take(10).ToList();
    }

    private static List<string> ExtractFacts(string content, string query)
    {
        // Simple fact extraction: sentences that contain query-related terms
        var sentences = Regex.Split(content, @"(?<=[.!?])\s+")
            .Where(s => s.Length > 20 && s.Length < 200)
            .Take(3)
            .ToList();

        return sentences;
    }

    private static double CalculateRefinementConfidence(string refinedContent, string originalContent)
    {
        if (string.IsNullOrEmpty(originalContent))
            return 0;
        if (string.IsNullOrEmpty(refinedContent))
            return 0;

        var ratio = (double)refinedContent.Length / originalContent.Length;
        // Higher confidence if we extracted a meaningful portion
        return ratio > 0.1 && ratio < 1.0 ? 0.7 + (ratio * 0.3) : 0.5;
    }

    private async Task<string?> GenerateSummaryAsync(
        string query,
        List<RefinedDocumentKnowledge> refinedDocuments,
        CancellationToken cancellationToken)
    {
        if (_completionService == null)
            return null;

        try
        {
            var combinedContent = string.Join("\n\n", refinedDocuments.Select(d => d.RefinedContent));
            var prompt = $"Based on the following content, provide a brief summary relevant to the query '{query}':\n\n{combinedContent}";

            return await _completionService.CompleteAsync(prompt, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            LogCorrectiveRAG1(_logger, ex);
            return null;
        }
    }

    private static double CalculateConfidenceScore(
        List<CorrectedDocument> documents,
        DocumentGradingResult gradingResult,
        CorrectionAction action)
    {
        if (documents.Count == 0)
            return 0;

        var avgRelevance = documents.Average(d => d.RelevanceScore);
        var actionPenalty = action switch
        {
            CorrectionAction.None => 0,
            CorrectionAction.Supplemented => 0.1,
            CorrectionAction.Replaced => 0.2,
            CorrectionAction.Mixed => 0.15,
            _ => 0
        };

        return Math.Max(0, Math.Min(1, avgRelevance - actionPenalty));
    }

    private static CorrectiveRAGResult CreateEmptyResult(TimeSpan processingTime, List<CorrectionStep> steps)
    {
        return new CorrectiveRAGResult
        {
            Documents = Array.Empty<CorrectedDocument>(),
            ActionTaken = CorrectionAction.None,
            GradingResult = new DocumentGradingResult
            {
                Assessment = OverallAssessment.Incorrect
            },
            ProcessingTime = processingTime,
            ConfidenceScore = 0,
            IsSuccessful = false,
            ErrorMessage = "No documents retrieved",
            CorrectionSteps = steps.AsReadOnly()
        };
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Starting Corrective RAG for query: {Query}")]
    private static partial void LogCorrectiveRAG6(ILogger logger, string query);
    [LoggerMessage(Level = LogLevel.Warning, Message = "No initial documents retrieved for query: {Query}")]
    private static partial void LogCorrectiveRAG5(ILogger logger, string query);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Correction action determined: {Action}")]
    private static partial void LogCorrectiveRAG4(ILogger logger, CorrectionAction action);
    [LoggerMessage(Level = LogLevel.Error, Message = "Error during corrective RAG for query: {Query}")]
    private static partial void LogCorrectiveRAG3(ILogger logger, Exception exception, string query);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Alternative retrieval failed for query: {Query}")]
    private static partial void LogCorrectiveRAG2(ILogger logger, Exception exception, string query);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to generate summary")]
    private static partial void LogCorrectiveRAG1(ILogger logger, Exception exception);

    #endregion
}

/// <summary>
/// Configuration options for CorrectiveRAGService.
/// </summary>
public partial class CorrectiveRAGServiceOptions
{
    /// <summary>
    /// Threshold for considering a document as "correct" (relevant).
    /// </summary>
    public double CorrectThreshold { get; set; } = 0.7;

    /// <summary>
    /// Threshold for considering a document as "ambiguous".
    /// </summary>
    public double AmbiguousThreshold { get; set; } = 0.4;

    /// <summary>
    /// Weight for semantic similarity in relevance calculation.
    /// </summary>
    public double SemanticWeight { get; set; } = 0.6;

    /// <summary>
    /// Weight for keyword matching in relevance calculation.
    /// </summary>
    public double KeywordWeight { get; set; } = 0.4;

    /// <summary>
    /// Whether to use LLM for knowledge refinement.
    /// </summary>
    public bool UseLlmForRefinement { get; set; } = true;

    /// <summary>
    /// Maximum retries for alternative retrieval.
    /// </summary>
    public int MaxRetries { get; set; } = 2;
}
