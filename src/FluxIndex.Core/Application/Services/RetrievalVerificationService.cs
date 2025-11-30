using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// Retrieval Verification Service for validating search results.
/// Implements CRAG (Corrective RAG) patterns for hallucination detection,
/// relevance verification, and multi-evidence validation.
/// </summary>
public class RetrievalVerificationService : IRetrievalVerificationService
{
    private readonly IEmbeddingService _embeddingService;
    private readonly ITextCompletionService? _completionService;
    private readonly RetrievalVerificationOptions _options;
    private readonly ILogger<RetrievalVerificationService> _logger;

    public RetrievalVerificationService(
        IEmbeddingService embeddingService,
        ITextCompletionService? completionService,
        IOptions<RetrievalVerificationOptions> options,
        ILogger<RetrievalVerificationService> logger)
    {
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _completionService = completionService;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RetrievalVerificationResult> VerifyRetrievalAsync(
        string query,
        IReadOnlyList<RetrievedChunk> retrievedChunks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(retrievedChunks);

        if (retrievedChunks.Count == 0)
        {
            return new RetrievalVerificationResult
            {
                IsValid = false,
                OverallConfidence = 0,
                ValidationStatus = ValidationStatus.Insufficient,
                Message = "No chunks to verify"
            };
        }

        _logger.LogDebug("Verifying {Count} retrieved chunks for query: {Query}",
            retrievedChunks.Count, query);

        var startTime = DateTime.UtcNow;

        // Step 1: Calculate query embedding
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);

        // Step 2: Verify each chunk
        var verifiedChunks = new List<VerifiedChunk>();
        foreach (var chunk in retrievedChunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var verification = await VerifyChunkAsync(query, queryEmbedding, chunk, cancellationToken);
            verifiedChunks.Add(verification);
        }

        // Step 3: Multi-evidence validation
        var multiEvidenceResult = await ValidateMultiEvidenceAsync(
            query, verifiedChunks, cancellationToken);

        // Step 4: Determine overall validity
        var validChunks = verifiedChunks.Where(c => c.IsRelevant && c.RelevanceScore >= _options.MinRelevanceThreshold).ToList();
        var overallConfidence = CalculateOverallConfidence(verifiedChunks);
        var status = DetermineValidationStatus(verifiedChunks, overallConfidence);

        var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

        _logger.LogInformation(
            "Verification complete: {ValidCount}/{TotalCount} valid chunks, confidence: {Confidence:F3}, status: {Status}",
            validChunks.Count, retrievedChunks.Count, overallConfidence, status);

        return new RetrievalVerificationResult
        {
            IsValid = status == ValidationStatus.Validated,
            OverallConfidence = overallConfidence,
            ValidationStatus = status,
            VerifiedChunks = verifiedChunks.AsReadOnly(),
            MultiEvidenceValidation = multiEvidenceResult,
            RecommendedAction = DetermineRecommendedAction(status, verifiedChunks),
            ExecutionTimeMs = elapsedMs
        };
    }

    /// <inheritdoc />
    public async Task<HallucinationCheckResult> CheckHallucinationAsync(
        string generatedContent,
        IReadOnlyList<RetrievedChunk> sourceChunks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(generatedContent);
        ArgumentNullException.ThrowIfNull(sourceChunks);

        if (sourceChunks.Count == 0)
        {
            return new HallucinationCheckResult
            {
                HasHallucination = true,
                Confidence = 1.0,
                HallucinationScore = 1.0,
                Reason = "No source chunks to verify against"
            };
        }

        _logger.LogDebug("Checking hallucination for content against {Count} source chunks",
            sourceChunks.Count);

        // Method 1: Embedding-based verification
        var embeddingResult = await CheckHallucinationByEmbeddingAsync(
            generatedContent, sourceChunks, cancellationToken);

        // Method 2: LLM-based verification (if available)
        var llmResult = _completionService != null
            ? await CheckHallucinationByLLMAsync(generatedContent, sourceChunks, cancellationToken)
            : null;

        // Combine results
        var hallucinationScore = llmResult != null
            ? (embeddingResult.Score * 0.4 + llmResult.Value.Score * 0.6)
            : embeddingResult.Score;

        var hasHallucination = hallucinationScore >= _options.HallucinationThreshold;
        var flaggedSpans = CombineHallucinatedSpans(embeddingResult.Spans, llmResult?.Spans);

        return new HallucinationCheckResult
        {
            HasHallucination = hasHallucination,
            HallucinationScore = hallucinationScore,
            Confidence = embeddingResult.Confidence,
            HallucinatedSpans = flaggedSpans,
            Reason = hasHallucination
                ? "Generated content contains information not supported by source chunks"
                : "Content appears grounded in source chunks",
            VerificationMethod = llmResult != null ? "hybrid" : "embedding"
        };
    }

