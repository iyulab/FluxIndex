using FluxIndex.Core.Interfaces;
using FluxIndex.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using System.Globalization;

namespace FluxIndex.Core.Services;

/// <summary>
/// 규칙 기반 메타데이터 추출기 (AI 서비스 불필요한 폴백)
/// 패턴 매칭과 휴리스틱으로 기본 메타데이터 추출
/// FileFlux RuleBasedMetadataExtractor 포팅
/// </summary>
public partial class RuleBasedMetadataExtractor : IRuleBasedMetadataExtractor
{
    private static readonly char[] WordSplitSeparators = [' ', '\n', '\r', '\t'];
    private static readonly string[] ParagraphSplitSeparators = ["\n\n", "\r\n\r\n"];

    private readonly ILogger<RuleBasedMetadataExtractor> _logger;

    public RuleBasedMetadataExtractor(ILogger<RuleBasedMetadataExtractor>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RuleBasedMetadataExtractor>.Instance;
    }

    /// <summary>
    /// 규칙 기반 메타데이터 추출
    /// </summary>
    public Task<ExtractedMetadata> ExtractAsync(
        string content,
        MetadataSchema schema,
        CancellationToken cancellationToken = default)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
            LogRuleBasedMetadata3(_logger, schema);

        var metadata = schema switch
        {
            MetadataSchema.ProductManual => ExtractProductManualMetadata(content),
            MetadataSchema.TechnicalDoc => ExtractTechnicalDocMetadata(content),
            MetadataSchema.Article => ExtractArticleMetadata(content),
            MetadataSchema.General => ExtractGeneralMetadata(content),
            MetadataSchema.Custom => ExtractGeneralMetadata(content),
            _ => new ExtractedMetadata()
        };

        metadata.ExtractionMethod = "RuleBased";
        metadata.Source = MetadataSource.RuleBased;
        metadata.ExtractedAt = DateTimeOffset.UtcNow;

        LogRuleBasedMetadata2(_logger, metadata.OverallConfidence);

