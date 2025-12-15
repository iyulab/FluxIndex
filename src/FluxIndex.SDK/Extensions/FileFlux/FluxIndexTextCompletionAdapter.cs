using FileFlux;
using FileFlux.Core;
using FileFlux.Domain;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using IFileFluxTextCompletionService = FileFlux.ITextCompletionService;
using IFluxIndexTextCompletionService = FluxIndex.Core.Application.Interfaces.ITextCompletionService;
using FileFluxQualityAssessment = FileFlux.QualityAssessment;

namespace FluxIndex.SDK.Extensions.FileFlux;

/// <summary>
/// Adapter that bridges FluxIndex's ITextCompletionService to FileFlux's ITextCompletionService interface
/// Enables FileFlux to use FluxIndex's OpenAI text completion implementation
/// </summary>
public class FluxIndexTextCompletionAdapter : IFileFluxTextCompletionService
{
    private readonly IFluxIndexTextCompletionService _fluxIndexService;
    private readonly ILogger<FluxIndexTextCompletionAdapter> _logger;

    public FluxIndexTextCompletionAdapter(
        IFluxIndexTextCompletionService fluxIndexService,
        ILogger<FluxIndexTextCompletionAdapter> logger)
    {
        _fluxIndexService = fluxIndexService ?? throw new ArgumentNullException(nameof(fluxIndexService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Provider information for FileFlux integration
    /// Updated January 2025 with GPT-5 series (released August 2025)
    /// </summary>
    public TextCompletionServiceInfo ProviderInfo => new()
    {
        Name = "FluxIndex OpenAI Adapter",
        Type = TextCompletionProviderType.OpenAI,
        SupportedModels = new[]
        {
            // GPT-5 Series (Released August 2025) - Advanced reasoning capabilities
            "gpt-5-nano",            // ⭐ Most cost-effective: $0.05/1M input, $0.40/1M output (8x cheaper than gpt-4o-mini)
            "gpt-5-mini",            // Balanced: $0.25/1M input, $2.00/1M output
            "gpt-5",                 // Flagship reasoning: $1.25/1M input, $10.00/1M output

            // GPT-4o Series (Legacy - for backward compatibility)
            "gpt-4o-mini",           // Legacy: $0.15/1M input, $0.60/1M output
            "gpt-4o",                // Legacy flagship: $2.50/1M input, $10.00/1M output
            "gpt-4o-2024-08-06"      // Legacy stable: $2.50/1M input, $10.00/1M output
        },
        MaxContextLength = 128000, // Context window for both GPT-5 and GPT-4o series
        InputTokenCost = 0.00005m, // gpt-5-nano pricing (most cost-effective)
        OutputTokenCost = 0.0004m,
        ApiVersion = "2025-08-07"  // GPT-5 release date
    };

    /// <summary>
    /// Check if the FluxIndex text completion service is available
    /// </summary>
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        // FluxIndex service is available if it's been registered in DI
        return Task.FromResult(_fluxIndexService != null);
    }

    /// <summary>
    /// Generate text completion using FluxIndex's service
    /// </summary>
    public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _fluxIndexService.GenerateCompletionAsync(
                prompt,
                maxTokens: 2000,
                temperature: 0.7f,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate text completion");
            throw;
        }
    }

    /// <summary>
    /// Analyze document structure using LLM
    /// </summary>
    public async Task<StructureAnalysisResult> AnalyzeStructureAsync(
        string prompt,
        DocumentType documentType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Analyzing document structure for type: {DocumentType}", documentType);

            // Build structured prompt for structure analysis
            var structuredPrompt = $@"{prompt}

Respond with a JSON object in this exact format:
{{
  ""documentType"": ""{documentType}"",
  ""sections"": [
    {{
      ""type"": ""section_type"",
      ""title"": ""section title"",
      ""startPosition"": 0,
      ""endPosition"": 100,
      ""level"": 1,
      ""parentId"": null,
      ""importance"": 0.8
    }}
  ],
  ""confidence"": 0.85
}}";

            var jsonResponse = await _fluxIndexService.GenerateJsonCompletionAsync(
                structuredPrompt,
                maxTokens: 4000,
                cancellationToken: cancellationToken);

            _logger.LogDebug("Structure analysis response: {Response}", jsonResponse);

            // Parse JSON response
            var result = ParseStructureAnalysis(jsonResponse, documentType);
            result.RawResponse = jsonResponse;
            result.TokensUsed = _fluxIndexService.CountTokens(prompt) + _fluxIndexService.CountTokens(jsonResponse);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze document structure");

            // Return fallback result
            return new StructureAnalysisResult
            {
                DocumentType = documentType,
                Confidence = 0.0,
                RawResponse = ex.Message,
                TokensUsed = 0
            };
        }
    }

