using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Services.QueryOrchestration;

/// <summary>
/// Local query transformation without requiring LLM
/// Provides basic transformations using rules and patterns
/// </summary>
public class LocalQueryTransformer : IQueryOrchestrator
{
    private readonly ILogger<LocalQueryTransformer> _logger;
    private readonly ITextCompletionService? _textCompletionService;
    
    // Pattern-based rules for query analysis
    private static readonly Dictionary<string, QueryIntent> IntentPatterns = new()
    {
        { @"^(what|어떤|무엇)", QueryIntent.Factual },
        { @"^(how|어떻게|방법)", QueryIntent.Procedural },
        { @"(compare|비교|차이|vs)", QueryIntent.Analytical },
        { @"(why|왜|이유)", QueryIntent.Exploratory },
        { @"(define|정의|뜻|의미)", QueryIntent.Definitional }
    };

    public LocalQueryTransformer(ILogger<LocalQueryTransformer>? logger = null, ITextCompletionService? textCompletionService = null)
    {
        _logger = logger ?? new NullLogger<LocalQueryTransformer>();
        _textCompletionService = textCompletionService;
    }

    /// <summary>
    /// Analyzes query using pattern matching and heuristics
    /// </summary>
    public async Task<QueryPlan> AnalyzeQueryAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Analyzing query locally: {Query}", query);

        var plan = new QueryPlan
        {
            OriginalQuery = query,
            Complexity = DetermineComplexity(query),
            Intent = DetermineIntent(query),
            RequiresMultiHop = DetectMultiHop(query),
            DetectedEntities = ExtractEntities(query),
            RecommendedStrategy = DetermineStrategy(query),
            ConfidenceScore = 0.6f // Lower confidence for rule-based analysis
        };

        // If text completion service is available, enhance with AI analysis
        if (_textCompletionService != null)
        {
            try
            {
                await EnhanceWithLLM(plan, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LLM enhancement failed, using local analysis only");
            }
        }

        return plan;
    }

    /// <summary>
    /// Transforms query using local strategies or LLM if available
    /// </summary>
    public async Task<TransformedQuery> TransformQueryAsync(
        string query,
        QueryStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Transforming query with strategy: {Strategy}", strategy);

        var result = new TransformedQuery
        {
            OriginalQuery = query,
            Strategy = strategy
        };

        switch (strategy)
        {
            case QueryStrategy.HyDE:
                if (_textCompletionService != null)
                {
                    var hydeDoc = await GenerateHypotheticalDocumentAsync(query, cancellationToken);
                    result.PrimaryQuery = hydeDoc;
                    result.Queries = new List<string> { hydeDoc };
                }
                else
                {
                    // Fallback: use query expansion
                    result = await ExpandQueryLocally(query);
                }
                break;

            case QueryStrategy.MultiQuery:
                var subQueries = await DecomposeQueryAsync(query, 3, cancellationToken);
                result.Queries = subQueries.ToList();
                result.PrimaryQuery = query;
                break;

            case QueryStrategy.StepBack:
                var stepBackQuery = await GenerateStepBackQueryAsync(query, cancellationToken);
                result.PrimaryQuery = stepBackQuery;
                result.Queries = new List<string> { stepBackQuery, query };
                break;

            case QueryStrategy.Direct:
            default:
                result.PrimaryQuery = query;
                result.Queries = new List<string> { query };
                break;
        }

        return result;
    }

    /// <summary>
    /// Generates hypothetical document if LLM is available
    /// </summary>
    public async Task<string> GenerateHypotheticalDocumentAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (_textCompletionService == null)
        {
            _logger.LogWarning("No LLM service available for HyDE, returning expanded query");
            return ExpandQueryText(query);
        }

        var prompt = $"""
            Given this question: {query}
            
            Write a detailed answer that would appear in a knowledge base.
            Include specific details and technical terms.
            Length: 200-300 words.
            
            Answer:
            """;

