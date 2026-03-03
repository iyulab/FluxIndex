using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// Advanced entity extraction service implementation.
/// Uses regex patterns for common entities and optional LLM for complex extraction.
/// Foundation for GraphRAG entity graph construction.
/// </summary>
public partial class EntityExtractionService : IAdvancedEntityExtractionService
{
    private readonly ITextCompletionService? _llmService;
    private readonly ILogger<EntityExtractionService> _logger;

    // Pre-compiled regex patterns for entity extraction
    private static readonly Dictionary<NamedEntityType, Regex> EntityPatterns = new()
    {
        // Email pattern
        [NamedEntityType.Email] = new Regex(
            @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase),

        // URL pattern
        [NamedEntityType.Url] = new Regex(
            @"https?://[^\s<>""']+|www\.[^\s<>""']+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase),

        // Phone number patterns (various formats)
        [NamedEntityType.PhoneNumber] = new Regex(
            @"(\+?[0-9]{1,3}[-.\s]?)?\(?[0-9]{2,4}\)?[-.\s]?[0-9]{3,4}[-.\s]?[0-9]{3,4}",
            RegexOptions.Compiled),

        // Date patterns
        [NamedEntityType.DateTime] = new Regex(
            @"\b(\d{1,2}[-/]\d{1,2}[-/]\d{2,4}|\d{4}[-/]\d{1,2}[-/]\d{1,2}|" +
            @"(?:January|February|March|April|May|June|July|August|September|October|November|December|" +
            @"Jan|Feb|Mar|Apr|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+\d{1,2},?\s+\d{4}|\d{1,2}\s+" +
            @"(?:January|February|March|April|May|June|July|August|September|October|November|December|" +
            @"Jan|Feb|Mar|Apr|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+\d{4})\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase),

        // Money pattern
        [NamedEntityType.Money] = new Regex(
            @"[$€£¥₩]\s*\d+(?:,\d{3})*(?:\.\d{2})?|\d+(?:,\d{3})*(?:\.\d{2})?\s*(?:dollars?|euros?|pounds?|yen|won|USD|EUR|GBP|JPY|KRW)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase),

        // Percentage pattern
        [NamedEntityType.Percentage] = new Regex(
            @"\b\d+(?:\.\d+)?%|\b\d+(?:\.\d+)?\s*percent\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase),

        // Technology/Software patterns (common programming languages and frameworks)
        [NamedEntityType.Technology] = new Regex(
            @"\b(?:Python|JavaScript|TypeScript|Java|C#|C\+\+|Go|Rust|Ruby|PHP|Swift|Kotlin|Scala|" +
            @"React|Angular|Vue|Next\.js|Node\.js|Django|Flask|Spring|ASP\.NET|Laravel|Rails|" +
            @"Docker|Kubernetes|AWS|Azure|GCP|PostgreSQL|MySQL|MongoDB|Redis|Elasticsearch)\b",
            RegexOptions.Compiled),

        // Quantity pattern
        [NamedEntityType.Quantity] = new Regex(
            @"\b\d+(?:\.\d+)?\s*(?:kg|g|mg|lb|oz|km|m|cm|mm|mi|ft|in|L|mL|gal|" +
            @"bytes?|KB|MB|GB|TB|PB|bps|Mbps|Gbps|Hz|kHz|MHz|GHz)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase)
    };

    // Capitalized word sequences pattern for potential named entities
    private static readonly Regex CapitalizedSequencePattern = new(
        @"\b([A-Z][a-z]+(?:\s+[A-Z][a-z]+)+)\b",
        RegexOptions.Compiled);

    // Organization indicators
    private static readonly Regex OrganizationSuffixPattern = new(
        @"\b[A-Z][A-Za-z]+(?:\s+[A-Z][A-Za-z]+)*\s+(?:Inc\.?|Corp\.?|LLC|Ltd\.?|Company|Co\.?|Group|Foundation|Institute|University|College|Association|Organization|Agency|Department|Ministry)\b",
        RegexOptions.Compiled);

    public EntityExtractionService(
        ILogger<EntityExtractionService> logger,
        ITextCompletionService? llmService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _llmService = llmService;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExtractedEntity>> ExtractEntitiesAsync(
        string content,
        EntityExtractionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Array.Empty<ExtractedEntity>();
        }

        options ??= new EntityExtractionOptions();
        var stopwatch = Stopwatch.StartNew();
        var entities = new List<ExtractedEntity>();

        try
        {
            // Step 1: Extract entities using regex patterns
            var regexEntities = ExtractWithPatterns(content, options);
            entities.AddRange(regexEntities);

            // Step 2: Extract capitalized sequences for potential named entities
            var capitalizedEntities = ExtractCapitalizedSequences(content, options);
            entities.AddRange(capitalizedEntities);

            // Step 3: Use LLM for complex entity extraction if enabled
            if (options.UseLlm && _llmService != null)
            {
                var llmEntities = await ExtractWithLlmAsync(content, options, cancellationToken);
                entities.AddRange(llmEntities);
            }

            // Step 4: Deduplicate and merge overlapping entities
            entities = DeduplicateEntities(entities, content);

            // Step 5: Filter by options
            if (options.EntityTypes?.Count > 0)
            {
                entities = entities.Where(e => options.EntityTypes.Contains(e.Type)).ToList();
            }

            entities = entities
                .Where(e => e.Confidence >= options.MinConfidence)
                .OrderByDescending(e => e.Confidence)
                .Take(options.MaxEntities)
                .ToList();

            stopwatch.Stop();
            if (_logger.IsEnabled(LogLevel.Debug))
                LogEntityExtraction6(_logger, entities.Count, stopwatch.ElapsedMilliseconds, options.UseLlm && _llmService != null);

            return entities;
        }
        catch (Exception ex)
        {
            LogEntityExtraction5(_logger, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EntityRelation>> ExtractRelationsAsync(
        string content,
        IReadOnlyList<ExtractedEntity>? entities = null,
        EntityExtractionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Array.Empty<EntityRelation>();
        }

        options ??= new EntityExtractionOptions();

        // Extract entities first if not provided
        entities ??= await ExtractEntitiesAsync(content, options, cancellationToken);

        if (entities.Count < 2)
        {
            return Array.Empty<EntityRelation>();
        }

        var relations = new List<EntityRelation>();

        // Pattern-based relation extraction
        var patternRelations = ExtractRelationsWithPatterns(content, entities);
        relations.AddRange(patternRelations);

        // LLM-based relation extraction if enabled
        if (options.UseLlm && _llmService != null)
        {
            var llmRelations = await ExtractRelationsWithLlmAsync(content, entities, cancellationToken);
            relations.AddRange(llmRelations);
        }

        // Deduplicate relations
        relations = DeduplicateRelations(relations);

        return relations;
    }

    /// <inheritdoc />
    public async Task<EntityGraph> ExtractEntityGraphAsync(
        string content,
        EntityExtractionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        options ??= new EntityExtractionOptions();

        var entities = await ExtractEntitiesAsync(content, options, cancellationToken);
        var relations = options.ExtractRelations
            ? await ExtractRelationsAsync(content, entities, options, cancellationToken)
            : Array.Empty<EntityRelation>();

        stopwatch.Stop();

        var stats = new EntityExtractionStats
        {
            TotalEntities = entities.Count,
            EntitiesByType = entities.GroupBy(e => e.Type).ToDictionary(g => g.Key, g => g.Count()),
            TotalRelations = relations.Count,
            ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
            UsedLlm = options.UseLlm && _llmService != null
        };

        return new EntityGraph
        {
            SourceId = Guid.NewGuid().ToString(),
            Entities = entities,
            Relations = relations,
            Stats = stats
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EntityGraph>> ExtractBatchAsync(
        IEnumerable<string> contents,
        EntityExtractionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var contentList = contents.ToList();
        var results = new List<EntityGraph>();

        foreach (var content in contentList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var graph = await ExtractEntityGraphAsync(content, options, cancellationToken);
            results.Add(graph);
        }

        return results;
    }

    /// <inheritdoc />
    public Task<LinkedEntityGraph> LinkEntitiesAsync(
        IEnumerable<EntityGraph> entityGraphs,
        EntityLinkingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        options ??= new EntityLinkingOptions();

        var graphs = entityGraphs.ToList();
        var allEntities = graphs.SelectMany(g => g.Entities).ToList();
        var allRelations = graphs.SelectMany(g => g.Relations).ToList();

        // Group entities by normalized text and type for linking
        var linkedEntities = new List<LinkedEntity>();
        var entityGroups = allEntities
            .GroupBy(e => options.RequireSameType
                ? (e.NormalizedText.ToLowerInvariant(), e.Type)
                : (e.NormalizedText.ToLowerInvariant(), NamedEntityType.Unknown));

        foreach (var group in entityGroups)
        {
            var groupEntities = group.ToList();
            var canonical = groupEntities
                .OrderByDescending(e => e.Confidence)
                .ThenByDescending(e => e.OccurrenceCount)
                .First();

            var linkedEntity = new LinkedEntity
            {
                CanonicalId = Guid.NewGuid().ToString(),
                CanonicalText = canonical.NormalizedText,
                Type = canonical.Type,
                SurfaceForms = groupEntities.Select(e => e.Text).Distinct().ToList(),
                MergedEntityIds = groupEntities.Select(e => e.Id).ToList(),
                SourceIds = groupEntities.Select(e => e.SourceId).Where(s => s != null).Distinct().ToList()!,
                TotalOccurrences = groupEntities.Sum(e => e.OccurrenceCount),
                ImportanceScore = CalculateImportanceScore(groupEntities, allRelations)
            };

            linkedEntities.Add(linkedEntity);
        }

        // Update relation entity IDs to canonical IDs
        var entityIdMap = new Dictionary<string, string>();
        foreach (var linked in linkedEntities)
        {
            foreach (var originalId in linked.MergedEntityIds)
            {
                entityIdMap[originalId] = linked.CanonicalId;
            }
        }

        var linkedRelations = allRelations
            .Select(r => new EntityRelation
            {
                Id = r.Id,
                SourceEntityId = entityIdMap.GetValueOrDefault(r.SourceEntityId, r.SourceEntityId),
                TargetEntityId = entityIdMap.GetValueOrDefault(r.TargetEntityId, r.TargetEntityId),
                Type = r.Type,
                Label = r.Label,
                Confidence = r.Confidence,
                IsDirectional = r.IsDirectional,
                Evidence = r.Evidence,
                SourceId = r.SourceId,
                Metadata = r.Metadata
            })
            .ToList();

        // Deduplicate relations after linking
        linkedRelations = DeduplicateRelations(linkedRelations);

        stopwatch.Stop();

        var stats = new EntityLinkingStats
        {
            OriginalEntityCount = allEntities.Count,
            LinkedEntityCount = linkedEntities.Count,
            MergeCount = allEntities.Count - linkedEntities.Count,
            ProcessingTimeMs = stopwatch.ElapsedMilliseconds
        };

        return Task.FromResult(new LinkedEntityGraph
        {
            Entities = linkedEntities.OrderByDescending(e => e.ImportanceScore).ToList(),
            Relations = linkedRelations,
            SourceIds = graphs.Select(g => g.SourceId).ToList(),
            Stats = stats
        });
    }

    #region Private Methods

    private static List<ExtractedEntity> ExtractWithPatterns(string content, EntityExtractionOptions options)
    {
        var entities = new List<ExtractedEntity>();

        foreach (var (entityType, pattern) in EntityPatterns)
        {
            if (options.EntityTypes?.Count > 0 && !options.EntityTypes.Contains(entityType))
            {
                continue;
            }

            var matches = pattern.Matches(content);
            foreach (Match match in matches)
            {
                var context = options.IncludeContext
                    ? ExtractContext(content, match.Index, match.Length, options.ContextWindowSize)
                    : null;

                entities.Add(new ExtractedEntity
                {
                    Text = match.Value,
                    NormalizedText = NormalizeEntityText(match.Value, entityType),
                    Type = entityType,
                    Confidence = 0.9, // High confidence for pattern matches
                    StartPosition = match.Index,
                    EndPosition = match.Index + match.Length,
                    Context = context,
                    OccurrenceCount = 1
                });
            }
        }

        return entities;
    }

    private static List<ExtractedEntity> ExtractCapitalizedSequences(string content, EntityExtractionOptions options)
    {
        var entities = new List<ExtractedEntity>();

        // Extract organization-like entities
        var orgMatches = OrganizationSuffixPattern.Matches(content);
        foreach (Match match in orgMatches)
        {
            var context = options.IncludeContext
                ? ExtractContext(content, match.Index, match.Length, options.ContextWindowSize)
                : null;

            entities.Add(new ExtractedEntity
            {
                Text = match.Value,
                NormalizedText = match.Value.Trim(),
                Type = NamedEntityType.Organization,
                Confidence = 0.85,
                StartPosition = match.Index,
                EndPosition = match.Index + match.Length,
                Context = context,
                OccurrenceCount = 1
            });
        }

        // Extract other capitalized sequences (potential names, locations, etc.)
        var capMatches = CapitalizedSequencePattern.Matches(content);
        foreach (Match match in capMatches)
        {
            // Skip if already matched as organization
            if (entities.Any(e => e.StartPosition <= match.Index && e.EndPosition >= match.Index + match.Length))
            {
                continue;
            }

            // Skip common phrases that aren't entities
            var text = match.Value;
            if (IsCommonPhrase(text))
            {
                continue;
            }

            var context = options.IncludeContext
                ? ExtractContext(content, match.Index, match.Length, options.ContextWindowSize)
                : null;

            // Heuristic entity type classification
            var entityType = ClassifyCapitalizedSequence(text, context);

            entities.Add(new ExtractedEntity
            {
                Text = text,
                NormalizedText = text.Trim(),
                Type = entityType,
                Confidence = 0.6, // Lower confidence for heuristic extraction
                StartPosition = match.Index,
                EndPosition = match.Index + match.Length,
                Context = context,
                OccurrenceCount = 1
            });
        }

        return entities;
    }

    private async Task<List<ExtractedEntity>> ExtractWithLlmAsync(
        string content,
        EntityExtractionOptions options,
        CancellationToken cancellationToken)
    {
        if (_llmService == null)
        {
            return new List<ExtractedEntity>();
        }

        try
        {
            var prompt = BuildEntityExtractionPrompt(content, options);
            var response = await _llmService.CompleteAsync(
                prompt, new Flux.Abstractions.TextCompletionOptions { MaxTokens = 2000, Temperature = 0.1f }, cancellationToken);
            return ParseLlmEntityResponse(response, content, options);
        }
        catch (Exception ex)
        {
            LogEntityExtraction4(_logger, ex);
            return new List<ExtractedEntity>();
        }
    }

    private static List<EntityRelation> ExtractRelationsWithPatterns(
        string content,
        IReadOnlyList<ExtractedEntity> entities)
    {
        var relations = new List<EntityRelation>();

        // Simple co-occurrence based relation detection
        var sentences = SplitIntoSentences(content);

        foreach (var sentence in sentences)
        {
            var sentenceEntities = entities
                .Where(e => sentence.Contains(e.Text, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Create relations between entities in the same sentence
            for (int i = 0; i < sentenceEntities.Count; i++)
            {
                for (int j = i + 1; j < sentenceEntities.Count; j++)
                {
                    var source = sentenceEntities[i];
                    var target = sentenceEntities[j];

                    // Determine relation type based on entity types and sentence context
                    var relationType = InferRelationType(source, target, sentence);

                    relations.Add(new EntityRelation
                    {
                        SourceEntityId = source.Id,
                        TargetEntityId = target.Id,
                        Type = relationType,
                        Label = $"{source.Text} -> {target.Text}",
                        Confidence = 0.5, // Lower confidence for pattern-based relations
                        Evidence = sentence,
                        IsDirectional = true
                    });
                }
            }
        }

        return relations;
    }

    private async Task<List<EntityRelation>> ExtractRelationsWithLlmAsync(
        string content,
        IReadOnlyList<ExtractedEntity> entities,
        CancellationToken cancellationToken)
    {
        if (_llmService == null || entities.Count < 2)
        {
            return new List<EntityRelation>();
        }

        try
        {
            var prompt = BuildRelationExtractionPrompt(content, entities);
            var response = await _llmService.CompleteAsync(
                prompt, new Flux.Abstractions.TextCompletionOptions { MaxTokens = 1500, Temperature = 0.1f }, cancellationToken);
            return ParseLlmRelationResponse(response, entities);
        }
        catch (Exception ex)
        {
            LogEntityExtraction3(_logger, ex);
            return new List<EntityRelation>();
        }
    }

    private static List<ExtractedEntity> DeduplicateEntities(List<ExtractedEntity> entities, string content)
    {
        // Group by normalized text and type, merge occurrences
        var grouped = entities
            .GroupBy(e => (e.NormalizedText.ToLowerInvariant(), e.Type))
            .Select(g =>
            {
                var items = g.OrderByDescending(e => e.Confidence).ToList();
                var primary = items.First();

                return new ExtractedEntity
                {
                    Id = primary.Id,
                    Text = primary.Text,
                    NormalizedText = primary.NormalizedText,
                    Type = primary.Type,
                    Confidence = items.Max(e => e.Confidence),
                    StartPosition = items.Min(e => e.StartPosition),
                    EndPosition = items.Max(e => e.EndPosition),
                    Context = primary.Context,
                    OccurrenceCount = items.Count,
                    Occurrences = items.Select(e => new EntityOccurrence
                    {
                        StartPosition = e.StartPosition,
                        EndPosition = e.EndPosition,
                        SentenceIndex = 0 // Could be enhanced
                    }).ToList(),
                    Metadata = primary.Metadata
                };
            })
            .ToList();

        return grouped;
    }

    private static List<EntityRelation> DeduplicateRelations(List<EntityRelation> relations)
    {
        return relations
            .GroupBy(r => (r.SourceEntityId, r.TargetEntityId, r.Type))
            .Select(g =>
            {
                var items = g.ToList();
                var primary = items.OrderByDescending(r => r.Confidence).First();
                return new EntityRelation
                {
                    Id = primary.Id,
                    SourceEntityId = primary.SourceEntityId,
                    TargetEntityId = primary.TargetEntityId,
                    Type = primary.Type,
                    Label = primary.Label,
                    Confidence = items.Max(r => r.Confidence),
                    IsDirectional = primary.IsDirectional,
                    Evidence = primary.Evidence,
                    SourceId = primary.SourceId,
                    Metadata = primary.Metadata
                };
            })
            .ToList();
    }

    private static string ExtractContext(string content, int start, int length, int windowSize)
    {
        var contextStart = Math.Max(0, start - windowSize);
        var contextEnd = Math.Min(content.Length, start + length + windowSize);
        return content.Substring(contextStart, contextEnd - contextStart);
    }

    private static string NormalizeEntityText(string text, NamedEntityType type)
    {
        // Basic normalization
        var normalized = text.Trim();

        return type switch
        {
            NamedEntityType.Email => normalized.ToLowerInvariant(),
            NamedEntityType.Url => normalized.ToLowerInvariant(),
            _ => normalized
        };
    }

    private static bool IsCommonPhrase(string text)
    {
        var commonPhrases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "The", "This", "That", "These", "Those",
            "In This", "For Example", "As Well", "In Order",
            "On The", "At The", "By The", "For The",
            "However", "Therefore", "Furthermore", "Moreover"
        };

        return commonPhrases.Contains(text);
    }

    private static NamedEntityType ClassifyCapitalizedSequence(string text, string? context)
    {
        // Simple heuristics for entity type classification
        var words = text.Split(' ');

        // Check for person name patterns (typically 2-3 capitalized words)
        if (words.Length >= 2 && words.Length <= 4 && words.All(w => char.IsUpper(w[0])))
        {
            // Common person name patterns
            if (context?.Contains("said", StringComparison.OrdinalIgnoreCase) == true ||
                context?.Contains("wrote", StringComparison.OrdinalIgnoreCase) == true ||
                context?.Contains("CEO", StringComparison.OrdinalIgnoreCase) == true ||
                context?.Contains("Dr.", StringComparison.OrdinalIgnoreCase) == true ||
                context?.Contains("Mr.", StringComparison.OrdinalIgnoreCase) == true ||
                context?.Contains("Mrs.", StringComparison.OrdinalIgnoreCase) == true ||
                context?.Contains("Ms.", StringComparison.OrdinalIgnoreCase) == true)
            {
                return NamedEntityType.Person;
            }
        }

        // Check for location patterns
        if (context?.Contains("in ", StringComparison.OrdinalIgnoreCase) == true ||
            context?.Contains("at ", StringComparison.OrdinalIgnoreCase) == true ||
            context?.Contains("from ", StringComparison.OrdinalIgnoreCase) == true ||
            context?.Contains("located", StringComparison.OrdinalIgnoreCase) == true)
        {
            return NamedEntityType.Location;
        }

        // Default to concept for other capitalized sequences
        return NamedEntityType.Concept;
    }

    private static RelationType InferRelationType(ExtractedEntity source, ExtractedEntity target, string sentence)
    {
        var sentenceLower = sentence.ToLowerInvariant();

        // Check for common relation patterns
        if (sentenceLower.Contains("works for") || sentenceLower.Contains("employed by") ||
            sentenceLower.Contains("works at"))
        {
            return RelationType.WorksFor;
        }

        if (sentenceLower.Contains("located in") || sentenceLower.Contains("based in") ||
            sentenceLower.Contains("headquarters in"))
        {
            return RelationType.LocatedIn;
        }

        if (sentenceLower.Contains("founded") || sentenceLower.Contains("created"))
        {
            return RelationType.FoundedBy;
        }

        if (sentenceLower.Contains("part of") || sentenceLower.Contains("belongs to"))
        {
            return RelationType.PartOf;
        }

        if (sentenceLower.Contains("uses") || sentenceLower.Contains("utilizes"))
        {
            return RelationType.Uses;
        }

        if (sentenceLower.Contains("depends on") || sentenceLower.Contains("requires"))
        {
            return RelationType.DependsOn;
        }

        // Technology-specific relations
        if (source.Type == NamedEntityType.Technology && target.Type == NamedEntityType.Technology)
        {
            if (sentenceLower.Contains("extends") || sentenceLower.Contains("inherits"))
            {
                return RelationType.InheritsFrom;
            }

            if (sentenceLower.Contains("implements"))
            {
                return RelationType.Implements;
            }
        }

        return RelationType.RelatedTo;
    }

    private static List<string> SplitIntoSentences(string content)
    {
        // Simple sentence splitting
        return Regex.Split(content, @"(?<=[.!?])\s+")
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    private static string BuildEntityExtractionPrompt(string content, EntityExtractionOptions options)
    {
        var typesList = options.EntityTypes?.Count > 0
            ? string.Join(", ", options.EntityTypes)
            : "Person, Organization, Location, Technology, Concept, Product, Event";

        return $$"""
            Extract named entities from the following text. Return the result as a JSON array.

            Entity types to extract: {{typesList}}

            For each entity, provide:
            - text: the exact text from the content
            - type: one of the entity types listed above
            - confidence: a number between 0 and 1

            Text to analyze:
            {{content}}

            Return only the JSON array, no other text.
            Example format:
            [
              {"text": "Microsoft", "type": "Organization", "confidence": 0.95},
              {"text": "Bill Gates", "type": "Person", "confidence": 0.9}
            ]
            """;
    }

    private static string BuildRelationExtractionPrompt(string content, IReadOnlyList<ExtractedEntity> entities)
    {
        var entityList = string.Join("\n", entities.Select(e => $"- {e.Text} ({e.Type})"));

        return $$"""
            Extract relationships between the following entities found in the text.

            Entities:
            {{entityList}}

            Text:
            {{content}}

            For each relationship, provide:
            - source: the source entity text
            - target: the target entity text
            - type: one of (PartOf, LocatedIn, WorksFor, FoundedBy, Uses, DependsOn, RelatedTo)
            - confidence: a number between 0 and 1

            Return only the JSON array, no other text.
            Example format:
            [
              {"source": "Bill Gates", "target": "Microsoft", "type": "FoundedBy", "confidence": 0.95}
            ]
            """;
    }

    private List<ExtractedEntity> ParseLlmEntityResponse(
        string response,
        string content,
        EntityExtractionOptions options)
    {
        var entities = new List<ExtractedEntity>();

        try
        {
            // Simple JSON parsing (would use proper JSON library in production)
            var jsonStart = response.IndexOf('[');
            var jsonEnd = response.LastIndexOf(']');

            if (jsonStart < 0 || jsonEnd < 0)
            {
                return entities;
            }

            var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);

            // Parse using System.Text.Json
            var parsed = System.Text.Json.JsonSerializer.Deserialize<List<LlmEntityResult>>(json);

            if (parsed == null)
            {
                return entities;
            }

            foreach (var item in parsed)
            {
                if (string.IsNullOrWhiteSpace(item.Text))
                {
                    continue;
                }

                var position = content.IndexOf(item.Text, StringComparison.OrdinalIgnoreCase);
                var entityType = Enum.TryParse<NamedEntityType>(item.Type, true, out var et) ? et : NamedEntityType.Unknown;

                var context = options.IncludeContext && position >= 0
                    ? ExtractContext(content, position, item.Text.Length, options.ContextWindowSize)
                    : null;

                entities.Add(new ExtractedEntity
                {
                    Text = item.Text,
                    NormalizedText = item.Text.Trim(),
                    Type = entityType,
                    Confidence = Math.Clamp(item.Confidence, 0, 1),
                    StartPosition = position >= 0 ? position : 0,
                    EndPosition = position >= 0 ? position + item.Text.Length : 0,
                    Context = context,
                    OccurrenceCount = 1
                });
            }
        }
        catch (Exception ex)
        {
            LogEntityExtraction2(_logger, ex);
        }

        return entities;
    }

    private List<EntityRelation> ParseLlmRelationResponse(
        string response,
        IReadOnlyList<ExtractedEntity> entities)
    {
        var relations = new List<EntityRelation>();

        try
        {
            var jsonStart = response.IndexOf('[');
            var jsonEnd = response.LastIndexOf(']');

            if (jsonStart < 0 || jsonEnd < 0)
            {
                return relations;
            }

            var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
            var parsed = System.Text.Json.JsonSerializer.Deserialize<List<LlmRelationResult>>(json);

            if (parsed == null)
            {
                return relations;
            }

            foreach (var item in parsed)
            {
                var sourceEntity = entities.FirstOrDefault(e =>
                    e.Text.Equals(item.Source, StringComparison.OrdinalIgnoreCase));
                var targetEntity = entities.FirstOrDefault(e =>
                    e.Text.Equals(item.Target, StringComparison.OrdinalIgnoreCase));

                if (sourceEntity == null || targetEntity == null)
                {
                    continue;
                }

                var relationType = Enum.TryParse<RelationType>(item.Type, true, out var rt)
                    ? rt
                    : RelationType.RelatedTo;

                relations.Add(new EntityRelation
                {
                    SourceEntityId = sourceEntity.Id,
                    TargetEntityId = targetEntity.Id,
                    Type = relationType,
                    Label = $"{sourceEntity.Text} -> {targetEntity.Text}",
                    Confidence = Math.Clamp(item.Confidence, 0, 1),
                    IsDirectional = true
                });
            }
        }
        catch (Exception ex)
        {
            LogEntityExtraction1(_logger, ex);
        }

        return relations;
    }

    private static double CalculateImportanceScore(
        List<ExtractedEntity> entities,
        List<EntityRelation> allRelations)
    {
        var totalOccurrences = entities.Sum(e => e.OccurrenceCount);
        var avgConfidence = entities.Average(e => e.Confidence);

        // Count relations involving any of these entities
        var entityIds = entities.Select(e => e.Id).ToHashSet();
        var relationCount = allRelations.Count(r =>
            entityIds.Contains(r.SourceEntityId) || entityIds.Contains(r.TargetEntityId));

        // Combine factors for importance score
        return (totalOccurrences * 0.3 + avgConfidence * 0.4 + Math.Min(relationCount * 0.1, 0.3));
    }

    #endregion

    #region Helper Classes for JSON Parsing

    private sealed class LlmEntityResult
    {
        public string Text { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public double Confidence { get; set; }
    }

    private sealed class LlmRelationResult
    {
        public string Source { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public double Confidence { get; set; }
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Extracted {Count} entities from content in {Time}ms (LLM: {UsedLlm})")]
    private static partial void LogEntityExtraction6(ILogger logger, int count, long time, bool usedLlm);
    [LoggerMessage(Level = LogLevel.Error, Message = "Error extracting entities from content")]
    private static partial void LogEntityExtraction5(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Warning, Message = "LLM entity extraction failed, falling back to pattern-only extraction")]
    private static partial void LogEntityExtraction4(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Warning, Message = "LLM relation extraction failed")]
    private static partial void LogEntityExtraction3(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to parse LLM entity response")]
    private static partial void LogEntityExtraction2(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to parse LLM relation response")]
    private static partial void LogEntityExtraction1(ILogger logger, Exception exception);

    #endregion
}