    /// <inheritdoc />
    public async Task<FactualConsistencyResult> CheckFactualConsistencyAsync(
        IReadOnlyList<RetrievedChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        if (chunks.Count < 2)
        {
            return new FactualConsistencyResult
            {
                IsConsistent = true,
                ConsistencyScore = 1.0,
                Message = "Insufficient chunks for consistency check"
            };
        }

        _logger.LogDebug("Checking factual consistency across {Count} chunks", chunks.Count);

        var contradictions = new List<Contradiction>();

        // Pairwise comparison for contradictions
        for (int i = 0; i < chunks.Count; i++)
        {
            for (int j = i + 1; j < chunks.Count; j++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var contradiction = await DetectContradictionAsync(
                    chunks[i], chunks[j], cancellationToken);

                if (contradiction != null)
                {
                    contradictions.Add(contradiction);
                }
            }
        }

        var consistencyScore = contradictions.Count == 0
            ? 1.0
            : Math.Max(0, 1.0 - (contradictions.Count * 0.2));

        return new FactualConsistencyResult
        {
            IsConsistent = contradictions.Count == 0,
            ConsistencyScore = consistencyScore,
            Contradictions = contradictions.AsReadOnly(),
            Message = contradictions.Count == 0
                ? "No contradictions detected"
                : $"Found {contradictions.Count} potential contradiction(s)"
        };
    }

    /// <inheritdoc />
    public async Task<SourceAttributionResult> VerifySourceAttributionAsync(
        string claim,
        IReadOnlyList<RetrievedChunk> potentialSources,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(potentialSources);

        if (potentialSources.Count == 0)
        {
            return new SourceAttributionResult
            {
                IsAttributable = false,
                AttributionScore = 0,
                Message = "No potential sources provided"
            };
        }

        _logger.LogDebug("Verifying source attribution for claim against {Count} sources",
            potentialSources.Count);

        // Generate claim embedding
        var claimEmbedding = await _embeddingService.GenerateEmbeddingAsync(claim, cancellationToken);

        var sourceMatches = new List<SourceMatch>();

        foreach (var source in potentialSources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var similarity = CalculateCosineSimilarity(claimEmbedding, source.Embedding.Values);
            var entailmentScore = await CalculateEntailmentScoreAsync(claim, source.Content, cancellationToken);

            if (similarity >= _options.MinAttributionSimilarity ||
                entailmentScore >= _options.MinEntailmentScore)
            {
                sourceMatches.Add(new SourceMatch
                {
                    ChunkId = source.ChunkId,
                    Content = source.Content,
                    SimilarityScore = similarity,
                    EntailmentScore = entailmentScore,
                    IsStrongSupport = entailmentScore >= 0.8
                });
            }
        }

        var bestMatch = sourceMatches.OrderByDescending(m => m.EntailmentScore).FirstOrDefault();
        var isAttributable = bestMatch != null && bestMatch.EntailmentScore >= _options.MinEntailmentScore;

        return new SourceAttributionResult
        {
            IsAttributable = isAttributable,
            AttributionScore = bestMatch?.EntailmentScore ?? 0,
            SupportingSources = sourceMatches.AsReadOnly(),
            BestMatchingSource = bestMatch,
            Message = isAttributable
                ? $"Claim is supported by {sourceMatches.Count} source(s)"
                : "Claim could not be attributed to provided sources"
        };
    }