        return await _textCompletionService.GenerateCompletionAsync(prompt, 300, 0.7f, cancellationToken);
    }

    /// <summary>
    /// Decomposes query using patterns or LLM
    /// </summary>
    public async Task<IEnumerable<string>> DecomposeQueryAsync(
        string query,
        int maxQueries = 3,
        CancellationToken cancellationToken = default)
    {
        // Try local decomposition first
        var localQueries = DecomposeQueryLocally(query);
        
        if (localQueries.Count() >= 2)
        {
            return localQueries.Take(maxQueries);
        }

        // If LLM is available and local decomposition insufficient
        if (_textCompletionService != null)
        {
            var prompt = $"""
                Break down this question into {maxQueries} simpler questions:
                {query}
                
                Format each question on a new line starting with "- "
                """;

            var response = await _textCompletionService.GenerateCompletionAsync(prompt, 200, 0.7f, cancellationToken);
            var lines = response.Split('\n')
                .Where(l => l.Trim().StartsWith("-"))
                .Select(l => l.Trim().TrimStart('-').Trim())
                .Where(l => !string.IsNullOrEmpty(l))
                .Take(maxQueries);

            if (lines.Any())
                return lines;
        }

        return localQueries;
    }

    /// <summary>
    /// Generates step-back query
    /// </summary>
    public async Task<string> GenerateStepBackQueryAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        // Try local generalization first
        var generalQuery = GeneralizeQueryLocally(query);
        
        if (_textCompletionService != null && generalQuery == query)
        {
            var prompt = $"""
                Given this specific question: {query}
                
                Generate a more general question about the underlying concept.
                
                General question:
                """;

            generalQuery = await _textCompletionService.GenerateCompletionAsync(prompt, 100, 0.7f, cancellationToken);
        }

        return generalQuery;
    }

    // Local transformation methods (no LLM required)

    private QueryComplexity DetermineComplexity(string query)
    {
        var wordCount = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var hasMultipleClauses = query.Contains(',') || query.Contains("and") || query.Contains("그리고");
        var hasQuestionWords = Regex.Matches(query, @"\?|어떻게|무엇|왜|언제|어디").Count;

        if (wordCount < 5 && !hasMultipleClauses)
            return QueryComplexity.Simple;
        if (wordCount < 10 && hasQuestionWords <= 1)
            return QueryComplexity.Moderate;
        if (hasMultipleClauses || hasQuestionWords > 1)
            return QueryComplexity.Complex;
        
        return QueryComplexity.Moderate;
    }

    private QueryIntent DetermineIntent(string query)
    {
        var lowerQuery = query.ToLowerInvariant();
        
        foreach (var pattern in IntentPatterns)
        {
            if (Regex.IsMatch(lowerQuery, pattern.Key, RegexOptions.IgnoreCase))
            {
                return pattern.Value;
            }
        }
        
        return QueryIntent.Factual;
    }

    private bool DetectMultiHop(string query)
    {
        var multiHopIndicators = new[] { "and then", "그리고", "다음", "after", "before", "결과" };
        return multiHopIndicators.Any(indicator => query.Contains(indicator, StringComparison.OrdinalIgnoreCase));
    }

    private List<string> ExtractEntities(string query)
    {
        var entities = new List<string>();
        
        // Extract capitalized words (potential entities)
        var matches = Regex.Matches(query, @"\b[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*\b");
        entities.AddRange(matches.Select(m => m.Value));
        
        // Extract Korean product names/models
        var koreanMatches = Regex.Matches(query, @"[A-Z0-9]{2,}[-A-Z0-9]*");
        entities.AddRange(koreanMatches.Select(m => m.Value));
        
        return entities.Distinct().ToList();
    }

    private QueryStrategy DetermineStrategy(string query)
    {
        var complexity = DetermineComplexity(query);
        var hasVagueTerms = new[] { "그", "유명한", "최신", "좋은", "famous", "best", "latest" }
            .Any(term => query.Contains(term, StringComparison.OrdinalIgnoreCase));
        
        if (hasVagueTerms)
            return QueryStrategy.HyDE;
        
        if (complexity == QueryComplexity.Complex || complexity == QueryComplexity.VeryComplex)
            return QueryStrategy.MultiQuery;
        
        if (query.Contains("?") && query.Length > 50)
            return QueryStrategy.StepBack;
        
        return QueryStrategy.Direct;
    }

    private IEnumerable<string> DecomposeQueryLocally(string query)
    {
        var subQueries = new List<string>();
        
        // Split by conjunctions
        var parts = Regex.Split(query, @",|and|그리고|및");
        
        if (parts.Length > 1)
        {
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (!string.IsNullOrEmpty(trimmed) && trimmed.Length > 5)
                {
                    subQueries.Add(trimmed);
                }
            }
        }
        
        // If no decomposition possible, return original
        if (!subQueries.Any())
        {
            subQueries.Add(query);
        }
        
        return subQueries;
    }

    private string GeneralizeQueryLocally(string query)
    {
        // Remove specific details to generalize
        var generalized = query;
        
        // Remove specific model numbers
        generalized = Regex.Replace(generalized, @"[A-Z0-9]{2,}[-A-Z0-9]*", "model");
        
        // Replace specific with general terms
        var replacements = new Dictionary<string, string>
        {
            { @"\d+GB", "storage size" },
            { @"\d+ms", "latency" },
            { @"\d+%", "percentage" },
            { "Samsung|LG|Apple", "manufacturer" }
        };
        
        foreach (var replacement in replacements)
        {
            generalized = Regex.Replace(generalized, replacement.Key, replacement.Value, RegexOptions.IgnoreCase);
        }
        
        return generalized;
    }

    private string ExpandQueryText(string query)
    {
        // Simple query expansion without LLM
        var expanded = $"{query}. This refers to information about {query}. ";
        expanded += $"Details related to {query} including specifications, features, and characteristics.";
        return expanded;
    }

    private TransformedQuery ExpandQueryLocally(string query)
    {
        var keywords = ExtractKeywords(query);
        var expandedQueries = new List<string> { query };
        
        // Add variations
        if (keywords.Any())
        {
            expandedQueries.Add(string.Join(" ", keywords));
            expandedQueries.Add($"{query} information details");
        }
        
        return new TransformedQuery
        {
            OriginalQuery = query,
            Strategy = QueryStrategy.HyDE,
            PrimaryQuery = ExpandQueryText(query),
            Queries = expandedQueries
        };
    }

    private List<string> ExtractKeywords(string query)
    {
        // Remove common words and extract keywords
        var stopWords = new HashSet<string> { "the", "is", "at", "which", "on", "a", "an", "and", "or", "but" };
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !stopWords.Contains(w.ToLowerInvariant()))
            .ToList();
        
        return words;
    }

    private async Task EnhanceWithLLM(QueryPlan plan, CancellationToken cancellationToken)
    {
        if (_textCompletionService == null) return;

        var prompt = $"""
            Analyze this query: {plan.OriginalQuery}
            Current analysis: Complexity={plan.Complexity}, Intent={plan.Intent}
            
            Provide confidence score (0.0-1.0) for this analysis:
            """;

        try
        {
            var response = await _textCompletionService.GenerateCompletionAsync(prompt, 50, 0.3f, cancellationToken);
            if (float.TryParse(response.Trim(), out var confidence))
            {
                plan.ConfidenceScore = Math.Min(1.0f, Math.Max(0.0f, confidence));
            }
        }
        catch
        {
            // Keep original confidence
        }
    }
}