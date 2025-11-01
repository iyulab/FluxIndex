using FluxIndex.AI.Anthropic.Configuration;
using FluxIndex.Core.Interfaces;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxIndex.AI.Anthropic.Services;

/// <summary>
/// Anthropic Claude implementation of IMetadataExtractor
/// Follows OpenAI pattern with caching, retry, and fallback
/// </summary>
public class AnthropicMetadataExtractor : IMetadataExtractor
{
    private readonly ITextCompletionService _completionService;
    private readonly AnthropicOptions _options;
    private readonly IRuleBasedMetadataExtractor _ruleBasedExtractor;
    private readonly IMemoryCache? _cache;
    private readonly ILogger<AnthropicMetadataExtractor> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public AnthropicMetadataExtractor(
        ITextCompletionService completionService,
        IOptions<AnthropicOptions> options,
        IRuleBasedMetadataExtractor ruleBasedExtractor,
        ILogger<AnthropicMetadataExtractor> logger,
        IMemoryCache? cache = null)
    {
        _completionService = completionService;
        _options = options.Value;
        _ruleBasedExtractor = ruleBasedExtractor;
        _logger = logger;
        _cache = cache;
    }

    public async Task<ExtractedMetadata> ExtractAsync(
        string content,
        MetadataSchema schema = MetadataSchema.General,
        AIMetadataExtractionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            _logger.LogWarning("Empty or null content provided for metadata extraction");
            return new ExtractedMetadata();
        }

        options ??= new AIMetadataExtractionOptions();