    #region Private Methods

    private async Task<VerifiedChunk> VerifyChunkAsync(
        string query,
        float[] queryEmbedding,
        RetrievedChunk chunk,
        CancellationToken cancellationToken)
    {
        // Calculate semantic similarity
        var similarity = CalculateCosineSimilarity(queryEmbedding, chunk.Embedding.Values);

        // Calculate lexical overlap (simple keyword matching)
        var lexicalScore = CalculateLexicalOverlap(query, chunk.Content);

        // Combined relevance score
        var relevanceScore = (similarity * 0.7) + (lexicalScore * 0.3);

        // Determine if relevant
        var isRelevant = relevanceScore >= _options.MinRelevanceThreshold;

        // Grade relevance (CRAG-style)
        var grade = GradeRelevance(relevanceScore);

        return new VerifiedChunk
        {
            ChunkId = chunk.ChunkId,
            Content = chunk.Content,
            OriginalScore = chunk.Score,
            RelevanceScore = relevanceScore,
            SemanticSimilarity = similarity,
            LexicalOverlap = lexicalScore,
            IsRelevant = isRelevant,
            RelevanceGrade = grade,
            VerificationNotes = GenerateVerificationNotes(relevanceScore, similarity, lexicalScore)
        };
    }

    private async Task<MultiEvidenceResult> ValidateMultiEvidenceAsync(
        string query,
        IReadOnlyList<VerifiedChunk> verifiedChunks,
        CancellationToken cancellationToken)
    {
        var relevantChunks = verifiedChunks
            .Where(c => c.IsRelevant)
            .OrderByDescending(c => c.RelevanceScore)
            .ToList();

        if (relevantChunks.Count == 0)
        {
            return new MultiEvidenceResult
            {
                HasSufficientEvidence = false,
                EvidenceCount = 0,
                AgreementScore = 0,
                Message = "No relevant evidence found"
            };
        }

        // Calculate agreement between top chunks
        var agreementScore = await CalculateEvidenceAgreementAsync(
            relevantChunks.Take(_options.MaxEvidenceChunks).ToList(),
            cancellationToken);

        var hasSufficient = relevantChunks.Count >= _options.MinEvidenceChunks &&
                           agreementScore >= _options.MinAgreementScore;

        return new MultiEvidenceResult
        {
            HasSufficientEvidence = hasSufficient,
            EvidenceCount = relevantChunks.Count,
            AgreementScore = agreementScore,
            TopEvidenceChunks = relevantChunks.Take(5).Select(c => c.ChunkId).ToList().AsReadOnly(),
            Message = hasSufficient
                ? $"Found {relevantChunks.Count} pieces of corroborating evidence"
                : "Insufficient corroborating evidence"
        };
    }

    private async Task<double> CalculateEvidenceAgreementAsync(
        IReadOnlyList<VerifiedChunk> chunks,
        CancellationToken cancellationToken)
    {
        if (chunks.Count <= 1)
            return 1.0;

        var similarities = new List<double>();

        for (int i = 0; i < chunks.Count; i++)
        {
            for (int j = i + 1; j < chunks.Count; j++)
            {
                var chunkI = chunks[i];
                var chunkJ = chunks[j];

                // Calculate content similarity (simplified - using embedding comparison)
                var embeddingI = await _embeddingService.GenerateEmbeddingAsync(
                    chunkI.Content, cancellationToken);
                var embeddingJ = await _embeddingService.GenerateEmbeddingAsync(
                    chunkJ.Content, cancellationToken);

                var similarity = CalculateCosineSimilarity(embeddingI, embeddingJ);
                similarities.Add(similarity);
            }
        }

        // Return average pairwise similarity as agreement score
        return similarities.Count > 0 ? similarities.Average() : 0;
    }