    /// <summary>
    /// Summarize content using LLM
    /// </summary>
    public async Task<ContentSummary> SummarizeContentAsync(
        string prompt,
        int maxLength = 200,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Summarizing content with max length: {MaxLength}", maxLength);

            var structuredPrompt = $@"{prompt}

Respond with a JSON object in this exact format:
{{
  ""summary"": ""Brief summary of the content"",
  ""keywords"": [""keyword1"", ""keyword2"", ""keyword3""],
  ""confidence"": 0.9,
  ""originalLength"": {prompt.Length}
}}

Keep the summary under {maxLength} characters.";

            var jsonResponse = await _fluxIndexService.GenerateJsonCompletionAsync(
                structuredPrompt,
                maxTokens: maxLength * 2,
                cancellationToken: cancellationToken);

            _logger.LogDebug("Summary response: {Response}", jsonResponse);

            var result = ParseContentSummary(jsonResponse, prompt.Length);
            result.TokensUsed = _fluxIndexService.CountTokens(prompt) + _fluxIndexService.CountTokens(jsonResponse);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to summarize content");

            return new ContentSummary
            {
                Summary = string.Empty,
                Keywords = Array.Empty<string>(),
                Confidence = 0.0,
                OriginalLength = prompt.Length,
                TokensUsed = 0
            };
        }
    }

    /// <summary>
    /// Extract metadata from document using LLM
    /// </summary>
    public async Task<MetadataExtractionResult> ExtractMetadataAsync(
        string prompt,
        DocumentType documentType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Extracting metadata for document type: {DocumentType}", documentType);

            var structuredPrompt = $@"{prompt}

Respond with a JSON object in this exact format:
{{
  ""keywords"": [""keyword1"", ""keyword2""],
  ""language"": ""en"",
  ""categories"": [""category1"", ""category2""],
  ""entities"": {{
    ""people"": [""person1"", ""person2""],
    ""organizations"": [""org1"", ""org2""],
    ""locations"": [""location1""]
  }},
  ""technicalMetadata"": {{
    ""programmingLanguage"": ""C#"",
    ""framework"": "".NET""
  }},
  ""confidence"": 0.85
}}";

            var jsonResponse = await _fluxIndexService.GenerateJsonCompletionAsync(
                structuredPrompt,
                maxTokens: 3000,
                cancellationToken: cancellationToken);

            _logger.LogDebug("Metadata extraction response: {Response}", jsonResponse);

            var result = ParseMetadataExtraction(jsonResponse);
            result.TokensUsed = _fluxIndexService.CountTokens(prompt) + _fluxIndexService.CountTokens(jsonResponse);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract metadata");

            return new MetadataExtractionResult
            {
                Keywords = Array.Empty<string>(),
                Categories = Array.Empty<string>(),
                Confidence = 0.0,
                TokensUsed = 0
            };
        }
    }

    /// <summary>
    /// Assess quality of document chunks using LLM
    /// </summary>
    public async Task<FileFluxQualityAssessment> AssessQualityAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Assessing quality");

            var structuredPrompt = $@"{prompt}