        try
        {
            var sampledContent = SampleContent(content, options.Strategy);

            _logger.LogInformation("Extracting metadata with Anthropic: schema={Schema}, strategy={Strategy}, length={Length}",
                schema, options.Strategy, sampledContent.Length);

            var prompt = BuildPrompt(sampledContent, schema, options);
            var model = GetModelForStrategy(options.Strategy);

            var response = await CallAnthropicWithRetryAsync(prompt, model, options, cancellationToken);
            var metadata = ParseMetadataResponse(response, schema);

            metadata.DocumentId = Guid.NewGuid().ToString();
            metadata.ExtractedAt = DateTimeOffset.UtcNow;
            metadata.ExtractionMethod = $"Anthropic-{model}";
            metadata.Source = MetadataSource.AI;

            // Fallback if confidence too low
            if (metadata.OverallConfidence < options.MinConfidence)
            {
                _logger.LogWarning("Confidence {Confidence} below {Threshold}, merging with RuleBased",
                    metadata.OverallConfidence, options.MinConfidence);

                var ruleBasedMetadata = await _ruleBasedExtractor.ExtractAsync(content, schema, cancellationToken);
                metadata = _ruleBasedExtractor.MergeMetadata(metadata, ruleBasedMetadata);
            }

            _logger.LogInformation("Metadata extraction successful: confidence={Confidence}, topics={Topics}",
                metadata.OverallConfidence, metadata.Topics.Length);

            return metadata;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Anthropic metadata extraction failed for schema {Schema}", schema);

            if (!options.ContinueOnFailure)
            {
                throw;
            }

            _logger.LogWarning("Falling back to RuleBased extraction");
            return await _ruleBasedExtractor.ExtractAsync(content, schema, cancellationToken);
        }
    }

    public async Task<ExtractedMetadata> ExtractWithCacheAsync(
        string content,
        string cacheKey,
        MetadataSchema schema = MetadataSchema.General,
        AIMetadataExtractionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new AIMetadataExtractionOptions();

        if (!options.EnableCaching || _cache == null)
        {
            return await ExtractAsync(content, schema, options, cancellationToken);
        }

        if (_cache.TryGetValue(cacheKey, out ExtractedMetadata? cachedMetadata) && cachedMetadata != null)
        {
            _logger.LogDebug("Cache hit for metadata: {CacheKey}", cacheKey);
            return cachedMetadata;
        }

        var metadata = await ExtractAsync(content, schema, options, cancellationToken);
        _cache.Set(cacheKey, metadata, options.CacheTTL);
        _logger.LogDebug("Cached metadata: {CacheKey}, TTL={TTL}", cacheKey, options.CacheTTL);

        return metadata;
    }

    public Task<IReadOnlyList<ExtractedMetadata>> ExtractBatchAsync(
        IReadOnlyList<BatchMetadataRequest> requests,
        MetadataSchema schema = MetadataSchema.General,
        AIMetadataExtractionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Use ExtractBatchWithProgressAsync instead");
    }

    public async Task<BatchMetadataExtractionResult> ExtractBatchWithProgressAsync(
        BatchMetadataExtractionRequest request,
        AIMetadataExtractionOptions? options = null,
        IProgress<BatchMetadataExtractionProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new AIMetadataExtractionOptions();

        var result = new BatchMetadataExtractionResult
        {
            BatchId = request.BatchId,
            TotalItems = request.Items.Count,
            StartedAt = DateTime.UtcNow
        };

        using var semaphore = new SemaphoreSlim(request.MaxConcurrency);
        var lockObj = new object();
        var startTime = DateTime.UtcNow;

        var tasks = request.Items.Select(async (item, index) =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                progressCallback?.Report(new BatchMetadataExtractionProgress
                {
                    BatchId = request.BatchId,
                    CurrentItemIndex = index + 1,
                    TotalItems = request.Items.Count,
                    Status = BatchExtractionStatus.Processing,
                    CurrentDocumentId = item.DocumentId,
                    Message = $"Processing {item.DocumentId}...",
                    EstimatedTimeRemaining = CalculateEstimatedTime(startTime, index + 1, request.Items.Count)
                });

                var itemSchema = item.Schema ?? MetadataSchema.General;
                var itemOptions = options;
                if (item.Strategy.HasValue)
                {
                    itemOptions = new AIMetadataExtractionOptions
                    {
                        Strategy = item.Strategy.Value,
                        MinConfidence = options.MinConfidence,
                        ContinueOnFailure = options.ContinueOnFailure,
                        EnableCaching = options.EnableCaching,
                        CacheTTL = options.CacheTTL
                    };
                }

                var cacheKey = !string.IsNullOrEmpty(item.DocumentId)
                    ? GenerateCacheKey(item.Content, itemSchema)
                    : string.Empty;

                var itemStartTime = DateTime.UtcNow;
                var metadata = await ExtractWithCacheAsync(item.Content, cacheKey, itemSchema, itemOptions, cancellationToken);
                var processingTime = DateTime.UtcNow - itemStartTime;

                var itemResult = new MetadataExtractionItemResult
                {
                    DocumentId = item.DocumentId,
                    Success = true,
                    Metadata = metadata,
                    ProcessingTime = processingTime,
                    Timestamp = DateTime.UtcNow
                };

                lock (lockObj)
                {
                    result.ItemResults.Add(itemResult);
                    result.SuccessfulItems++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract metadata for document {DocumentId}", item.DocumentId);

                var itemResult = new MetadataExtractionItemResult
                {
                    DocumentId = item.DocumentId,
                    Success = false,
                    ErrorMessage = ex.Message,
                    Timestamp = DateTime.UtcNow
                };

                lock (lockObj)
                {
                    result.ItemResults.Add(itemResult);
                    result.FailedItems++;
                }

                if (!request.ContinueOnError)
                {
                    throw;
                }
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks);

        result.CompletedAt = DateTime.UtcNow;
        result.Statistics = CalculateBatchStatistics(result.ItemResults);

        progressCallback?.Report(new BatchMetadataExtractionProgress
        {
            BatchId = request.BatchId,
            CurrentItemIndex = request.Items.Count,
            TotalItems = request.Items.Count,
            Status = BatchExtractionStatus.Completed,
            SuccessfulItems = result.SuccessfulItems,
            FailedItems = result.FailedItems,
            Message = "Batch extraction completed"
        });

        return result;
    }

    public string GenerateCacheKey(string content, MetadataSchema schema)
    {
        var combined = $"{content}|{schema}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
        return Convert.ToBase64String(bytes);
    }

    public IReadOnlyList<MetadataSchema> GetSupportedSchemas()
    {
        return new[]
        {
            MetadataSchema.General,
            MetadataSchema.ProductManual,
            MetadataSchema.TechnicalDoc,
            MetadataSchema.Article,
            MetadataSchema.Custom
        };
    }

    public string GetSchemaDescription(MetadataSchema schema)
    {
        return schema switch
        {
            MetadataSchema.General => "General document metadata extraction",
            MetadataSchema.ProductManual => "Product manual and specification extraction",
            MetadataSchema.TechnicalDoc => "Technical documentation extraction",
            MetadataSchema.Article => "Article and blog post extraction",
            MetadataSchema.Custom => "Custom schema with user-defined prompt",
            _ => "Unknown schema"
        };
    }

    private string GetModelForStrategy(MetadataExtractionStrategy strategy)
    {
        return strategy switch
        {
            MetadataExtractionStrategy.Fast => _options.FastModel,
            MetadataExtractionStrategy.Deep => _options.DeepModel,
            _ => _options.DefaultModel
        };
    }

    private string SampleContent(string content, MetadataExtractionStrategy strategy)
    {
        var maxLength = strategy switch
        {
            MetadataExtractionStrategy.Fast => 2000,
            MetadataExtractionStrategy.Deep => 8000,
            _ => 4000 // Smart
        };

        return content.Length <= maxLength ? content : content[..maxLength];
    }

    private string BuildPrompt(string content, MetadataSchema schema, AIMetadataExtractionOptions options)
    {
        var schemaInstructions = schema switch
        {
            MetadataSchema.ProductManual => GetProductManualPrompt(),
            MetadataSchema.TechnicalDoc => GetTechnicalDocPrompt(),
            MetadataSchema.Article => GetArticlePrompt(),
            MetadataSchema.Custom => options.CustomPrompt ?? GetGeneralPrompt(),
            _ => GetGeneralPrompt()
        };

        return $@"{schemaInstructions}

Document content:
{content}

Extract metadata and return ONLY a valid JSON object (no markdown, no explanations):";
    }

    private string GetGeneralPrompt()
    {
        return @"Extract metadata from the document and return a JSON object with these fields:
- topics: array of 3-5 main topics
- keywords: array of 5-10 relevant keywords
- description: 1-2 sentence summary (max 200 chars)
- documentType: type (e.g., 'guide', 'tutorial', 'reference', 'article')
- language: ISO 639-1 code (e.g., 'en', 'ko')
- categories: array of categories
- overallConfidence: confidence score 0.0-1.0";
    }

    private string GetProductManualPrompt()
    {
        return GetGeneralPrompt() + @"

Additionally extract into schemaSpecificData:
- productName: product name
- manufacturer: company name
- version: version number
- model: model identifier";
    }

    private string GetTechnicalDocPrompt()
    {
        return GetGeneralPrompt() + @"

Additionally extract into schemaSpecificData:
- libraries: array of libraries with versions
- frameworks: array of frameworks
- technologies: array of technologies
- apiVersion: API version if applicable";
    }

    private string GetArticlePrompt()
    {
        return GetGeneralPrompt() + @"

Additionally extract into schemaSpecificData:
- author: author name
- publishedDate: publication date (ISO format)
- readingTimeMinutes: estimated reading time
- tags: array of tags";
    }

    private async Task<string> CallAnthropicWithRetryAsync(
        string prompt,
        string model,
        AIMetadataExtractionOptions options,
        CancellationToken cancellationToken)
    {
        var retries = 0;
        Exception? lastException = null;

        while (retries <= options.MaxRetries)
        {
            try
            {
                var response = await _completionService.GenerateJsonCompletionAsync(prompt, 4096, cancellationToken);
                return response;
            }
            catch (Exception ex)
            {
                lastException = ex;
                retries++;

                if (retries > options.MaxRetries)
                {
                    break;
                }

                var delay = options.RetryDelayMs * (int)Math.Pow(2, retries - 1);
                _logger.LogWarning(ex, "Anthropic API call failed, retry {Retry}/{MaxRetries} after {Delay}ms",
                    retries, options.MaxRetries, delay);

                await Task.Delay(delay, cancellationToken);
            }
        }

        throw new InvalidOperationException($"Anthropic API call failed after {options.MaxRetries} retries", lastException);
    }

    private ExtractedMetadata ParseMetadataResponse(string response, MetadataSchema schema)
    {
        try
        {
            var cleanedResponse = response.Trim();
            if (cleanedResponse.StartsWith("```json"))
            {
                cleanedResponse = cleanedResponse[7..];
            }
            if (cleanedResponse.StartsWith("```"))
            {
                cleanedResponse = cleanedResponse[3..];
            }
            if (cleanedResponse.EndsWith("```"))
            {
                cleanedResponse = cleanedResponse[..^3];
            }
            cleanedResponse = cleanedResponse.Trim();

            var metadata = JsonSerializer.Deserialize<ExtractedMetadata>(cleanedResponse, JsonOptions);
            if (metadata == null)
            {
                throw new JsonException("Deserialized metadata is null");
            }

            return metadata;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Anthropic response as JSON: {Response}", response);
            throw new InvalidOperationException("Failed to parse metadata from Anthropic response", ex);
        }
    }

    private BatchMetadataStatistics CalculateBatchStatistics(List<MetadataExtractionItemResult> results)
    {
        var successfulResults = results.Where(r => r.Success && r.Metadata != null).ToList();

        if (successfulResults.Count == 0)
        {
            return new BatchMetadataStatistics();
        }

        var statistics = new BatchMetadataStatistics
        {
            AverageConfidence = successfulResults.Average(r => r.Metadata!.OverallConfidence),
            AverageProcessingTime = TimeSpan.FromMilliseconds(successfulResults.Average(r => r.ProcessingTime.TotalMilliseconds))
        };

        // Calculate topic frequency
        var topicFrequency = new Dictionary<string, int>();
        foreach (var result in successfulResults)
        {
            foreach (var topic in result.Metadata!.Topics)
            {
                topicFrequency[topic] = topicFrequency.GetValueOrDefault(topic, 0) + 1;
            }
        }
        statistics.TopTopics = topicFrequency.OrderByDescending(kv => kv.Value).Take(10).ToDictionary(kv => kv.Key, kv => kv.Value);

        // Calculate keyword frequency
        var keywordFrequency = new Dictionary<string, int>();
        foreach (var result in successfulResults)
        {
            foreach (var keyword in result.Metadata!.Keywords)
            {
                keywordFrequency[keyword] = keywordFrequency.GetValueOrDefault(keyword, 0) + 1;
            }
        }
        statistics.TopKeywords = keywordFrequency.OrderByDescending(kv => kv.Value).Take(20).ToDictionary(kv => kv.Key, kv => kv.Value);

        // Calculate document type distribution
        var docTypeDistribution = successfulResults
            .GroupBy(r => r.Metadata!.DocumentType)
            .ToDictionary(g => g.Key, g => g.Count());
        statistics.DocumentTypeDistribution = docTypeDistribution;

        // Calculate language distribution
        var languageDistribution = successfulResults
            .GroupBy(r => r.Metadata!.Language)
            .ToDictionary(g => g.Key, g => g.Count());
        statistics.LanguageDistribution = languageDistribution;

        // Calculate extraction method distribution
        var extractionMethodDistribution = successfulResults
            .GroupBy(r => r.Metadata!.ExtractionMethod)
            .ToDictionary(g => g.Key, g => g.Count());
        statistics.ExtractionMethodDistribution = extractionMethodDistribution;

        return statistics;
    }

    private TimeSpan? CalculateEstimatedTime(DateTime startTime, int completedItems, int totalItems)
    {
        if (completedItems == 0)
        {
            return null;
        }

        var elapsed = DateTime.UtcNow - startTime;
        var avgTimePerItem = elapsed.TotalSeconds / completedItems;
        var remainingItems = totalItems - completedItems;
        var estimatedSeconds = avgTimePerItem * remainingItems;

        return TimeSpan.FromSeconds(estimatedSeconds);
    }
}