    private async Task<(double Score, double Confidence, List<HallucinatedSpan> Spans)> CheckHallucinationByEmbeddingAsync(
        string generatedContent,
        IReadOnlyList<RetrievedChunk> sourceChunks,
        CancellationToken cancellationToken)
    {
        // Split generated content into sentences/segments
        var segments = SplitIntoSegments(generatedContent);
        var hallucinatedSpans = new List<HallucinatedSpan>();
        var supportedSegments = 0;

        foreach (var segment in segments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var segmentEmbedding = await _embeddingService.GenerateEmbeddingAsync(
                segment.Text, cancellationToken);

            // Find best matching source
            var maxSimilarity = sourceChunks.Max(c =>
                CalculateCosineSimilarity(segmentEmbedding, c.Embedding.Values));

            if (maxSimilarity >= _options.MinSupportSimilarity)
            {
                supportedSegments++;
            }
            else
            {
                hallucinatedSpans.Add(new HallucinatedSpan
                {
                    Text = segment.Text,
                    StartIndex = segment.StartIndex,
                    EndIndex = segment.EndIndex,
                    Confidence = 1 - maxSimilarity,
                    Reason = "No supporting source found"
                });
            }
        }

        var score = segments.Count > 0
            ? 1.0 - ((double)supportedSegments / segments.Count)
            : 0;

        var confidence = segments.Count > 3 ? 0.8 : 0.6;

        return (score, confidence, hallucinatedSpans);
    }

    private async Task<(double Score, List<HallucinatedSpan> Spans)?> CheckHallucinationByLLMAsync(
        string generatedContent,
        IReadOnlyList<RetrievedChunk> sourceChunks,
        CancellationToken cancellationToken)
    {
        if (_completionService == null)
            return null;

        try
        {
            var sourcesText = string.Join("\n---\n",
                sourceChunks.Take(5).Select(c => c.Content.Substring(0, Math.Min(500, c.Content.Length))));

            var prompt = $$"""
                Analyze if the following generated content is fully supported by the source documents.

                SOURCE DOCUMENTS:
                {{sourcesText}}

                GENERATED CONTENT:
                {{generatedContent}}

                Rate the hallucination level from 0.0 (fully supported) to 1.0 (completely fabricated).
                Identify any specific claims not supported by sources.

                Respond in JSON format:
                {"score": 0.0, "unsupported_claims": ["claim1", "claim2"]}
                """;

            var response = await _completionService.GenerateJsonCompletionAsync(
                prompt, 300, cancellationToken);

            // Parse response (simplified)
            var score = ParseHallucinationScore(response);
            var spans = ParseUnsupportedClaims(response, generatedContent);

            return (score, spans);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM hallucination check failed, relying on embedding-based check");
            return null;
        }
    }

    private async Task<Contradiction?> DetectContradictionAsync(
        RetrievedChunk chunk1,
        RetrievedChunk chunk2,
        CancellationToken cancellationToken)
    {
        // Simple contradiction detection based on semantic similarity
        // High similarity + different facts could indicate contradiction

        if (_completionService == null)
        {
            // Rule-based fallback
            return await DetectContradictionRuleBasedAsync(chunk1, chunk2, cancellationToken);
        }

        try
        {
            var passage1 = chunk1.Content.Substring(0, Math.Min(500, chunk1.Content.Length));
            var passage2 = chunk2.Content.Substring(0, Math.Min(500, chunk2.Content.Length));
            var prompt = $$"""
                Determine if these two text passages contradict each other.

                Passage 1: {{passage1}}

                Passage 2: {{passage2}}

                Respond in JSON: {"contradicts": true/false, "explanation": "...", "severity": 0.0-1.0}
                """;

            var response = await _completionService.GenerateJsonCompletionAsync(
                prompt, 200, cancellationToken);

            return ParseContradictionResponse(response, chunk1.ChunkId, chunk2.ChunkId);
        }
        catch
        {
            return await DetectContradictionRuleBasedAsync(chunk1, chunk2, cancellationToken);
        }
    }

