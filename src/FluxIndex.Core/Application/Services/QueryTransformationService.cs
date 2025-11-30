using System.Text.Json;
using System.Text.RegularExpressions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// Query Transformation Service implementing advanced RAG techniques.
/// Supports HyDE, Multi-Query, Query Decomposition, and Query Intent Analysis.
/// </summary>
public class QueryTransformationService : IQueryTransformationService
{
    private readonly ITextCompletionService? _completionService;
    private readonly QueryTransformationOptions _options;
    private readonly ILogger<QueryTransformationService> _logger;

    // Patterns for query decomposition
    private static readonly Regex MultiPartPattern = new(
        @"\b(and|또한|그리고|or|또는|also|as\s+well\s+as)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex QuestionSplitPattern = new(
        @"(?<=[.?!])\s+(?=[A-Z가-힣])",
        RegexOptions.Compiled);

    private static readonly Regex ComparisonPattern = new(
        @"\b(compare|difference|vs\.?|versus|비교|차이점?|대비)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SequentialPattern = new(
        @"\b(then|after|before|first|next|다음|이후|이전|먼저)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public QueryTransformationService(
        ITextCompletionService? completionService,
        IOptions<QueryTransformationOptions> options,
        ILogger<QueryTransformationService> logger)
    {
        _completionService = completionService;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<HyDEResult> GenerateHypotheticalDocumentAsync(
        string query,
        HyDEOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        options ??= HyDEOptions.CreateDefault();

        var startTime = DateTime.UtcNow;

        _logger.LogDebug("Generating hypothetical document for query: {Query}", query);

        if (_completionService == null)
        {
            _logger.LogWarning("Text completion service not available, returning empty HyDE result");
            return new HyDEResult
            {
                OriginalQuery = query,
                HypotheticalDocument = string.Empty,
                QualityScore = 0,
                GenerationTimeMs = 0
            };
        }

        try
        {
            var prompt = BuildHyDEPrompt(query, options);
            var hypotheticalDoc = await _completionService.GenerateCompletionAsync(
                prompt,
                options.MaxLength,
                _options.HyDETemperature,
                cancellationToken);

            var elapsedMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            var qualityScore = EvaluateHyDEQuality(query, hypotheticalDoc);

            _logger.LogDebug(
                "Generated hypothetical document with quality score {Score} in {Elapsed}ms",
                qualityScore, elapsedMs);

            return new HyDEResult
            {
                OriginalQuery = query,
                HypotheticalDocument = hypotheticalDoc,
                QualityScore = qualityScore,
                TokensUsed = _completionService.CountTokens(hypotheticalDoc),
                GenerationTimeMs = elapsedMs
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate hypothetical document for query: {Query}", query);
            return new HyDEResult
            {
                OriginalQuery = query,
                HypotheticalDocument = string.Empty,
                QualityScore = 0,
                GenerationTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds
            };
        }
    }

    /// <inheritdoc />
    public async Task<QuOTEResult> GenerateQuestionOrientedEmbeddingAsync(
        string query,
        QuOTEOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        options ??= QuOTEOptions.CreateDefault();

        _logger.LogDebug("Generating question-oriented embedding for query: {Query}", query);

        if (_completionService == null)
        {
            // Fallback to rule-based expansion
            return GenerateRuleBasedQuOTE(query, options);
        }

        try
        {
            var prompt = BuildQuOTEPrompt(query, options);
            var jsonResponse = await _completionService.GenerateJsonCompletionAsync(
                prompt,
                _options.MaxQuOTETokens,
                cancellationToken);

            return ParseQuOTEResponse(query, jsonResponse, options);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate QuOTE, falling back to rule-based");
            return GenerateRuleBasedQuOTE(query, options);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GenerateMultipleQueriesAsync(
        string query,
        int count = 3,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        count = Math.Clamp(count, 1, _options.MaxMultiQueryCount);

        _logger.LogDebug("Generating {Count} query variations for: {Query}", count, query);

        if (_completionService == null)
        {
            return GenerateRuleBasedQueryVariations(query, count);
        }

        try
        {
            var prompt = BuildMultiQueryPrompt(query, count);
            var response = await _completionService.GenerateCompletionAsync(
                prompt,
                _options.MaxMultiQueryTokens,
                _options.MultiQueryTemperature,
                cancellationToken);

            var queries = ParseMultiQueryResponse(response, count);

            // Always include original query
            if (!queries.Contains(query, StringComparer.OrdinalIgnoreCase))
            {
                queries = queries.Prepend(query).Take(count).ToList();
            }

            _logger.LogDebug("Generated {Count} query variations", queries.Count);
            return queries.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate multiple queries, using rule-based fallback");
            return GenerateRuleBasedQueryVariations(query, count);
        }
    }

    /// <inheritdoc />
    public async Task<QueryDecompositionResult> DecomposeQueryAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        _logger.LogDebug("Decomposing query: {Query}", query);

        // First, try rule-based decomposition
        var ruleBasedResult = TryRuleBasedDecomposition(query);
        if (ruleBasedResult.SubQueries.Count > 1 && ruleBasedResult.Confidence >= 0.7f)
        {
            _logger.LogDebug(
                "Rule-based decomposition successful: {Count} sub-queries with {Confidence} confidence",
                ruleBasedResult.SubQueries.Count, ruleBasedResult.Confidence);
            return ruleBasedResult;
        }

        // If no LLM available or confidence is low, try LLM-based decomposition
        if (_completionService == null)
        {
            return ruleBasedResult;
        }

        try
        {
            var prompt = BuildDecompositionPrompt(query);
            var jsonResponse = await _completionService.GenerateJsonCompletionAsync(
                prompt,
                _options.MaxDecompositionTokens,
                cancellationToken);

            var llmResult = ParseDecompositionResponse(query, jsonResponse);

            // Use LLM result if it has higher confidence
            if (llmResult.Confidence > ruleBasedResult.Confidence)
            {
                _logger.LogDebug(
                    "LLM-based decomposition: {Count} sub-queries with {Confidence} confidence",
                    llmResult.SubQueries.Count, llmResult.Confidence);
                return llmResult;
            }

            return ruleBasedResult;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decompose query with LLM, using rule-based result");
            return ruleBasedResult;
        }
    }

    /// <inheritdoc />
    public async Task<QueryIntentResult> AnalyzeQueryIntentAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        _logger.LogDebug("Analyzing query intent: {Query}", query);

        // Rule-based intent analysis (fast path)
        var ruleBasedResult = AnalyzeIntentRuleBased(query);

        if (_completionService == null || ruleBasedResult.Confidence >= 0.8f)
        {
            return ruleBasedResult;
        }

        try
        {
            var prompt = BuildIntentAnalysisPrompt(query);
            var jsonResponse = await _completionService.GenerateJsonCompletionAsync(
                prompt,
                _options.MaxIntentAnalysisTokens,
                cancellationToken);

            return ParseIntentAnalysisResponse(query, jsonResponse, ruleBasedResult);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to analyze query intent with LLM, using rule-based result");
            return ruleBasedResult;
        }
    }

    /// <inheritdoc />
    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        // Service is healthy if either:
        // 1. No completion service is configured (rule-based mode)
        // 2. Completion service is available
        return Task.FromResult(_completionService == null || true);
    }

    #region Prompt Builders

    private string BuildHyDEPrompt(string query, HyDEOptions options)
    {
        var domainContext = string.IsNullOrEmpty(options.DomainContext)
            ? ""
            : $"Domain context: {options.DomainContext}\n";

        return $"""
            You are an expert document writer. Given a query, write a hypothetical document that would perfectly answer this query.

            {domainContext}Query: {query}

            Write a {options.DocumentStyle} document (approximately {options.MaxLength / 4} words) that would be the ideal answer to this query.
            Focus on providing specific, accurate, and relevant information.
            Do not include phrases like "I think" or "In my opinion". Write as if you are an authoritative source.

            Document:
            """;
    }

    private string BuildQuOTEPrompt(string query, QuOTEOptions options)
    {
        return $"""
            Analyze the following query and generate expanded queries and related questions.

            Query: {query}

            Generate a JSON response with:
            1. "expanded_queries": {options.MaxExpansions} alternative ways to phrase this query
            2. "related_questions": {options.MaxRelatedQuestions} related questions that could help answer the original query
            3. "query_weights": relative importance weights (0-1) for each expanded query

            JSON Response:
            """;
    }

    private string BuildMultiQueryPrompt(string query, int count)
    {
        return $"""
            Generate {count} alternative phrasings of the following query. Each variation should:
            - Maintain the same core meaning
            - Use different vocabulary or structure
            - Target potentially different aspects of the topic

            Original Query: {query}

            Output {count} queries, one per line:
            """;
    }

    private string BuildDecompositionPrompt(string query)
    {
        return $"""
            Analyze the following complex query and decompose it into simpler sub-queries.

            Query: {query}

            Determine if this query contains multiple distinct information needs and break them down.

            Generate a JSON response with:
            1. "sub_queries": array of simpler, focused sub-queries
            2. "relationship_type": one of "sequential", "parallel", "hierarchical", "conditional"
            3. "confidence": confidence score (0-1) in the decomposition
            4. "reasoning": brief explanation of the decomposition

            If the query is already simple, return the original query as the only sub-query.

            JSON Response:
            """;
    }

    private string BuildIntentAnalysisPrompt(string query)
    {
        return $"""
            Analyze the intent and characteristics of the following query.

            Query: {query}

            Generate a JSON response with:
            1. "primary_intent": main purpose (learning, problem_solving, research, decision_support, reference)
            2. "query_type": type (informational, procedural, troubleshooting, comparative, evaluative)
            3. "domain": subject domain
            4. "complexity": complexity level (simple, medium, complex, very_complex)
            5. "keywords": key terms and concepts
            6. "recommended_strategy": suggested retrieval strategy

            JSON Response:
            """;
    }

    #endregion

    #region Response Parsers

    private QuOTEResult ParseQuOTEResponse(string originalQuery, string jsonResponse, QuOTEOptions options)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonResponse);
            var root = doc.RootElement;

