using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.RegularExpressions;

namespace FluxIndex.Core.Application.Services.Enrichment;

/// <summary>
/// Rule-based implementation of IMetadataEnrichmentService.
/// Provides metadata enrichment, quality evaluation, and relationship analysis
/// using heuristic rules without LLM dependencies.
/// </summary>
public partial class RuleBasedMetadataEnrichmentService : IMetadataEnrichmentService
{
    private static readonly char[] WordSplitSeparators = [' ', '\n', '\r', '\t'];

    private readonly ILogger<RuleBasedMetadataEnrichmentService> _logger;

    // Language detection character ranges
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "is", "at", "which", "on", "a", "an", "and", "or", "but",
        "in", "with", "to", "for", "of", "as", "by", "this", "that", "these", "those",
        "it", "its", "be", "are", "was", "were", "been", "being", "have", "has", "had",
        "do", "does", "did", "will", "would", "could", "should", "may", "might", "must",
        "if", "then", "else", "when", "where", "how", "what", "who", "whom", "whose",
        "not", "no", "nor", "so", "too", "very", "just", "only", "also", "even"
    };

    public RuleBasedMetadataEnrichmentService(ILogger<RuleBasedMetadataEnrichmentService>? logger = null)
    {
        _logger = logger ?? NullLogger<RuleBasedMetadataEnrichmentService>.Instance;
    }

    /// <summary>
    /// Enriches chunk metadata using rule-based extraction.
    /// </summary>
    public Task<ChunkMetadata> EnrichMetadataAsync(
        string content,
        int chunkIndex,
        string? previousChunkContent = null,
        string? nextChunkContent = null,
        Dictionary<string, object>? documentMetadata = null,
        CancellationToken cancellationToken = default)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
            LogRuleBasedMetadataEnrichment4(_logger, chunkIndex);

        var metadata = new ChunkMetadata
        {
            // Text analysis metrics
            TokenCount = EstimateTokenCount(content),
            CharacterCount = content.Length,
            SentenceCount = CountSentences(content),
            ReadabilityScore = CalculateReadabilityScore(content),
            Language = DetectLanguage(content),

            // Semantic metadata
            Keywords = ExtractKeywords(content).ToList(),
            Entities = ExtractEntities(content).ToList(),
            Topics = ExtractTopics(content).ToList(),
            ContentType = DetectContentType(content),

            // Structural metadata
            SectionLevel = DetectSectionLevel(content),
            SectionTitle = ExtractSectionTitle(content),
            Headings = ExtractHeadings(content).ToList(),
            ContextBefore = TruncateContext(previousChunkContent, 100),
            ContextAfter = TruncateContext(nextChunkContent, 100),

            // Search optimization
            ImportanceScore = CalculateImportanceScore(content, chunkIndex),
            SearchableTerms = ExtractSearchableTerms(content).ToList(),
            KeywordWeights = CalculateKeywordWeights(content)
        };

        LogRuleBasedMetadataEnrichment3(_logger, metadata.Keywords.Count, metadata.Entities.Count, metadata.Topics.Count);

        return Task.FromResult(metadata);
    }

    /// <summary>
    /// Evaluates chunk quality using rule-based heuristics.
    /// </summary>
    public Task<ChunkQuality> EvaluateQualityAsync(
        DocumentChunk chunk,
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        var content = chunk.Content;
        var quality = new ChunkQuality
        {
            ContentCompleteness = EvaluateCompleteness(content),
            InformationDensity = EvaluateInformationDensity(content),
            Coherence = EvaluateCoherence(content),
            Uniqueness = 1.0, // Requires comparison with other chunks

            QueryRelevanceScore = query != null ? CalculateQueryRelevance(content, query) : 0.0,
            ContextualRelevance = EvaluateContextualRelevance(chunk),
            AuthorityScore = EvaluateAuthorityScore(content),
            FreshnessScore = EvaluateFreshnessScore(chunk),

            RetrievalCount = 0,
            ClickThroughRate = 0.0,
            PositiveFeedback = 0,
            NegativeFeedback = 0,
            UserRating = 0.0,
            LastAccessed = DateTime.UtcNow
        };

        if (_logger.IsEnabled(LogLevel.Debug))
            LogRuleBasedMetadataEnrichment2(_logger, quality.ContentCompleteness, quality.InformationDensity, quality.Coherence);

        return Task.FromResult(quality);
    }

    /// <summary>
    /// Analyzes relationships between chunks using keyword similarity.
    /// </summary>
    public Task<List<ChunkRelationship>> AnalyzeRelationshipsAsync(
        DocumentChunk sourceChunk,
        IEnumerable<DocumentChunk> candidateChunks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceChunk);
        ArgumentNullException.ThrowIfNull(candidateChunks);

        var relationships = new List<ChunkRelationship>();
        var sourceKeywords = ExtractKeywords(sourceChunk.Content).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (sourceKeywords.Count == 0)
            return Task.FromResult(relationships);

        foreach (var candidate in candidateChunks)
        {
            if (candidate.Id == sourceChunk.Id)
                continue;

            cancellationToken.ThrowIfCancellationRequested();

            var candidateKeywords = ExtractKeywords(candidate.Content).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (candidateKeywords.Count == 0)
                continue;

            // Calculate Jaccard similarity
            var intersection = sourceKeywords.Intersect(candidateKeywords, StringComparer.OrdinalIgnoreCase).Count();
            var union = sourceKeywords.Union(candidateKeywords, StringComparer.OrdinalIgnoreCase).Count();
            var similarity = union > 0 ? (double)intersection / union : 0.0;

            // Only create relationships for significant similarity
            if (similarity >= 0.15)
            {
                var relationshipType = DetermineRelationshipType(sourceChunk, candidate, similarity);
                relationships.Add(new ChunkRelationship
                {
                    SourceChunkId = sourceChunk.Id,
                    TargetChunkId = candidate.Id,
                    Type = relationshipType,
                    Strength = similarity,
                    Description = $"Keyword similarity: {similarity:P1}",
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        LogRuleBasedMetadataEnrichment1(_logger, relationships.Count, sourceChunk.Id);

        return Task.FromResult(relationships);
    }

    #region Text Analysis Helpers

    private static int EstimateTokenCount(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0;

        // Rough estimation: ~4 characters per token for English
        // ~2 characters per token for CJK languages
        var language = DetectLanguage(content);
        var charsPerToken = language is "ko" or "zh" or "ja" ? 2.0 : 4.0;
        return (int)Math.Ceiling(content.Length / charsPerToken);
    }

    private static int CountSentences(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0;

        // Count sentence-ending punctuation
        return Regex.Count(content, @"[.!?]+[\s\n]+|[.!?]+$");
    }

    private static double CalculateReadabilityScore(string content)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Length < 100)
            return 0.5;

        var sentences = Math.Max(1, CountSentences(content));
        var words = content.Split(WordSplitSeparators, StringSplitOptions.RemoveEmptyEntries).Length;
        var avgWordsPerSentence = (double)words / sentences;

        // Simple readability: lower avg words per sentence = easier to read
        // Target: 15-20 words per sentence
        if (avgWordsPerSentence <= 20)
            return Math.Min(1.0, avgWordsPerSentence / 20.0 * 0.8 + 0.2);
        else
            return Math.Max(0.3, 1.0 - (avgWordsPerSentence - 20) / 40.0);
    }

    private static string DetectLanguage(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "unknown";

        // Korean (Hangul)
        if (content.Any(c => c >= 0xAC00 && c <= 0xD7AF))
            return "ko";

        // Chinese
        if (content.Any(c => c >= 0x4E00 && c <= 0x9FFF))
            return "zh";

        // Japanese (Hiragana or Katakana)
        if (content.Any(c => (c >= 0x3040 && c <= 0x309F) || (c >= 0x30A0 && c <= 0x30FF)))
            return "ja";

        return "en";
    }

    #endregion

    #region Keyword and Entity Extraction

    private static IEnumerable<string> ExtractKeywords(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            yield break;

        var words = Regex.Split(content, @"\W+")
            .Where(w => w.Length >= 3 && w.Length <= 30)
            .Where(w => !StopWords.Contains(w))
            .Where(w => !Regex.IsMatch(w, @"^\d+$"));

        var wordCounts = words
            .GroupBy(w => w.ToLowerInvariant())
            .OrderByDescending(g => g.Count())
            .Take(15);

        foreach (var group in wordCounts)
        {
            yield return group.Key;
        }
    }

    private static IEnumerable<string> ExtractEntities(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            yield break;

        // Capitalized words (potential proper nouns)
        var properNouns = Regex.Matches(content, @"\b([A-Z][a-z]+(?:\s+[A-Z][a-z]+)*)\b");
        var entities = properNouns
            .Cast<Match>()
            .Select(m => m.Value)
            .Where(e => e.Length > 2 && !StopWords.Contains(e.ToLowerInvariant()))
            .GroupBy(e => e)
            .OrderByDescending(g => g.Count())
            .Take(10);

        foreach (var entity in entities)
        {
            yield return entity.Key;
        }
    }

    private static IEnumerable<string> ExtractTopics(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            yield break;

        // Extract from headers
        var headers = Regex.Matches(content, @"^#{1,3}\s+(.+)$", RegexOptions.Multiline);
        foreach (Match match in headers)
        {
            if (match.Groups.Count > 1)
            {
                var header = match.Groups[1].Value.Trim();
                if (header.Length > 3 && header.Length < 60)
                    yield return header;
            }
        }
    }

    private static IEnumerable<string> ExtractHeadings(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            yield break;

        var headers = Regex.Matches(content, @"^(#{1,6})\s+(.+)$", RegexOptions.Multiline);
        foreach (Match match in headers)
        {
            if (match.Groups.Count > 2)
            {
                yield return match.Groups[2].Value.Trim();
            }
        }
    }

    private static string ExtractSectionTitle(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        var match = Regex.Match(content, @"^#{1,6}\s+(.+)$", RegexOptions.Multiline);
        return match.Success && match.Groups.Count > 1
            ? match.Groups[1].Value.Trim()
            : string.Empty;
    }

    private static int DetectSectionLevel(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0;

        var match = Regex.Match(content, @"^(#{1,6})\s+", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Length : 0;
    }

    private static string DetectContentType(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "text";

        // Check for code patterns
        if (Regex.IsMatch(content, @"```[\s\S]*```") ||
            Regex.IsMatch(content, @"^\s{4,}\S", RegexOptions.Multiline) ||
            Regex.IsMatch(content, @"function\s+\w+|class\s+\w+|def\s+\w+|public\s+\w+"))
            return "code";

        // Check for table patterns
        if (Regex.IsMatch(content, @"\|.+\|.+\|"))
            return "table";

        // Check for list patterns
        if (Regex.Count(content, @"^\s*[-*•]\s+", RegexOptions.Multiline) > 2 ||
            Regex.Count(content, @"^\s*\d+[\.\)]\s+", RegexOptions.Multiline) > 2)
            return "list";

        return "text";
    }

    private static IEnumerable<string> ExtractSearchableTerms(string content)
    {
        // Combine keywords and entities
        return ExtractKeywords(content)
            .Concat(ExtractEntities(content))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20);
    }

    private static Dictionary<string, float> CalculateKeywordWeights(string content)
    {
        var weights = new Dictionary<string, float>();
        if (string.IsNullOrWhiteSpace(content))
            return weights;

        var words = Regex.Split(content, @"\W+")
            .Where(w => w.Length >= 3 && !StopWords.Contains(w));

        var totalWords = words.Count();
        if (totalWords == 0)
            return weights;

        var wordCounts = words
            .GroupBy(w => w.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var kvp in wordCounts.OrderByDescending(x => x.Value).Take(15))
        {
            // TF-like weighting
            weights[kvp.Key] = (float)kvp.Value / totalWords;
        }

        return weights;
    }

    #endregion

    #region Quality Evaluation Helpers

    private static double EvaluateCompleteness(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0.0;

        // Check for incomplete sentences
        var sentences = CountSentences(content);
        var hasCompleteStart = Regex.IsMatch(content.TrimStart(), @"^[A-Z\uAC00-\uD7AF]");
        var hasCompleteEnd = Regex.IsMatch(content.TrimEnd(), @"[.!?\uAC00-\uD7AF]$");

        var score = 0.5;
        if (sentences >= 1) score += 0.2;
        if (hasCompleteStart) score += 0.15;
        if (hasCompleteEnd) score += 0.15;

        return Math.Min(1.0, score);
    }

    private static double EvaluateInformationDensity(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0.0;

        var words = content.Split(WordSplitSeparators, StringSplitOptions.RemoveEmptyEntries);
        var uniqueWords = words.Select(w => w.ToLowerInvariant()).Distinct().Count();

        if (words.Length == 0)
            return 0.0;

        // Ratio of unique words to total words
        var uniqueRatio = (double)uniqueWords / words.Length;

        // More unique words = higher information density
        return Math.Min(1.0, uniqueRatio * 1.2);
    }

    private static double EvaluateCoherence(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0.0;

        // Simple coherence: consistent sentence structure
        var sentences = CountSentences(content);
        if (sentences == 0)
            return 0.5;

        // Check for transition words indicating coherent flow
        var transitionWords = new[] { "therefore", "however", "moreover", "furthermore",
            "additionally", "consequently", "thus", "hence", "meanwhile", "그러므로", "그러나",
            "또한", "따라서", "하지만" };

        var hasTransitions = transitionWords.Any(t =>
            content.Contains(t, StringComparison.OrdinalIgnoreCase));

        var score = 0.6;
        if (hasTransitions) score += 0.2;
        if (sentences >= 2 && sentences <= 10) score += 0.2;

        return Math.Min(1.0, score);
    }

    private static double CalculateQueryRelevance(string content, string query)
    {
        if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(query))
            return 0.0;

        var queryTerms = Regex.Split(query, @"\W+")
            .Where(w => w.Length >= 2 && !StopWords.Contains(w))
            .Select(w => w.ToLowerInvariant())
            .ToHashSet();

        if (queryTerms.Count == 0)
            return 0.0;

        var contentLower = content.ToLowerInvariant();
        var matchedTerms = queryTerms.Count(term => contentLower.Contains(term));

        return (double)matchedTerms / queryTerms.Count;
    }

    private static double EvaluateContextualRelevance(DocumentChunk chunk)
    {
        // Based on chunk position within document
        if (chunk.TotalChunks <= 0)
            return 0.5;

        // First and last chunks often contain important info
        var position = (double)chunk.ChunkIndex / Math.Max(1, chunk.TotalChunks - 1);
        if (chunk.ChunkIndex == 0 || chunk.ChunkIndex == chunk.TotalChunks - 1)
            return 0.8;

        return 0.5 + 0.2 * Math.Sin(position * Math.PI); // Slight preference for middle
    }

    private static double EvaluateAuthorityScore(string content)
    {
        // Check for authoritative indicators
        var authorityIndicators = new[]
        {
            @"\[[\d,\s]+\]", // Citations
            @"according to", @"research shows", @"studies indicate",
            @"official", @"documentation", @"specification"
        };

        var score = 0.5;
        foreach (var indicator in authorityIndicators)
        {
            if (Regex.IsMatch(content, indicator, RegexOptions.IgnoreCase))
                score += 0.1;
        }

        return Math.Min(1.0, score);
    }

    private static double EvaluateFreshnessScore(DocumentChunk chunk)
    {
        var daysSinceCreation = (DateTime.UtcNow - chunk.CreatedAt).TotalDays;
        if (daysSinceCreation < 0)
            daysSinceCreation = 0;

        // Decay over 365 days
        return Math.Max(0.3, 1.0 - (daysSinceCreation / 365.0) * 0.7);
    }

    #endregion

    #region Relationship Analysis Helpers

    private static RelationshipType DetermineRelationshipType(
        DocumentChunk source, DocumentChunk target, double similarity)
    {
        // Same document, adjacent chunks
        if (source.DocumentId == target.DocumentId)
        {
            if (Math.Abs(source.ChunkIndex - target.ChunkIndex) == 1)
                return RelationshipType.Sequential;
            return RelationshipType.Semantic;
        }

        // High similarity indicates semantic relationship
        if (similarity >= 0.5)
            return RelationshipType.Similarity;

        // Cross-document reference
        return RelationshipType.Reference;
    }

    private static string TruncateContext(string? content, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        if (content.Length <= maxLength)
            return content;

        return string.Concat(content.AsSpan(0, maxLength), "...");
    }

    private static double CalculateImportanceScore(string content, int chunkIndex)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0.0;

        var score = 0.5;

        // Headers indicate importance
        if (Regex.IsMatch(content, @"^#{1,2}\s+", RegexOptions.Multiline))
            score += 0.2;

        // First chunk bonus
        if (chunkIndex == 0)
            score += 0.15;

        // Information density bonus
        var density = EvaluateInformationDensity(content);
        score += density * 0.15;

        return Math.Min(1.0, score);
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Enriching metadata for chunk {ChunkIndex}")]
    private static partial void LogRuleBasedMetadataEnrichment4(ILogger logger, int chunkIndex);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Enriched metadata: Keywords={Keywords}, Entities={Entities}, Topics={Topics}")]
    private static partial void LogRuleBasedMetadataEnrichment3(ILogger logger, int keywords, int entities, int topics);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Quality evaluation: Completeness={Completeness:F2}, Density={Density:F2}, Coherence={Coherence:F2}")]
    private static partial void LogRuleBasedMetadataEnrichment2(ILogger logger, double completeness, double density, double coherence);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Found {Count} relationships for chunk {ChunkId}")]
    private static partial void LogRuleBasedMetadataEnrichment1(ILogger logger, int count, string chunkId);

    #endregion
}