Respond with a JSON object in this exact format:
{{
  ""confidenceScore"": 0.85,
  ""completenessScore"": 0.90,
  ""consistencyScore"": 0.88,
  ""explanation"": ""Quality assessment explanation"",
  ""recommendations"": [
    {{
      ""type"": ""CHUNK_SIZE_OPTIMIZATION"",
      ""description"": ""Recommendation description"",
      ""suggestedValue"": ""512"",
      ""priority"": 5,
      ""expectedImprovement"": 0.1,
      ""suggestedParameters"": {{
        ""chunkSize"": 512,
        ""overlapSize"": 64
      }}
    }}
  ]
}}";

            var jsonResponse = await _fluxIndexService.GenerateJsonCompletionAsync(
                structuredPrompt,
                maxTokens: 3000,
                cancellationToken: cancellationToken);

            _logger.LogDebug("Quality assessment response: {Response}", jsonResponse);

            var result = ParseQualityAssessment(jsonResponse);
            result.TokensUsed = _fluxIndexService.CountTokens(prompt) + _fluxIndexService.CountTokens(jsonResponse);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to assess quality");

            return new FileFluxQualityAssessment
            {
                ConfidenceScore = 0.0,
                CompletenessScore = 0.0,
                ConsistencyScore = 0.0,
                Explanation = "Quality assessment failed",
                TokensUsed = 0
            };
        }
    }

    #region JSON Parsing Helpers

    private StructureAnalysisResult ParseStructureAnalysis(string jsonResponse, DocumentType documentType)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonResponse);
            var root = doc.RootElement;

            var result = new StructureAnalysisResult
            {
                DocumentType = documentType,
                Confidence = root.TryGetProperty("confidence", out var confidence) ? confidence.GetDouble() : 0.0
            };

            if (root.TryGetProperty("sections", out var sectionsArray))
            {
                foreach (var section in sectionsArray.EnumerateArray())
                {
                    result.Sections.Add(new SectionInfo
                    {
                        Type = ParseSectionType(section.GetProperty("type").GetString()),
                        Title = section.GetProperty("title").GetString() ?? string.Empty,
                        StartPosition = section.GetProperty("startPosition").GetInt32(),
                        EndPosition = section.GetProperty("endPosition").GetInt32(),
                        Level = section.GetProperty("level").GetInt32(),
                        ParentId = section.TryGetProperty("parentId", out var parentId) ? parentId.GetString() : null,
                        Importance = section.TryGetProperty("importance", out var importance) ? importance.GetDouble() : 0.5
                    });
                }
            }

            result.Structure.AllSections = result.Sections;
            if (result.Sections.Count > 0)
            {
                result.Structure.Root = result.Sections[0];
            }

            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse structure analysis JSON, using fallback");
            return new StructureAnalysisResult
            {
                DocumentType = documentType,
                Confidence = 0.0
            };
        }
    }

    private ContentSummary ParseContentSummary(string jsonResponse, int originalLength)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonResponse);
            var root = doc.RootElement;

            return new ContentSummary
            {
                Summary = root.GetProperty("summary").GetString() ?? string.Empty,
                Keywords = root.TryGetProperty("keywords", out var keywords)
                    ? keywords.EnumerateArray().Select(k => k.GetString() ?? string.Empty).ToArray()
                    : Array.Empty<string>(),
                Confidence = root.TryGetProperty("confidence", out var confidence) ? confidence.GetDouble() : 0.0,
                OriginalLength = originalLength
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse content summary JSON, using fallback");
            return new ContentSummary
            {
                Summary = string.Empty,
                Keywords = Array.Empty<string>(),
                Confidence = 0.0,
                OriginalLength = originalLength
            };
        }
    }

    private MetadataExtractionResult ParseMetadataExtraction(string jsonResponse)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonResponse);
            var root = doc.RootElement;

            var result = new MetadataExtractionResult
            {
                Keywords = root.TryGetProperty("keywords", out var keywords)
                    ? keywords.EnumerateArray().Select(k => k.GetString() ?? string.Empty).ToArray()
                    : Array.Empty<string>(),
                Language = root.TryGetProperty("language", out var language) ? language.GetString() : null,
                Categories = root.TryGetProperty("categories", out var categories)
                    ? categories.EnumerateArray().Select(c => c.GetString() ?? string.Empty).ToArray()
                    : Array.Empty<string>(),
                Confidence = root.TryGetProperty("confidence", out var confidence) ? confidence.GetDouble() : 0.0
            };

            if (root.TryGetProperty("entities", out var entities))
            {
                foreach (var entity in entities.EnumerateObject())
                {
                    result.Entities[entity.Name] = entity.Value.EnumerateArray()
                        .Select(e => e.GetString() ?? string.Empty)
                        .ToArray();
                }
            }

            if (root.TryGetProperty("technicalMetadata", out var technicalMetadata))
            {
                foreach (var metadata in technicalMetadata.EnumerateObject())
                {
                    result.TechnicalMetadata[metadata.Name] = metadata.Value.GetString() ?? string.Empty;
                }
            }

            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse metadata extraction JSON, using fallback");
            return new MetadataExtractionResult
            {
                Keywords = Array.Empty<string>(),
                Categories = Array.Empty<string>(),
                Confidence = 0.0
            };
        }
    }

    private FileFluxQualityAssessment ParseQualityAssessment(string jsonResponse)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonResponse);
            var root = doc.RootElement;

            var result = new FileFluxQualityAssessment
            {
                ConfidenceScore = root.GetProperty("confidenceScore").GetDouble(),
                CompletenessScore = root.GetProperty("completenessScore").GetDouble(),
                ConsistencyScore = root.GetProperty("consistencyScore").GetDouble(),
                Explanation = root.GetProperty("explanation").GetString() ?? string.Empty
            };

            if (root.TryGetProperty("recommendations", out var recommendations))
            {
                foreach (var rec in recommendations.EnumerateArray())
                {
                    var recommendation = new QualityRecommendation
                    {
                        Type = ParseRecommendationType(rec.GetProperty("type").GetString()),
                        Description = rec.GetProperty("description").GetString() ?? string.Empty,
                        SuggestedValue = rec.TryGetProperty("suggestedValue", out var suggestedValue)
                            ? suggestedValue.GetString()
                            : null,
                        Priority = rec.TryGetProperty("priority", out var priority) ? priority.GetInt32() : 5,
                        ExpectedImprovement = rec.TryGetProperty("expectedImprovement", out var improvement)
                            ? improvement.GetDouble()
                            : 0.0
                    };

                    if (rec.TryGetProperty("suggestedParameters", out var parameters))
                    {
                        foreach (var param in parameters.EnumerateObject())
                        {
                            recommendation.SuggestedParameters[param.Name] = param.Value.ValueKind switch
                            {
                                JsonValueKind.String => param.Value.GetString() ?? string.Empty,
                                JsonValueKind.Number => param.Value.GetInt32(),
                                JsonValueKind.True => true,
                                JsonValueKind.False => false,
                                _ => param.Value.ToString()
                            };
                        }
                    }

                    result.Recommendations.Add(recommendation);
                }
            }

            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse quality assessment JSON, using fallback");
            return new FileFluxQualityAssessment
            {
                ConfidenceScore = 0.0,
                CompletenessScore = 0.0,
                ConsistencyScore = 0.0,
                Explanation = "Quality assessment failed"
            };
        }
    }

    private SectionType ParseSectionType(string? typeString)
    {
        return typeString?.ToUpperInvariant() switch
        {
            "HEADER" => SectionType.Header,
            "PARAGRAPH" => SectionType.Paragraph,
            "LIST" => SectionType.List,
            "CODE" => SectionType.Code,
            "TABLE" => SectionType.Table,
            "IMAGE" => SectionType.Image,
            "QUOTE" => SectionType.Quote,
            "HEADINGL1" => SectionType.HeadingL1,
            "HEADINGL2" => SectionType.HeadingL2,
            "HEADINGL3" => SectionType.HeadingL3,
            "CODEBLOCK" => SectionType.CodeBlock,
            "APIENDPOINT" => SectionType.ApiEndpoint,
            "CLASS" => SectionType.Class,
            "METHOD" => SectionType.Method,
            "EXAMPLE" => SectionType.Example,
            "COMMENT" => SectionType.Comment,
            "NAVIGATION" => SectionType.Navigation,
            "ARTICLE" => SectionType.Article,
            "ASIDE" => SectionType.Aside,
            "FOOTER" => SectionType.Footer,
            _ => SectionType.Other
        };
    }

    private RecommendationType ParseRecommendationType(string? typeString)
    {
        return typeString?.ToUpperInvariant() switch
        {
            "CHUNK_SIZE_OPTIMIZATION" => RecommendationType.CHUNK_SIZE_OPTIMIZATION,
            "METADATA_ENHANCEMENT" => RecommendationType.METADATA_ENHANCEMENT,
            "TITLE_IMPROVEMENT" => RecommendationType.TITLE_IMPROVEMENT,
            "DESCRIPTION_ENHANCEMENT" => RecommendationType.DESCRIPTION_ENHANCEMENT,
            "CONTEXT_ADDITION" => RecommendationType.CONTEXT_ADDITION,
            "STRUCTURE_IMPROVEMENT" => RecommendationType.STRUCTURE_IMPROVEMENT,
            "CHUNKINGSTRATEGY" => RecommendationType.ChunkingStrategy,
            "CHUNKSIZE" => RecommendationType.ChunkSize,
            "OVERLAPCONFIGURATION" => RecommendationType.OverlapConfiguration,
            "STRUCTUREPRESERVATION" => RecommendationType.StructurePreservation,
            "CONTENTFILTERING" => RecommendationType.ContentFiltering,
            "BOUNDARYDETECTION" => RecommendationType.BoundaryDetection,
            "METADATAENHANCEMENT" => RecommendationType.MetadataEnhancement,
            _ => RecommendationType.CHUNK_SIZE_OPTIMIZATION
        };
    }

    #endregion
}