    private Task<Contradiction?> DetectContradictionRuleBasedAsync(
        RetrievedChunk chunk1,
        RetrievedChunk chunk2,
        CancellationToken cancellationToken)
    {
        // Simple rule-based contradiction detection
        // Look for negation patterns

        var negationPatterns = new[] { "not", "never", "no ", "don't", "doesn't", "won't", "isn't", "aren't" };

        var content1Lower = chunk1.Content.ToLowerInvariant();
        var content2Lower = chunk2.Content.ToLowerInvariant();

        // Check if one chunk negates something in the other
        foreach (var pattern in negationPatterns)
        {
            if ((content1Lower.Contains(pattern) && !content2Lower.Contains(pattern)) ||
                (!content1Lower.Contains(pattern) && content2Lower.Contains(pattern)))
            {
                // Potential contradiction - need more analysis
                var similarity = CalculateCosineSimilarity(chunk1.Embedding.Values, chunk2.Embedding.Values);

                // High similarity with opposite sentiment could be contradiction
                if (similarity > 0.7)
                {
                    return Task.FromResult<Contradiction?>(new Contradiction
                    {
                        ChunkId1 = chunk1.ChunkId,
                        ChunkId2 = chunk2.ChunkId,
                        Severity = 0.5,
                        Description = "Potential contradiction detected (rule-based)",
                        Evidence = $"Negation pattern found: '{pattern}'"
                    });
                }
            }
        }

        return Task.FromResult<Contradiction?>(null);
    }

    private async Task<double> CalculateEntailmentScoreAsync(
        string claim,
        string source,
        CancellationToken cancellationToken)
    {
        if (_completionService != null)
        {
            try
            {
                var prompt = $"""
                    Does the source text support the claim? Rate from 0.0 (no support) to 1.0 (strong support).

                    Claim: {claim}

                    Source: {source.Substring(0, Math.Min(500, source.Length))}

                    Respond with just a number between 0.0 and 1.0.
                    """;

                var response = await _completionService.GenerateCompletionAsync(
                    prompt, 10, 0.1f, cancellationToken);

                if (double.TryParse(response.Trim(), out var score))
                {
                    return Math.Clamp(score, 0, 1);
                }
            }
            catch
            {
                // Fall through to embedding-based calculation
            }
        }

        // Fallback: use embedding similarity as entailment proxy
        var claimEmbedding = await _embeddingService.GenerateEmbeddingAsync(claim, cancellationToken);
        var sourceEmbedding = await _embeddingService.GenerateEmbeddingAsync(source, cancellationToken);

        return CalculateCosineSimilarity(claimEmbedding, sourceEmbedding);
    }

    private double CalculateCosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;

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

    private double CalculateLexicalOverlap(string query, string content)
    {
        var queryTokens = Tokenize(query);
        var contentTokens = Tokenize(content);

        if (queryTokens.Count == 0) return 0;

        var overlap = queryTokens.Intersect(contentTokens).Count();
        return (double)overlap / queryTokens.Count;
    }

    private HashSet<string> Tokenize(string text)
    {
        return text.ToLowerInvariant()
            .Split(new[] { ' ', ',', '.', '!', '?', ';', ':', '\n', '\r', '\t' },
                StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2)
            .ToHashSet();
    }

    private RelevanceGrade GradeRelevance(double score)
    {
        return score switch
        {
            >= 0.8 => RelevanceGrade.Correct,
            >= 0.5 => RelevanceGrade.Ambiguous,
            _ => RelevanceGrade.Incorrect
        };
    }

    private double CalculateOverallConfidence(IReadOnlyList<VerifiedChunk> chunks)
    {
        if (chunks.Count == 0) return 0;

        var relevantChunks = chunks.Where(c => c.IsRelevant).ToList();
        if (relevantChunks.Count == 0) return 0;

        // Weighted average: more weight to higher-scoring chunks
        var weightedSum = relevantChunks.Sum(c => c.RelevanceScore * c.RelevanceScore);
        var weightSum = relevantChunks.Sum(c => c.RelevanceScore);

        var avgScore = weightSum > 0 ? weightedSum / weightSum : 0;

        // Factor in evidence coverage
        var coverage = (double)relevantChunks.Count / chunks.Count;

        return (avgScore * 0.7) + (coverage * 0.3);
    }

