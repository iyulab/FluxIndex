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
public partial class QueryTransformationService : IQueryTransformationService
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
        var documentCount = Math.Clamp(options.DocumentCount, 1, 10);

        if (_logger.IsEnabled(LogLevel.Debug))
            LogQueryTransformation17(_logger, documentCount, query);

        if (_completionService == null)
        {
            LogQueryTransformation16(_logger);
            return new HyDEResult
            {
                OriginalQuery = query,
                HypotheticalDocument = string.Empty,
                HypotheticalDocuments = Array.Empty<string>(),
                QualityScores = Array.Empty<float>(),
                QualityScore = 0,
                GenerationTimeMs = 0
            };
        }

        try
        {
            // Single document mode (standard HyDE)
            if (documentCount == 1)
            {
                return await GenerateSingleHypotheticalDocumentAsync(query, options, startTime, cancellationToken);
            }

            // Multi-hypothetical mode
            return await GenerateMultiHypotheticalDocumentsAsync(query, options, documentCount, startTime, cancellationToken);
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                LogQueryTransformation15(_logger, ex, query);
            return new HyDEResult
            {
                OriginalQuery = query,
                HypotheticalDocument = string.Empty,
                HypotheticalDocuments = Array.Empty<string>(),
                QualityScores = Array.Empty<float>(),
                QualityScore = 0,
                GenerationTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds
            };
        }
    }

    private async Task<HyDEResult> GenerateSingleHypotheticalDocumentAsync(
        string query,
        HyDEOptions options,
        DateTime startTime,
        CancellationToken cancellationToken)
    {
        var prompt = BuildHyDEPrompt(query, options, perspective: null);
        var hypotheticalDoc = await _completionService!.GenerateCompletionAsync(
            prompt,
            options.MaxLength,
            _options.HyDETemperature,
            cancellationToken);

        var elapsedMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
        var qualityScore = EvaluateHyDEQuality(query, hypotheticalDoc);

        if (_logger.IsEnabled(LogLevel.Debug))
            LogQueryTransformation14(_logger, qualityScore, elapsedMs);

        return new HyDEResult
        {
            OriginalQuery = query,
            HypotheticalDocument = hypotheticalDoc,
            HypotheticalDocuments = new[] { hypotheticalDoc },
            QualityScores = new[] { qualityScore },
            QualityScore = qualityScore,
            TokensUsed = _completionService.CountTokens(hypotheticalDoc),
            GenerationTimeMs = elapsedMs
        };
    }

    private async Task<HyDEResult> GenerateMultiHypotheticalDocumentsAsync(
        string query,
        HyDEOptions options,
        int documentCount,
        DateTime startTime,
        CancellationToken cancellationToken)
    {
        var perspectives = GetPerspectives(options, documentCount);

        var perspectivesStr = string.Join(", ", perspectives);
        LogQueryTransformation13(_logger, documentCount, perspectivesStr);

        List<string> documents;
        List<float> qualityScores;

        if (options.EnableParallelGeneration)
        {
            // Parallel generation with semaphore to control concurrency
            var semaphore = new SemaphoreSlim(Math.Min(documentCount, 5));
            var tasks = perspectives.Select(async (perspective, index) =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var temperature = _options.HyDETemperature + (index * options.TemperatureStep);
                    temperature = Math.Clamp(temperature, 0.0f, 2.0f);

                    var prompt = BuildHyDEPrompt(query, options, perspective);
                    return await _completionService!.GenerateCompletionAsync(
                        prompt,
                        options.MaxLength,
                        temperature,
                        cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            documents = (await Task.WhenAll(tasks)).ToList();
        }
        else
        {
            // Sequential generation
            documents = new List<string>(documentCount);
            for (int i = 0; i < documentCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var temperature = _options.HyDETemperature + (i * options.TemperatureStep);
                temperature = Math.Clamp(temperature, 0.0f, 2.0f);

                var prompt = BuildHyDEPrompt(query, options, perspectives[i]);
                var doc = await _completionService!.GenerateCompletionAsync(
                    prompt,
                    options.MaxLength,
                    temperature,
                    cancellationToken);
                documents.Add(doc);
            }
        }

        // Calculate quality scores for each document
        qualityScores = documents.Select(doc => EvaluateHyDEQuality(query, doc)).ToList();

        var elapsedMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
        var avgQualityScore = qualityScores.Count > 0 ? qualityScores.Average() : 0f;
        var totalTokens = documents.Sum(doc => _completionService!.CountTokens(doc));

        if (_logger.IsEnabled(LogLevel.Debug))
            LogQueryTransformation12(_logger, documents.Count, avgQualityScore, elapsedMs);

        return new HyDEResult
        {
            OriginalQuery = query,
            HypotheticalDocument = documents.FirstOrDefault() ?? string.Empty,
            HypotheticalDocuments = documents,
            QualityScores = qualityScores,
            QualityScore = avgQualityScore,
            TokensUsed = totalTokens,
            GenerationTimeMs = elapsedMs
        };
    }

    private static readonly string[] DefaultPerspectives = new[]
    {
        "expert technical explanation",
        "beginner-friendly introduction",
        "practical real-world application",
        "theoretical academic analysis",
        "problem-solving troubleshooting guide"
    };

    private static List<string> GetPerspectives(HyDEOptions options, int count)
    {
        if (options.Perspectives.Count > 0)
        {
            // Use provided perspectives, cycling if necessary
            return Enumerable.Range(0, count)
                .Select(i => options.Perspectives[i % options.Perspectives.Count])
                .ToList();
        }

        // Use default perspectives
        return DefaultPerspectives.Take(count).ToList();
    }

    /// <inheritdoc />
    public async Task<QuOTEResult> GenerateQuestionOrientedEmbeddingAsync(
        string query,
        QuOTEOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        options ??= QuOTEOptions.CreateDefault();

        if (_logger.IsEnabled(LogLevel.Debug))
            LogQueryTransformation11(_logger, query);

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
            LogQueryTransformation10(_logger, ex);
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

        if (_logger.IsEnabled(LogLevel.Debug))
            LogQueryTransformation9(_logger, count, query);

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

            LogQueryTransformation8(_logger, queries.Count);
            return queries.AsReadOnly();
        }
        catch (Exception ex)
        {
            LogQueryTransformation7(_logger, ex);
            return GenerateRuleBasedQueryVariations(query, count);
        }
    }

    /// <inheritdoc />
    public async Task<QueryDecompositionResult> DecomposeQueryAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (_logger.IsEnabled(LogLevel.Debug))
            LogQueryTransformation6(_logger, query);

        // First, try rule-based decomposition
        var ruleBasedResult = TryRuleBasedDecomposition(query);
        if (ruleBasedResult.SubQueries.Count > 1 && ruleBasedResult.Confidence >= 0.7f)
        {
            LogQueryTransformation5(_logger, ruleBasedResult.SubQueries.Count, ruleBasedResult.Confidence);
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
                LogQueryTransformation4(_logger, llmResult.SubQueries.Count, llmResult.Confidence);
                return llmResult;
            }

            return ruleBasedResult;
        }
        catch (Exception ex)
        {
            LogQueryTransformation3(_logger, ex);
            return ruleBasedResult;
        }
    }

    /// <inheritdoc />
    public async Task<QueryIntentResult> AnalyzeQueryIntentAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (_logger.IsEnabled(LogLevel.Debug))
            LogQueryTransformation2(_logger, query);

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
            LogQueryTransformation1(_logger, ex);
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

    private static string BuildHyDEPrompt(string query, HyDEOptions options, string? perspective)
    {
        var domainContext = string.IsNullOrEmpty(options.DomainContext)
            ? ""
            : $"Domain context: {options.DomainContext}\n";

        var perspectiveInstruction = string.IsNullOrEmpty(perspective)
            ? ""
            : $"Perspective: Write from the perspective of a {perspective}.\n";

        return $"""
            You are an expert document writer. Given a query, write a hypothetical document that would perfectly answer this query.

            {domainContext}{perspectiveInstruction}Query: {query}

            Write a {options.DocumentStyle} document (approximately {options.MaxLength / 4} words) that would be the ideal answer to this query.
            Focus on providing specific, accurate, and relevant information.
            Do not include phrases like "I think" or "In my opinion". Write as if you are an authoritative source.

            Document:
            """;
    }

    private static string BuildQuOTEPrompt(string query, QuOTEOptions options)
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

    private static string BuildMultiQueryPrompt(string query, int count)
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

    private static string BuildDecompositionPrompt(string query)
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

    private static string BuildIntentAnalysisPrompt(string query)
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

    private static QuOTEResult ParseQuOTEResponse(string originalQuery, string jsonResponse, QuOTEOptions options)
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

    private static List<string> ParseMultiQueryResponse(string response, int maxCount)
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

    private static QueryDecompositionResult ParseDecompositionResponse(string originalQuery, string jsonResponse)
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

    private static QueryIntentResult ParseIntentAnalysisResponse(
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

    private static QueryDecompositionResult TryRuleBasedDecomposition(string query)
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

    private static List<string> ExtractComparisonSubjects(string query)
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

    private static QuOTEResult GenerateRuleBasedQuOTE(string query, QuOTEOptions options)
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

    private static List<string> GenerateRuleBasedQueryVariations(string query, int count)
    {
        var variations = new List<string> { query };

        // Add question form variations
        if (!query.EndsWith('?'))
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

    private static QueryIntentResult AnalyzeIntentRuleBased(string query)
    {
        var queryLower = query.ToLowerInvariant();

        // Determine intent
        var intent = Domain.Models.QueryIntent.Reference;
        if (queryLower.Contains("how") || queryLower.Contains("방법") || queryLower.Contains("어떻게"))
            intent = Domain.Models.QueryIntent.ProblemSolving;
        else if (queryLower.Contains("what") || queryLower.Contains("무엇") || queryLower.Contains('뭐'))
            intent = Domain.Models.QueryIntent.Learning;
        else if (queryLower.Contains("why") || queryLower.Contains('왜'))
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

    private static float EvaluateHyDEQuality(string query, string hypotheticalDoc)
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

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Generating {Count} hypothetical document(s) for query: {Query}")]
    private static partial void LogQueryTransformation17(ILogger logger, int count, string query);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Text completion service not available, returning empty HyDE result")]
    private static partial void LogQueryTransformation16(ILogger logger);
    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to generate hypothetical document(s) for query: {Query}")]
    private static partial void LogQueryTransformation15(ILogger logger, Exception exception, string query);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Generated hypothetical document with quality score {Score} in {Elapsed}ms")]
    private static partial void LogQueryTransformation14(ILogger logger, float score, float elapsed);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Generating {Count} hypothetical documents with perspectives: {Perspectives}")]
    private static partial void LogQueryTransformation13(ILogger logger, int count, string perspectives);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Generated {Count} hypothetical documents with avg quality {AvgScore:F2} in {Elapsed}ms")]
    private static partial void LogQueryTransformation12(ILogger logger, int count, float avgScore, float elapsed);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Generating question-oriented embedding for query: {Query}")]
    private static partial void LogQueryTransformation11(ILogger logger, string query);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to generate QuOTE, falling back to rule-based")]
    private static partial void LogQueryTransformation10(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Generating {Count} query variations for: {Query}")]
    private static partial void LogQueryTransformation9(ILogger logger, int count, string query);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Generated {Count} query variations")]
    private static partial void LogQueryTransformation8(ILogger logger, int count);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to generate multiple queries, using rule-based fallback")]
    private static partial void LogQueryTransformation7(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Decomposing query: {Query}")]
    private static partial void LogQueryTransformation6(ILogger logger, string query);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Rule-based decomposition successful: {Count} sub-queries with {Confidence} confidence")]
    private static partial void LogQueryTransformation5(ILogger logger, int count, double confidence);
    [LoggerMessage(Level = LogLevel.Debug, Message = "LLM-based decomposition: {Count} sub-queries with {Confidence} confidence")]
    private static partial void LogQueryTransformation4(ILogger logger, int count, double confidence);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to decompose query with LLM, using rule-based result")]
    private static partial void LogQueryTransformation3(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Analyzing query intent: {Query}")]
    private static partial void LogQueryTransformation2(ILogger logger, string query);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to analyze query intent with LLM, using rule-based result")]
    private static partial void LogQueryTransformation1(ILogger logger, Exception exception);

    #endregion
}

/// <summary>
/// Options for query transformation service
/// </summary>
public partial class QueryTransformationOptions
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