            var expandedQueries = new List<string>();
            if (root.TryGetProperty("expanded_queries", out var expanded))
            {
                foreach (var q in expanded.EnumerateArray())
                {
                    if (q.GetString() is string query)
                        expandedQueries.Add(query);
                }
            }

            var relatedQuestions = new List<string>();
            if (root.TryGetProperty("related_questions", out var related))
            {
                foreach (var q in related.EnumerateArray())
                {
                    if (q.GetString() is string question)
                        relatedQuestions.Add(question);
                }
            }

            var queryWeights = new Dictionary<string, float>();
            if (root.TryGetProperty("query_weights", out var weights))
            {
                foreach (var prop in weights.EnumerateObject())
                {
                    queryWeights[prop.Name] = prop.Value.GetSingle();
                }
            }

            return new QuOTEResult
            {
                OriginalQuery = originalQuery,
                ExpandedQueries = expandedQueries.AsReadOnly(),
                RelatedQuestions = relatedQuestions.AsReadOnly(),
                QueryWeights = queryWeights,
                QualityScore = expandedQueries.Count > 0 ? 0.8f : 0.3f
            };
        }
        catch (JsonException)
        {
            return GenerateRuleBasedQuOTE(originalQuery, options);
        }
    }

    private List<string> ParseMultiQueryResponse(string response, int maxCount)
    {
        var queries = response
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(q => q.Trim())
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .Select(q => Regex.Replace(q, @"^\d+[\.\)\-]\s*", "")) // Remove numbering
            .Take(maxCount)
            .ToList();

        return queries;
    }

    private QueryDecompositionResult ParseDecompositionResponse(string originalQuery, string jsonResponse)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonResponse);
            var root = doc.RootElement;

            var subQueries = new List<string>();
            if (root.TryGetProperty("sub_queries", out var queries))
            {
                foreach (var q in queries.EnumerateArray())
                {
                    if (q.GetString() is string query)
                        subQueries.Add(query);
                }
            }

            var relationshipType = QueryRelationshipType.Sequential;
            if (root.TryGetProperty("relationship_type", out var relType))
            {
                var relTypeStr = relType.GetString()?.ToLowerInvariant();
                relationshipType = relTypeStr switch
                {
                    "parallel" => QueryRelationshipType.Parallel,
                    "hierarchical" => QueryRelationshipType.Hierarchical,
                    "conditional" => QueryRelationshipType.Conditional,
                    _ => QueryRelationshipType.Sequential
                };
            }

            var confidence = 0.7f;
            if (root.TryGetProperty("confidence", out var conf))
            {
                confidence = conf.GetSingle();
            }

            // If no sub-queries extracted, return original
            if (subQueries.Count == 0)
            {
                subQueries.Add(originalQuery);
                confidence = 0.5f;
            }

            return new QueryDecompositionResult
            {
                OriginalQuery = originalQuery,
                SubQueries = subQueries.AsReadOnly(),
                Confidence = confidence,
                RelationshipType = relationshipType
            };
        }
        catch (JsonException)
        {
            return new QueryDecompositionResult
            {
                OriginalQuery = originalQuery,
                SubQueries = new[] { originalQuery },
                Confidence = 0.3f,
                RelationshipType = QueryRelationshipType.Sequential
            };
        }
    }

    private QueryIntentResult ParseIntentAnalysisResponse(
        string originalQuery,
        string jsonResponse,
        QueryIntentResult fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonResponse);
            var root = doc.RootElement;

            var result = new QueryIntentResult
            {
                OriginalQuery = originalQuery
            };

            if (root.TryGetProperty("primary_intent", out var intent))
                result.PrimaryIntent = intent.GetString() ?? fallback.PrimaryIntent;

            if (root.TryGetProperty("domain", out var domain))
                result.Domain = domain.GetString() ?? fallback.Domain;

            if (root.TryGetProperty("query_type", out var qType))
            {
                var typeStr = qType.GetString()?.ToLowerInvariant();
                result.QueryType = typeStr switch
                {
                    "procedural" => Domain.Models.QueryType.Procedural,
                    "troubleshooting" => Domain.Models.QueryType.Troubleshooting,
                    "comparative" => Domain.Models.QueryType.Comparative,
                    "evaluative" => Domain.Models.QueryType.Evaluative,
                    _ => Domain.Models.QueryType.Informational
                };
            }

            if (root.TryGetProperty("complexity", out var complexity))
            {
                var complexityStr = complexity.GetString()?.ToLowerInvariant();
                result.Complexity = complexityStr switch
                {
                    "medium" => QueryComplexity.Medium,
                    "complex" => QueryComplexity.Complex,
                    "very_complex" => QueryComplexity.VeryComplex,
                    _ => QueryComplexity.Simple
                };
            }

            if (root.TryGetProperty("keywords", out var keywords))
            {
                var keywordList = new List<string>();
                foreach (var kw in keywords.EnumerateArray())
                {
                    if (kw.GetString() is string keyword)
                        keywordList.Add(keyword);
                }
                result.Keywords = keywordList.AsReadOnly();
            }

            if (root.TryGetProperty("recommended_strategy", out var strategy))
                result.RecommendedStrategy = strategy.GetString() ?? fallback.RecommendedStrategy;

            result.Confidence = 0.85f;

            return result;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    #endregion

    #region Rule-Based Fallbacks

    private QueryDecompositionResult TryRuleBasedDecomposition(string query)
    {
        var subQueries = new List<string>();
        var confidence = 0.5f;
        var relationshipType = QueryRelationshipType.Sequential;

        // Check for multi-part queries with conjunctions
        if (MultiPartPattern.IsMatch(query))
        {
            var parts = MultiPartPattern.Split(query)
                .Select(p => p.Trim())
                .Where(p => p.Length > 5)
                .ToList();

            if (parts.Count > 1)
            {
                subQueries.AddRange(parts);
                confidence = 0.75f;
                relationshipType = QueryRelationshipType.Parallel;
            }
        }

        // Check for multiple questions
        if (subQueries.Count <= 1)
        {
            var questions = QuestionSplitPattern.Split(query)
                .Select(q => q.Trim())
                .Where(q => q.Length > 10)
                .ToList();

            if (questions.Count > 1)
            {
                subQueries = questions;
                confidence = 0.8f;
                relationshipType = QueryRelationshipType.Sequential;
            }
        }

        // Check for comparison queries
        if (subQueries.Count <= 1 && ComparisonPattern.IsMatch(query))
        {
            var compared = ExtractComparisonSubjects(query);
            if (compared.Count > 1)
            {
                subQueries = compared.Select(c => $"What is {c}?").ToList();
                subQueries.Add(query); // Add original comparison query
                confidence = 0.7f;
                relationshipType = QueryRelationshipType.Hierarchical;
            }
        }

        // Check for sequential queries
        if (subQueries.Count <= 1 && SequentialPattern.IsMatch(query))
        {
            relationshipType = QueryRelationshipType.Sequential;
        }

        // If no decomposition found, return original
        if (subQueries.Count == 0)
        {
            subQueries.Add(query);
            confidence = 0.3f;
        }

        return new QueryDecompositionResult
        {
            OriginalQuery = query,
            SubQueries = subQueries.AsReadOnly(),
            Confidence = confidence,
            RelationshipType = relationshipType
        };
    }

    private List<string> ExtractComparisonSubjects(string query)
    {
        var vsMatch = Regex.Match(query, @"(.+?)\s+(?:vs\.?|versus|vs)\s+(.+?)(?:\?|$)", RegexOptions.IgnoreCase);
        if (vsMatch.Success)
        {
            return new List<string>
            {
                vsMatch.Groups[1].Value.Trim(),
                vsMatch.Groups[2].Value.Trim()
            };
        }

        var diffMatch = Regex.Match(query, @"difference\s+between\s+(.+?)\s+and\s+(.+?)(?:\?|$)", RegexOptions.IgnoreCase);
        if (diffMatch.Success)
        {
            return new List<string>
            {
                diffMatch.Groups[1].Value.Trim(),
                diffMatch.Groups[2].Value.Trim()
            };
        }

        return new List<string>();
    }

    private QuOTEResult GenerateRuleBasedQuOTE(string query, QuOTEOptions options)
    {
        var expandedQueries = new List<string> { query };
        var relatedQuestions = new List<string>();
        var queryWeights = new Dictionary<string, float> { { query, 1.0f } };

        // Generate variations
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Synonym-based expansion (simplified)
        if (words.Length >= 2)
        {
            expandedQueries.Add($"What is {string.Join(" ", words)}?");
            expandedQueries.Add($"How does {string.Join(" ", words)} work?");
        }

        // Generate related questions
        relatedQuestions.Add($"What are the key features of {query}?");
        relatedQuestions.Add($"What are the benefits of {query}?");
        relatedQuestions.Add($"What are common issues with {query}?");

        return new QuOTEResult
        {
            OriginalQuery = query,
            ExpandedQueries = expandedQueries.Take(options.MaxExpansions).ToList().AsReadOnly(),
            RelatedQuestions = relatedQuestions.Take(options.MaxRelatedQuestions).ToList().AsReadOnly(),
            QueryWeights = queryWeights,
            QualityScore = 0.5f
        };
    }

    private List<string> GenerateRuleBasedQueryVariations(string query, int count)
    {
        var variations = new List<string> { query };

        // Add question form variations
        if (!query.EndsWith("?"))
        {
            variations.Add(query + "?");
        }

        // Add "what is" prefix
        if (!query.StartsWith("what", StringComparison.OrdinalIgnoreCase))
        {
            variations.Add($"What is {query}?");
        }

        // Add "how to" variation
        if (!query.StartsWith("how", StringComparison.OrdinalIgnoreCase))
        {
            variations.Add($"How to {query}?");
        }

        // Add explanation request
        variations.Add($"Explain {query}");

        return variations.Take(count).ToList();
    }

    private QueryIntentResult AnalyzeIntentRuleBased(string query)
    {
        var queryLower = query.ToLowerInvariant();

        // Determine intent
        var intent = Domain.Models.QueryIntent.Reference;
        if (queryLower.Contains("how") || queryLower.Contains("방법") || queryLower.Contains("어떻게"))
            intent = Domain.Models.QueryIntent.ProblemSolving;
        else if (queryLower.Contains("what") || queryLower.Contains("무엇") || queryLower.Contains("뭐"))
            intent = Domain.Models.QueryIntent.Learning;
        else if (queryLower.Contains("why") || queryLower.Contains("왜"))
            intent = Domain.Models.QueryIntent.Research;
        else if (queryLower.Contains("should") || queryLower.Contains("best") || queryLower.Contains("추천"))
            intent = Domain.Models.QueryIntent.DecisionSupport;

        // Determine query type
        var queryType = Domain.Models.QueryType.Informational;
        if (queryLower.Contains("error") || queryLower.Contains("오류") || queryLower.Contains("fix"))
            queryType = Domain.Models.QueryType.Troubleshooting;
        else if (ComparisonPattern.IsMatch(query))
            queryType = Domain.Models.QueryType.Comparative;
        else if (queryLower.Contains("how to") || queryLower.Contains("방법"))
            queryType = Domain.Models.QueryType.Procedural;

        // Determine complexity
        var wordCount = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var complexity = wordCount switch
        {
            <= 3 => QueryComplexity.Simple,
            <= 7 => QueryComplexity.Medium,
            <= 12 => QueryComplexity.Complex,
            _ => QueryComplexity.VeryComplex
        };

        // Extract keywords
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "the", "is", "are", "what", "how", "why", "when", "where",
            "을", "를", "이", "가", "은", "는", "의", "에"
        };
        var keywords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !stopWords.Contains(w))
            .Take(5)
            .ToList();

        // Recommend strategy
        var strategy = complexity switch
        {
            QueryComplexity.Simple => "keyword",
            QueryComplexity.Medium => "hybrid",
            QueryComplexity.Complex => "semantic",
            _ => "semantic_with_reranking"
        };

        return new QueryIntentResult
        {
            OriginalQuery = query,
            PrimaryIntent = intent.ToString(),
            QueryType = queryType,
            Domain = "general",
            Complexity = complexity,
            Keywords = keywords.AsReadOnly(),
            Confidence = 0.65f,
            RecommendedStrategy = strategy
        };
    }

    private float EvaluateHyDEQuality(string query, string hypotheticalDoc)
    {
        if (string.IsNullOrWhiteSpace(hypotheticalDoc))
            return 0;

        var score = 0.5f;

        // Length check
        if (hypotheticalDoc.Length >= 50)
            score += 0.1f;
        if (hypotheticalDoc.Length >= 100)
            score += 0.1f;

        // Check if query keywords appear in document
        var queryWords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var docWords = hypotheticalDoc.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var overlap = queryWords.Intersect(docWords).Count();
        score += Math.Min(overlap * 0.05f, 0.2f);

        return Math.Min(score, 1.0f);
    }

    #endregion
}