    private ValidationStatus DetermineValidationStatus(
        IReadOnlyList<VerifiedChunk> chunks,
        double confidence)
    {
        var relevantCount = chunks.Count(c => c.IsRelevant);
        var correctCount = chunks.Count(c => c.RelevanceGrade == RelevanceGrade.Correct);

        if (correctCount >= _options.MinCorrectChunks && confidence >= _options.ValidationConfidenceThreshold)
            return ValidationStatus.Validated;

        if (relevantCount >= _options.MinEvidenceChunks)
            return ValidationStatus.PartiallyValidated;

        if (relevantCount > 0)
            return ValidationStatus.Ambiguous;

        return ValidationStatus.Insufficient;
    }

    private VerificationAction DetermineRecommendedAction(
        ValidationStatus status,
        IReadOnlyList<VerifiedChunk> chunks)
    {
        return status switch
        {
            ValidationStatus.Validated => VerificationAction.Proceed,
            ValidationStatus.PartiallyValidated => VerificationAction.ProceedWithCaution,
            ValidationStatus.Ambiguous => VerificationAction.RefineQuery,
            ValidationStatus.Insufficient => VerificationAction.ExpandSearch,
            _ => VerificationAction.ManualReview
        };
    }

    private string GenerateVerificationNotes(double relevance, double semantic, double lexical)
    {
        var notes = new List<string>();

        if (relevance < 0.3)
            notes.Add("Low overall relevance");
        if (semantic < 0.5 && lexical > 0.5)
            notes.Add("High lexical match but low semantic similarity");
        if (semantic > 0.7 && lexical < 0.3)
            notes.Add("High semantic similarity but low keyword overlap");

        return string.Join("; ", notes);
    }

    private List<(string Text, int StartIndex, int EndIndex)> SplitIntoSegments(string content)
    {
        var segments = new List<(string, int, int)>();
        var sentences = content.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);

        var currentIndex = 0;
        foreach (var sentence in sentences)
        {
            var trimmed = sentence.Trim();
            if (trimmed.Length > 10)
            {
                var startIndex = content.IndexOf(trimmed, currentIndex, StringComparison.Ordinal);
                if (startIndex >= 0)
                {
                    segments.Add((trimmed, startIndex, startIndex + trimmed.Length));
                    currentIndex = startIndex + trimmed.Length;
                }
            }
        }