        return Task.FromResult(metadata);
    }

    /// <summary>
    /// 두 메타데이터를 병합 (AI + RuleBased 하이브리드 전략용)
    /// </summary>
    public ExtractedMetadata MergeMetadata(ExtractedMetadata primary, ExtractedMetadata fallback)
    {
        var merged = new ExtractedMetadata
        {
            Source = MetadataSource.Merged,
            ExtractionMethod = "Hybrid",
            ExtractedAt = DateTimeOffset.UtcNow
        };

        // 기본 필드 병합 (primary 우선, 없으면 fallback)
        merged.Topics = MergeArrays(primary.Topics, fallback.Topics);
        merged.Keywords = MergeArrays(primary.Keywords, fallback.Keywords);
        merged.Description = string.IsNullOrEmpty(primary.Description) ? fallback.Description : primary.Description;
        merged.DocumentType = string.IsNullOrEmpty(primary.DocumentType) ? fallback.DocumentType : primary.DocumentType;
        merged.Language = string.IsNullOrEmpty(primary.Language) || primary.Language == "en" ? fallback.Language : primary.Language;
        merged.Categories = MergeArrays(primary.Categories, fallback.Categories);

        // SchemaSpecificData 병합
        foreach (var kvp in fallback.SchemaSpecificData)
        {
            if (!primary.SchemaSpecificData.ContainsKey(kvp.Key))
            {
                merged.SchemaSpecificData[kvp.Key] = kvp.Value;
            }
        }
        foreach (var kvp in primary.SchemaSpecificData)
        {
            merged.SchemaSpecificData[kvp.Key] = kvp.Value;
        }

        // 신뢰도 병합 (가중 평균)
        merged.OverallConfidence = (primary.OverallConfidence * 0.7f) + (fallback.OverallConfidence * 0.3f);

        // FieldConfidence 병합
        foreach (var kvp in primary.FieldConfidence)
        {
            merged.FieldConfidence[kvp.Key] = kvp.Value;
        }
        foreach (var kvp in fallback.FieldConfidence)
        {
            if (!merged.FieldConfidence.ContainsKey(kvp.Key))
            {
                merged.FieldConfidence[kvp.Key] = kvp.Value * 0.8f; // 폴백 신뢰도는 낮춤
            }
        }

        // FieldSources 추적
        foreach (var field in new[] { "topics", "keywords", "description", "documentType", "language", "categories" })
        {
            merged.FieldSources[field] = MetadataSource.Merged;
        }

        if (_logger.IsEnabled(LogLevel.Debug))
            LogRuleBasedMetadata1(_logger, primary.ExtractionMethod, fallback.ExtractionMethod);

        return merged;
    }

    // ===================================================================
    // Private: Schema-specific extraction methods
    // ===================================================================

    private static ExtractedMetadata ExtractProductManualMetadata(string content)
    {
        var metadata = new ExtractedMetadata
        {
            DocumentType = "manual",
            Source = MetadataSource.RuleBased
        };

        if (string.IsNullOrWhiteSpace(content))
            return metadata;

        // Product name patterns
        var productPatterns = new[]
        {
            @"([A-Za-z0-9\s\-]+)\s+(Manual|User\s+Guide|Guide|Instructions)",
            @"^([A-Z][A-Za-z0-9\s\-]+)\s*\r?\n",
            @"Product:\s*([A-Za-z0-9\s\-]+)",
            @"Model:\s*([A-Za-z0-9\s\-]+)"
        };

        foreach (var pattern in productPatterns)
        {
            var match = Regex.Match(content, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (match.Success && match.Groups.Count > 1)
            {
                metadata.SchemaSpecificData["productName"] = match.Groups[1].Value.Trim();
                metadata.FieldConfidence["productName"] = 0.85f;
                break;
            }
        }

        // Company name patterns
        var companyPatterns = new[]
        {
            @"©\s*\d{4}\s+([A-Z][A-Za-z\s]+?)(?:\s+Inc\.|Corporation|Ltd\.|LLC|Co\.|,|$)",
            @"Copyright\s+(?:\d{4}\s+)?([A-Z][A-Za-z\s]+?)(?:\s+Inc\.|Corporation|Ltd\.|LLC|Co\.|,|$)",
            @"Manufacturer:\s*([A-Za-z\s]+)",
            @"Company:\s*([A-Za-z\s]+)"
        };

        foreach (var pattern in companyPatterns)
        {
            var match = Regex.Match(content, pattern, RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 1)
            {
                metadata.SchemaSpecificData["company"] = match.Groups[1].Value.Trim();
                metadata.FieldConfidence["company"] = 0.80f;
                break;
            }
        }

        // Version patterns
        var versionPatterns = new[]
        {
            @"(?:Version|Ver\.|v)\s*(\d+\.\d+(?:\.\d+)?)",
            @"(?:Firmware|Software)\s+(?:Version\s+)?(\d+\.\d+(?:\.\d+)?)",
            @"(?:iOS|Android|Windows)\s+(\d+(?:\.\d+)?)",
            @"Rev(?:ision)?\s*(\d+(?:\.\d+)?)"
        };

        foreach (var pattern in versionPatterns)
        {
            var match = Regex.Match(content, pattern, RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 1)
            {
                metadata.SchemaSpecificData["version"] = match.Groups[1].Value;
                metadata.FieldConfidence["version"] = 0.90f;
                break;
            }
        }

        // Release date patterns
        var datePatterns = new[]
        {
            @"(?:Released?|Published?|Date):\s*(\d{1,2}[\/-]\d{1,2}[\/-]\d{4})",
            @"(?:Released?|Published?|Date):\s*([A-Za-z]+\s+\d{1,2},?\s+\d{4})",
            @"(\d{4}-\d{2}-\d{2})"
        };

        foreach (var pattern in datePatterns)
        {
            var match = Regex.Match(content, pattern, RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 1)
            {
                if (DateTime.TryParse(match.Groups[1].Value, out var date))
                {
                    metadata.SchemaSpecificData["releaseDate"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    metadata.FieldConfidence["releaseDate"] = 0.75f;
                }
                break;
            }
        }

        // Extract topics and keywords
        metadata.Topics = ExtractTopicsFromHeaders(content);
        metadata.Keywords = ExtractKeywords(content, isManual: true);
        metadata.Description = ExtractDescription(content);
        metadata.Language = DetectLanguage(content);

        metadata.OverallConfidence = CalculateConfidence(metadata);

        return metadata;
    }

    private static ExtractedMetadata ExtractTechnicalDocMetadata(string content)
    {
        var metadata = new ExtractedMetadata
        {
            DocumentType = "documentation",
            Source = MetadataSource.RuleBased
        };

        if (string.IsNullOrWhiteSpace(content))
            return metadata;

        // Libraries and packages
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var libPatterns = new[]
        {
            @"import\s+([a-zA-Z0-9_\.@\/\-]+)",
            @"from\s+([a-zA-Z0-9_\.]+)\s+import",
            @"using\s+([a-zA-Z0-9_\.]+);",
            @"require\(['""]([^'""]+)['""]\)",
            @"#include\s+[<""]([^>""]+)[>""]",
            @"use\s+([a-zA-Z0-9_\\]+);",
            @"import\s+\{[^}]+\}\s+from\s+['""]([^'""]+)['""]"
        };

        foreach (var pattern in libPatterns)
        {
            var matches = Regex.Matches(content, pattern);
            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    var lib = match.Groups[1].Value;
                    if (!lib.StartsWith('.') && !lib.StartsWith('/') && lib.Length > 2)
                    {
                        libraries.Add(lib);
                    }
                }
            }
        }

        if (libraries.Count != 0)
        {
            metadata.SchemaSpecificData["libraries"] = libraries.Take(15).ToArray();
            metadata.FieldConfidence["libraries"] = 0.90f;
        }

        // Frameworks
        var frameworkKeywords = new[]
        {
            "React", "Vue", "Angular", "Svelte", "Next.js", "Nuxt",
            "Express", "FastAPI", "Django", "Flask", "Spring", "ASP.NET",
            "TensorFlow", "PyTorch", "scikit-learn",
            "Docker", "Kubernetes", "AWS", "Azure", "GCP"
        };

        var foundFrameworks = frameworkKeywords
            .Where(fw => content.Contains(fw, StringComparison.OrdinalIgnoreCase))
            .Take(10)
            .ToArray();

        if (foundFrameworks.Length != 0)
        {
            metadata.SchemaSpecificData["frameworks"] = foundFrameworks;
            metadata.FieldConfidence["frameworks"] = 0.85f;
        }

        // Technologies
        var techKeywords = new[]
        {
            "JavaScript", "TypeScript", "Python", "C#", "Java", "Go", "Rust", "C++",
            "API", "REST", "GraphQL", "gRPC", "WebSocket",
            "SQL", "NoSQL", "MongoDB", "PostgreSQL", "MySQL", "Redis",
            "HTML", "CSS", "SCSS", "Tailwind"
        };

        var foundTech = techKeywords
            .Where(tech => content.Contains(tech, StringComparison.OrdinalIgnoreCase))
            .Take(10)
            .ToArray();

        if (foundTech.Length != 0)
        {
            metadata.SchemaSpecificData["technologies"] = foundTech;
            metadata.FieldConfidence["technologies"] = 0.80f;
        }

        metadata.Topics = ExtractTopicsFromHeaders(content);
        metadata.Keywords = ExtractKeywords(content, isTechnical: true);
        metadata.Description = ExtractDescription(content);
        metadata.Language = DetectLanguage(content);

        metadata.OverallConfidence = CalculateConfidence(metadata);

        return metadata;
    }

    private static ExtractedMetadata ExtractArticleMetadata(string content)
    {
        var metadata = new ExtractedMetadata
        {
            DocumentType = "article",
            Source = MetadataSource.RuleBased
        };

        if (string.IsNullOrWhiteSpace(content))
            return metadata;

        // Author patterns
        var authorPatterns = new[]
        {
            @"(?:Author|By|Written\s+by):\s*([A-Z][a-z]+\s+[A-Z][a-z]+)",
            @"(?:Author|By):\s*([A-Za-z\s]+)",
            @"^By\s+([A-Z][a-z]+\s+[A-Z][a-z]+)"
        };

        foreach (var pattern in authorPatterns)
        {
            var match = Regex.Match(content, pattern, RegexOptions.Multiline | RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 1)
            {
                metadata.SchemaSpecificData["author"] = match.Groups[1].Value.Trim();
                metadata.FieldConfidence["author"] = 0.85f;
                break;
            }
        }

        // Published date
        var datePatterns = new[]
        {
            @"(?:Published|Posted|Date):\s*(\d{1,2}[\/-]\d{1,2}[\/-]\d{4})",
            @"(?:Published|Posted|Date):\s*([A-Za-z]+\s+\d{1,2},?\s+\d{4})",
            @"(\d{4}-\d{2}-\d{2})"
        };

        foreach (var pattern in datePatterns)
        {
            var match = Regex.Match(content, pattern, RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 1)
            {
                if (DateTime.TryParse(match.Groups[1].Value, out var date))
                {
                    metadata.SchemaSpecificData["publishedDate"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    metadata.FieldConfidence["publishedDate"] = 0.80f;
                }
                break;
            }
        }

        // Reading time estimation (rough: 200 words/min)
        var wordCount = content.Split(WordSplitSeparators, StringSplitOptions.RemoveEmptyEntries).Length;
        var readingTimeMinutes = Math.Max(1, wordCount / 200);
        metadata.SchemaSpecificData["readingTimeMinutes"] = readingTimeMinutes;

        metadata.Topics = ExtractTopicsFromHeaders(content);
        metadata.Keywords = ExtractKeywords(content);
        metadata.Description = ExtractDescription(content);
        metadata.Language = DetectLanguage(content);

        // Tags (from keywords)
        if (metadata.Keywords.Length > 0)
        {
            metadata.SchemaSpecificData["tags"] = metadata.Keywords.Take(5).ToArray();
        }

        metadata.OverallConfidence = CalculateConfidence(metadata);

        return metadata;
    }

    private static ExtractedMetadata ExtractGeneralMetadata(string content)
    {
        var metadata = new ExtractedMetadata
        {
            Source = MetadataSource.RuleBased
        };

        if (string.IsNullOrWhiteSpace(content))
            return metadata;

        metadata.Topics = ExtractTopicsFromHeaders(content);
        metadata.Keywords = ExtractKeywords(content);
        metadata.Description = ExtractDescription(content);
        metadata.DocumentType = DetectDocumentType(content);
        metadata.Language = DetectLanguage(content);

        metadata.OverallConfidence = CalculateConfidence(metadata);

        return metadata;
    }

    // ===================================================================
    // Private: Helper methods
    // ===================================================================

    private static string[] ExtractTopicsFromHeaders(string content)
    {
        var topics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Markdown headers
        var mdHeaders = Regex.Matches(content, @"^#{1,3}\s+(.+)$", RegexOptions.Multiline);
        foreach (Match match in mdHeaders)
        {
            if (match.Groups.Count > 1)
            {
                var header = match.Groups[1].Value.Trim();
                if (header.Length > 3 && header.Length < 60)
                {
                    topics.Add(header);
                }
            }
        }

        // Numbered sections
        var numberedSections = Regex.Matches(content, @"^\d+[\.\)]\s+([A-Z][^\r\n]{3,60})", RegexOptions.Multiline);
        foreach (Match match in numberedSections)
        {
            if (match.Groups.Count > 1)
            {
                topics.Add(match.Groups[1].Value.Trim());
            }
        }

        var result = topics.Take(5).ToArray();
        if (result.Length > 0)
        {
            // Set field-level confidence
            // Note: Will be added to FieldConfidence in calling method if needed
        }

        return result;
    }

    private static string[] ExtractKeywords(string content, bool isManual = false, bool isTechnical = false)
    {
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "is", "at", "which", "on", "a", "an", "and", "or", "but",
            "in", "with", "to", "for", "of", "as", "by", "this", "that", "these", "those"
        };

        var words = Regex.Split(content, @"\W+")
            .Where(w => w.Length > 3 && w.Length < 30)
            .Where(w => !stopWords.Contains(w))
            .Where(w => !Regex.IsMatch(w, @"^\d+$"));

        var wordCounts = words
            .GroupBy(w => w.ToLowerInvariant())
            .OrderByDescending(g => g.Count())
            .Take(20);

        foreach (var group in wordCounts)
        {
            keywords.Add(group.Key);
        }

        return keywords.Take(10).ToArray();
    }

    private static string ExtractDescription(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        var paragraphs = content.Split(ParagraphSplitSeparators, StringSplitOptions.RemoveEmptyEntries);

        foreach (var para in paragraphs)
        {
            var trimmed = para.Trim();
            if (trimmed.Length > 50 && trimmed.Length < 300 &&
                !trimmed.StartsWith('#') &&
                !Regex.IsMatch(trimmed, @"^[\d\.\)]+\s"))
            {
                var sentences = Regex.Split(trimmed, @"(?<=[.!?])\s+");
                if (sentences.Length > 0)
                {
                    var desc = sentences[0].Trim();
                    if (desc.Length > 20)
                    {
                        return desc.Length > 200 ? string.Concat(desc.AsSpan(0, 200), "...") : desc;
                    }
                }
            }
        }

        return string.Empty;
    }

    private static string DetectDocumentType(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "unknown";

        var lowerContent = content.ToLowerInvariant();

        if (lowerContent.Contains("user guide") || lowerContent.Contains("manual") ||
            lowerContent.Contains("instructions") || lowerContent.Contains("how to use"))
            return "manual";

        if (lowerContent.Contains("tutorial") || lowerContent.Contains("getting started") ||
            lowerContent.Contains("step by step") || lowerContent.Contains("walkthrough"))
            return "tutorial";

        if (lowerContent.Contains("api reference") || lowerContent.Contains("documentation") ||
            lowerContent.Contains("specification") || lowerContent.Contains("reference guide"))
            return "reference";

        if (lowerContent.Contains("guide") || lowerContent.Contains("overview") ||
            lowerContent.Contains("introduction"))
            return "guide";

        if (lowerContent.Contains("abstract") || lowerContent.Contains("conclusion") ||
            lowerContent.Contains("methodology"))
            return "article";

        return "document";
    }

    private static string DetectLanguage(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "unknown";

        // Korean
        if (content.Any(c => c >= 0xAC00 && c <= 0xD7AF))
            return "ko";

        // Chinese
        if (content.Any(c => c >= 0x4E00 && c <= 0x9FFF))
            return "zh";

        // Japanese
        if (content.Any(c => (c >= 0x3040 && c <= 0x309F) || (c >= 0x30A0 && c <= 0x30FF)))
            return "ja";

        return "en";
    }

    private static float CalculateConfidence(ExtractedMetadata metadata)
    {
        var score = 0.0f;
        var maxScore = 0.0f;

        // Core fields
        if (metadata.Topics.Length > 0) { score += 0.20f; maxScore += 0.20f; }
        if (metadata.Keywords.Length > 0) { score += 0.20f; maxScore += 0.20f; }
        if (!string.IsNullOrEmpty(metadata.Description)) { score += 0.15f; maxScore += 0.15f; }
        if (!string.IsNullOrEmpty(metadata.DocumentType)) { score += 0.10f; maxScore += 0.10f; }
        if (!string.IsNullOrEmpty(metadata.Language)) { score += 0.05f; maxScore += 0.05f; }

        // Schema-specific fields
        if (metadata.SchemaSpecificData.ContainsKey("productName")) { score += 0.10f; maxScore += 0.10f; }
        if (metadata.SchemaSpecificData.ContainsKey("company")) { score += 0.05f; maxScore += 0.05f; }
        if (metadata.SchemaSpecificData.ContainsKey("version")) { score += 0.05f; maxScore += 0.05f; }
        if (metadata.SchemaSpecificData.ContainsKey("libraries")) { score += 0.05f; maxScore += 0.05f; }
        if (metadata.SchemaSpecificData.ContainsKey("frameworks")) { score += 0.03f; maxScore += 0.03f; }
        if (metadata.SchemaSpecificData.ContainsKey("technologies")) { score += 0.02f; maxScore += 0.02f; }

        maxScore = Math.Max(maxScore, 0.01f);

        return Math.Min(score / maxScore, 1.0f);
    }

    private static string[] MergeArrays(string[] primary, string[] fallback)
    {
        var merged = new HashSet<string>(primary, StringComparer.OrdinalIgnoreCase);
        foreach (var item in fallback)
        {
            merged.Add(item);
        }
        return merged.ToArray();
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Starting rule-based extraction with schema: {Schema}")]
    private static partial void LogRuleBasedMetadata3(ILogger logger, MetadataSchema schema);
    [LoggerMessage(Level = LogLevel.Information, Message = "Rule-based extraction complete. Confidence: {Confidence}")]
    private static partial void LogRuleBasedMetadata2(ILogger logger, double confidence);
    [LoggerMessage(Level = LogLevel.Information, Message = "Merged metadata from {PrimaryMethod} and {FallbackMethod}")]
    private static partial void LogRuleBasedMetadata1(ILogger logger, string primaryMethod, string fallbackMethod);

    #endregion
}