/// <summary>
/// Options for query transformation service
/// </summary>
public class QueryTransformationOptions
{
    /// <summary>
    /// Temperature for HyDE generation (lower = more focused)
    /// </summary>
    public float HyDETemperature { get; set; } = 0.7f;

    /// <summary>
    /// Temperature for multi-query generation (higher = more diverse)
    /// </summary>
    public float MultiQueryTemperature { get; set; } = 0.8f;

    /// <summary>
    /// Maximum number of queries in multi-query mode
    /// </summary>
    public int MaxMultiQueryCount { get; set; } = 5;

    /// <summary>
    /// Maximum tokens for QuOTE response
    /// </summary>
    public int MaxQuOTETokens { get; set; } = 500;

    /// <summary>
    /// Maximum tokens for multi-query response
    /// </summary>
    public int MaxMultiQueryTokens { get; set; } = 200;

    /// <summary>
    /// Maximum tokens for decomposition response
    /// </summary>
    public int MaxDecompositionTokens { get; set; } = 400;

    /// <summary>
    /// Maximum tokens for intent analysis response
    /// </summary>
    public int MaxIntentAnalysisTokens { get; set; } = 300;

    /// <summary>
    /// Enable caching of transformation results
    /// </summary>
    public bool EnableCaching { get; set; } = true;

    /// <summary>
    /// Cache duration in minutes
    /// </summary>
    public int CacheDurationMinutes { get; set; } = 30;
}