        return segments;
    }

    private List<HallucinatedSpan> CombineHallucinatedSpans(
        List<HallucinatedSpan> embeddingSpans,
        List<HallucinatedSpan>? llmSpans)
    {
        if (llmSpans == null)
            return embeddingSpans;

        var combined = new List<HallucinatedSpan>(embeddingSpans);

        foreach (var llmSpan in llmSpans)
        {
            // Check if already covered by embedding span
            var overlapping = combined.FirstOrDefault(s =>
                (s.StartIndex <= llmSpan.StartIndex && s.EndIndex >= llmSpan.StartIndex) ||
                (llmSpan.StartIndex <= s.StartIndex && llmSpan.EndIndex >= s.StartIndex));

            if (overlapping == null)
            {
                combined.Add(llmSpan);
            }
            else
            {
                // Increase confidence for overlapping spans
                overlapping.Confidence = Math.Max(overlapping.Confidence, llmSpan.Confidence);
            }
        }

        return combined;
    }

    private double ParseHallucinationScore(string jsonResponse)
    {
        try
        {
            // Simple parsing - in production use proper JSON deserialization
            var scoreMatch = System.Text.RegularExpressions.Regex.Match(
                jsonResponse, @"""score""\s*:\s*([0-9.]+)");
            if (scoreMatch.Success && double.TryParse(scoreMatch.Groups[1].Value, out var score))
            {
                return Math.Clamp(score, 0, 1);
            }
        }
        catch { }
        return 0.5; // Default uncertain score
    }

    private List<HallucinatedSpan> ParseUnsupportedClaims(string jsonResponse, string content)
    {
        var spans = new List<HallucinatedSpan>();

        try
        {
            // Simple parsing
            var claimsMatch = System.Text.RegularExpressions.Regex.Match(
                jsonResponse, @"""unsupported_claims""\s*:\s*\[(.*?)\]");
            if (claimsMatch.Success)
            {
                var claims = claimsMatch.Groups[1].Value
                    .Split(',')
                    .Select(c => c.Trim().Trim('"'))
                    .Where(c => !string.IsNullOrEmpty(c));

                foreach (var claim in claims)
                {
                    var index = content.IndexOf(claim, StringComparison.OrdinalIgnoreCase);
                    if (index >= 0)
                    {
                        spans.Add(new HallucinatedSpan
                        {
                            Text = claim,
                            StartIndex = index,
                            EndIndex = index + claim.Length,
                            Confidence = 0.8,
                            Reason = "Identified by LLM as unsupported"
                        });
                    }
                }
            }
        }
        catch { }

        return spans;
    }

    private Contradiction? ParseContradictionResponse(string jsonResponse, string chunkId1, string chunkId2)
    {
        try
        {
            if (jsonResponse.Contains("\"contradicts\": true", StringComparison.OrdinalIgnoreCase) ||
                jsonResponse.Contains("\"contradicts\":true", StringComparison.OrdinalIgnoreCase))
            {
                var severityMatch = System.Text.RegularExpressions.Regex.Match(
                    jsonResponse, @"""severity""\s*:\s*([0-9.]+)");
                var severity = severityMatch.Success && double.TryParse(severityMatch.Groups[1].Value, out var s)
                    ? s
                    : 0.5;

                var explanationMatch = System.Text.RegularExpressions.Regex.Match(
                    jsonResponse, @"""explanation""\s*:\s*""([^""]+)""");
                var explanation = explanationMatch.Success
                    ? explanationMatch.Groups[1].Value
                    : "Contradiction detected";

                return new Contradiction
                {
                    ChunkId1 = chunkId1,
                    ChunkId2 = chunkId2,
                    Severity = severity,
                    Description = explanation
                };
            }
        }
        catch { }

        return null;
    }

    #endregion
}

/// <summary>
/// Interface for retrieval verification service
/// </summary>
public interface IRetrievalVerificationService
{
    /// <summary>
    /// Verifies the relevance and quality of retrieved chunks
    /// </summary>
    Task<RetrievalVerificationResult> VerifyRetrievalAsync(
        string query,
        IReadOnlyList<RetrievedChunk> retrievedChunks,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks for hallucination in generated content
    /// </summary>
    Task<HallucinationCheckResult> CheckHallucinationAsync(
        string generatedContent,
        IReadOnlyList<RetrievedChunk> sourceChunks,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks factual consistency across chunks
    /// </summary>
    Task<FactualConsistencyResult> CheckFactualConsistencyAsync(
        IReadOnlyList<RetrievedChunk> chunks,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies if a claim can be attributed to sources
    /// </summary>
    Task<SourceAttributionResult> VerifySourceAttributionAsync(
        string claim,
        IReadOnlyList<RetrievedChunk> potentialSources,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for retrieval verification
/// </summary>
public class RetrievalVerificationOptions
{
    /// <summary>Minimum relevance threshold (0-1)</summary>
    public double MinRelevanceThreshold { get; set; } = 0.5;

    /// <summary>Minimum entailment score for attribution</summary>
    public double MinEntailmentScore { get; set; } = 0.6;

    /// <summary>Minimum similarity for attribution</summary>
    public double MinAttributionSimilarity { get; set; } = 0.7;

    /// <summary>Minimum chunks for evidence</summary>
    public int MinEvidenceChunks { get; set; } = 2;

    /// <summary>Maximum chunks to consider as evidence</summary>
    public int MaxEvidenceChunks { get; set; } = 10;

    /// <summary>Minimum correct chunks for validation</summary>
    public int MinCorrectChunks { get; set; } = 1;

    /// <summary>Minimum agreement score</summary>
    public double MinAgreementScore { get; set; } = 0.5;

    /// <summary>Hallucination threshold</summary>
    public double HallucinationThreshold { get; set; } = 0.5;

    /// <summary>Minimum support similarity</summary>
    public double MinSupportSimilarity { get; set; } = 0.6;

    /// <summary>Validation confidence threshold</summary>
    public double ValidationConfidenceThreshold { get; set; } = 0.6;
}

/// <summary>
/// Retrieved chunk with embedding
/// </summary>
public class RetrievedChunk
{
    public string ChunkId { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public EmbeddingVector Embedding { get; init; } = null!;
    public double Score { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// Verified chunk result
/// </summary>
public class VerifiedChunk
{
    public string ChunkId { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public double OriginalScore { get; init; }
    public double RelevanceScore { get; init; }
    public double SemanticSimilarity { get; init; }
    public double LexicalOverlap { get; init; }
    public bool IsRelevant { get; init; }
    public RelevanceGrade RelevanceGrade { get; init; }
    public string VerificationNotes { get; init; } = string.Empty;
}

/// <summary>
/// Relevance grade (CRAG-style)
/// </summary>
public enum RelevanceGrade
{
    Correct,
    Ambiguous,
    Incorrect
}

/// <summary>
/// Verification result
/// </summary>
public class RetrievalVerificationResult
{
    public bool IsValid { get; init; }
    public double OverallConfidence { get; init; }
    public ValidationStatus ValidationStatus { get; init; }
    public IReadOnlyList<VerifiedChunk> VerifiedChunks { get; init; } = Array.Empty<VerifiedChunk>();
    public MultiEvidenceResult? MultiEvidenceValidation { get; init; }
    public VerificationAction RecommendedAction { get; init; }
    public string? Message { get; init; }
    public double ExecutionTimeMs { get; init; }
}

/// <summary>
/// Validation status
/// </summary>
public enum ValidationStatus
{
    Validated,
    PartiallyValidated,
    Ambiguous,
    Insufficient,
    Failed
}

/// <summary>
/// Recommended action after verification
/// </summary>
public enum VerificationAction
{
    Proceed,
    ProceedWithCaution,
    RefineQuery,
    ExpandSearch,
    ManualReview
}

/// <summary>
/// Multi-evidence validation result
/// </summary>
public class MultiEvidenceResult
{
    public bool HasSufficientEvidence { get; init; }
    public int EvidenceCount { get; init; }
    public double AgreementScore { get; init; }
    public IReadOnlyList<string> TopEvidenceChunks { get; init; } = Array.Empty<string>();
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Hallucination check result
/// </summary>
public class HallucinationCheckResult
{
    public bool HasHallucination { get; init; }
    public double HallucinationScore { get; init; }
    public double Confidence { get; init; }
    public IReadOnlyList<HallucinatedSpan> HallucinatedSpans { get; init; } = Array.Empty<HallucinatedSpan>();
    public string Reason { get; init; } = string.Empty;
    public string VerificationMethod { get; init; } = string.Empty;
}

/// <summary>
/// Hallucinated text span
/// </summary>
public class HallucinatedSpan
{
    public string Text { get; init; } = string.Empty;
    public int StartIndex { get; init; }
    public int EndIndex { get; init; }
    public double Confidence { get; set; }
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// Factual consistency result
/// </summary>
public class FactualConsistencyResult
{
    public bool IsConsistent { get; init; }
    public double ConsistencyScore { get; init; }
    public IReadOnlyList<Contradiction> Contradictions { get; init; } = Array.Empty<Contradiction>();
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Contradiction between chunks
/// </summary>
public class Contradiction
{
    public string ChunkId1 { get; init; } = string.Empty;
    public string ChunkId2 { get; init; } = string.Empty;
    public double Severity { get; init; }
    public string Description { get; init; } = string.Empty;
    public string? Evidence { get; init; }
}

/// <summary>
/// Source attribution result
/// </summary>
public class SourceAttributionResult
{
    public bool IsAttributable { get; init; }
    public double AttributionScore { get; init; }
    public IReadOnlyList<SourceMatch> SupportingSources { get; init; } = Array.Empty<SourceMatch>();
    public SourceMatch? BestMatchingSource { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Source match
/// </summary>
public class SourceMatch
{
    public string ChunkId { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public double SimilarityScore { get; init; }
    public double EntailmentScore { get; init; }
    public bool IsStrongSupport { get; init; }
}
