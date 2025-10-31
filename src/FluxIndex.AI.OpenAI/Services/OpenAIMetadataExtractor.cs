using Azure.AI.OpenAI;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Interfaces;
using FluxIndex.Core.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxIndex.AI.OpenAI.Services;

/// <summary>
/// OpenAI implementation of IMetadataExtractor
/// Supports both OpenAI API and Azure OpenAI with caching, retry, and fallback
/// </summary>
public class OpenAIMetadataExtractor : IMetadataExtractor
{
    private readonly ChatClient _client;
    private readonly OpenAIOptions _options;
    private readonly IRuleBasedMetadataExtractor _ruleBasedExtractor;
    private readonly IMemoryCache? _cache;
    private readonly ILogger<OpenAIMetadataExtractor> _logger;

    // JSON serialization options for response parsing
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public OpenAIMetadataExtractor(
        IOptions<OpenAIOptions> options,
        IRuleBasedMetadataExtractor ruleBasedExtractor,
        ILogger<OpenAIMetadataExtractor> logger,
        IMemoryCache? cache = null)
    {
        _options = options.Value;
        _ruleBasedExtractor = ruleBasedExtractor;
        _logger = logger;
        _cache = cache;

        // Initialize ChatClient for metadata extraction
        _client = CreateChatClient(_options);
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
            // Sample content based on strategy
            var sampledContent = SampleContent(content, options.Strategy);

            _logger.LogInformation("Extracting metadata: schema={Schema}, strategy={Strategy}, contentLength={Length}",
                schema, options.Strategy, sampledContent.Length);

            // Build system prompt for the schema
            var systemPrompt = BuildSystemPrompt(schema, options);
            var userPrompt = BuildUserPrompt(sampledContent, schema, options);

            // Call OpenAI with retry logic
            var response = await CallOpenAIWithRetryAsync(
                systemPrompt,
                userPrompt,
                options,
                cancellationToken);

            // Parse JSON response
            var metadata = ParseMetadataResponse(response, schema);
            metadata.DocumentId = Guid.NewGuid().ToString();
            metadata.ExtractedAt = DateTimeOffset.UtcNow;
            metadata.ExtractionMethod = "AI";
            metadata.Source = MetadataSource.AI;

            // Fallback to RuleBased if confidence is too low
            if (metadata.OverallConfidence < options.MinConfidence)
            {
                _logger.LogWarning("AI confidence {Confidence} below threshold {Threshold}, merging with RuleBased",
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
            _logger.LogError(ex, "AI metadata extraction failed for schema {Schema}", schema);

            if (!options.ContinueOnFailure)
            {
                throw;
            }

            // Fallback to RuleBased on failure
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

        // Check cache first
        if (_cache.TryGetValue(cacheKey, out ExtractedMetadata? cachedMetadata) && cachedMetadata != null)
        {
            _logger.LogDebug("Cache hit for metadata: {CacheKey}", cacheKey);
            return cachedMetadata;
        }

        // Extract and cache
        var metadata = await ExtractAsync(content, schema, options, cancellationToken);

        _cache.Set(cacheKey, metadata, options.CacheTTL);
        _logger.LogDebug("Cached metadata: {CacheKey}, TTL={TTL}", cacheKey, options.CacheTTL);

        return metadata;
    }

    public async Task<IReadOnlyList<ExtractedMetadata>> ExtractBatchAsync(
        IReadOnlyList<BatchMetadataRequest> requests,
        MetadataSchema schema = MetadataSchema.General,
        AIMetadataExtractionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (!requests.Any())
        {
            return Array.Empty<ExtractedMetadata>();
        }

        options ??= new AIMetadataExtractionOptions();

        _logger.LogInformation("Batch metadata extraction: {Count} documents, schema={Schema}",
            requests.Count, schema);

        var results = new List<ExtractedMetadata>();
        var uncachedRequests = new List<BatchMetadataRequest>();
        var cachedResults = new Dictionary<string, ExtractedMetadata>();

        // 1. Check cache for all requests (if enabled)
        if (options.EnableCaching && _cache != null)
        {
            foreach (var request in requests)
            {
                var cacheKey = request.CacheKey ?? GenerateCacheKey(request.Content, schema);

                if (_cache.TryGetValue(cacheKey, out ExtractedMetadata? cached) && cached != null)
                {
                    cachedResults[request.DocumentId] = cached;
                    _logger.LogDebug("Cache hit for batch item: {DocumentId}", request.DocumentId);
                }
                else
                {
                    uncachedRequests.Add(request);
                }
            }
        }
        else
        {
            uncachedRequests.AddRange(requests);
        }

        // 2. Process uncached requests in parallel
        if (uncachedRequests.Any())
        {
            _logger.LogInformation("Processing {Count} uncached metadata extraction requests",
                uncachedRequests.Count);

            var tasks = uncachedRequests.Select(async request =>
            {
                try
                {
                    var metadata = await ExtractAsync(request.Content, schema, options, cancellationToken);
                    metadata.DocumentId = request.DocumentId;

                    // Merge custom metadata if provided
                    if (request.CustomMetadata != null)
                    {
                        foreach (var kvp in request.CustomMetadata)
                        {
                            metadata.SchemaSpecificData[kvp.Key] = kvp.Value;
                        }
                    }

                    // Cache the result
                    if (options.EnableCaching && _cache != null)
                    {
                        var cacheKey = request.CacheKey ?? GenerateCacheKey(request.Content, schema);
                        _cache.Set(cacheKey, metadata, options.CacheTTL);
                    }

                    return (request.DocumentId, metadata);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to extract metadata for document {DocumentId}", request.DocumentId);

                    // Return empty metadata on failure if ContinueOnFailure is true
                    return (request.DocumentId, new ExtractedMetadata { DocumentId = request.DocumentId });
                }
            });

            var extractedResults = await Task.WhenAll(tasks);

            foreach (var (documentId, metadata) in extractedResults)
            {
                cachedResults[documentId] = metadata;
            }
        }

        // 3. Reconstruct results in original order
        foreach (var request in requests)
        {
            if (cachedResults.TryGetValue(request.DocumentId, out var metadata))
            {
                results.Add(metadata);
            }
            else
            {
                results.Add(new ExtractedMetadata { DocumentId = request.DocumentId });
            }
        }

        _logger.LogInformation("Batch extraction complete: {Total} requests, {Cached} cached, {Generated} generated",
            requests.Count, requests.Count - uncachedRequests.Count, uncachedRequests.Count);

        return results;
    }

    public string GenerateCacheKey(string content, MetadataSchema schema)
    {
        using var sha256 = SHA256.Create();
        var input = $"{schema}:{content.Substring(0, Math.Min(1000, content.Length))}";
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return $"metadata:{Convert.ToBase64String(hash).Replace("/", "_").Replace("+", "-")[..24]}";
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
            MetadataSchema.General => "General document analysis - topics, keywords, description, document type, language, categories",
            MetadataSchema.ProductManual => "Product manual analysis - product name, company, version, model, release date, specifications",
            MetadataSchema.TechnicalDoc => "Technical documentation - libraries, frameworks, technologies, API version, code examples",
            MetadataSchema.Article => "Article/blog post analysis - author, published date, reading time, tags, article structure",
            MetadataSchema.Custom => "Custom schema with user-defined extraction prompt",
            _ => "Unknown schema"
        };
    }

    // ===================================================================
    // Private Helper Methods
    // ===================================================================

    private ChatClient CreateChatClient(OpenAIOptions options)
    {
        if (string.IsNullOrEmpty(options.Endpoint))
        {
            // Use OpenAI API
            var openAIClient = new OpenAIClient(options.ApiKey);
            return openAIClient.GetChatClient(options.ModelName);
        }
        else
        {
            // Use Azure OpenAI
            var azureClient = new AzureOpenAIClient(
                new Uri(options.Endpoint),
                new System.ClientModel.ApiKeyCredential(options.ApiKey));
            return azureClient.GetChatClient(options.ModelName);
        }
    }

    private string SampleContent(string content, MetadataExtractionStrategy strategy)
    {
        var maxChars = strategy switch
        {
            MetadataExtractionStrategy.Fast => 2000,
            MetadataExtractionStrategy.Smart => 4000,
            MetadataExtractionStrategy.Deep => 8000,
            _ => 4000
        };

        if (content.Length <= maxChars)
        {
            return content;
        }

        // Adaptive sampling: take beginning + middle + end
        var chunkSize = maxChars / 3;
        var beginning = content.Substring(0, chunkSize);
        var middle = content.Substring(content.Length / 2 - chunkSize / 2, chunkSize);
        var end = content.Substring(content.Length - chunkSize, chunkSize);

        return $"{beginning}\n\n[... middle section ...]\n\n{middle}\n\n[... end section ...]\n\n{end}";
    }

    private string BuildSystemPrompt(MetadataSchema schema, AIMetadataExtractionOptions options)
    {
        // Will be implemented in Phase 2.2
        return schema switch
        {
            MetadataSchema.ProductManual => BuildProductManualPrompt(),
            MetadataSchema.TechnicalDoc => BuildTechnicalDocPrompt(),
            MetadataSchema.Article => BuildArticlePrompt(),
            MetadataSchema.Custom => options.CustomPrompt ?? BuildGeneralPrompt(),
            _ => BuildGeneralPrompt()
        };
    }

    private string BuildUserPrompt(string content, MetadataSchema schema, AIMetadataExtractionOptions options)
    {
        return $"Analyze the following document and extract metadata according to the schema:\n\n{content}";
    }

    private string BuildGeneralPrompt() => @"You are a document metadata extraction specialist.

Extract the following metadata from the document:

REQUIRED FIELDS:
- topics: Main topics covered (3-5 items, array of strings)
- keywords: Important searchable terms (5-10 items, array of strings)
- description: One-sentence summary (max 200 characters)
- documentType: manual | guide | tutorial | reference | article | note | documentation
- language: Primary language code (en, ko, ja, zh, etc.)
- categories: Document categories (array of strings, if applicable)

CONFIDENCE:
- overallConfidence: Overall extraction confidence (0.0-1.0)
- fieldConfidence: Per-field confidence scores (object with field names as keys)

Return ONLY valid JSON matching this structure. Do not include markdown code blocks or additional text.

Example output:
{
  ""topics"": [""JavaScript"", ""Async Programming"", ""Promises""],
  ""keywords"": [""async"", ""await"", ""promises"", ""callbacks"", ""async-await""],
  ""description"": ""Comprehensive guide to JavaScript asynchronous programming patterns and best practices"",
  ""documentType"": ""tutorial"",
  ""language"": ""en"",
  ""categories"": [""Programming"", ""JavaScript"", ""Web Development""],
  ""overallConfidence"": 0.91,
  ""fieldConfidence"": {
    ""topics"": 0.95,
    ""keywords"": 0.92,
    ""description"": 0.88,
    ""documentType"": 0.90,
    ""language"": 0.98,
    ""categories"": 0.85
  }
}";

    private string BuildProductManualPrompt() => @"You are a product manual metadata extraction specialist.

Extract product-specific metadata from the document:

REQUIRED FIELDS:
- topics: Main topics covered in the manual (3-5 items)
- keywords: Searchable product terms (5-10 items)
- description: One-sentence product/manual summary (max 200 chars)
- documentType: Should be ""manual"" or ""guide""
- language: Primary language code
- categories: Product categories

SCHEMA-SPECIFIC DATA (store in schemaSpecificData object):
- productName: Full product name (REQUIRED)
- company: Manufacturer/company name (REQUIRED)
- version: Product or software version (REQUIRED)
- model: Product model number (OPTIONAL)
- releaseDate: Release or publication date in ISO 8601 format (OPTIONAL)

CONFIDENCE:
- overallConfidence: Overall extraction confidence (0.0-1.0)
- fieldConfidence: Per-field confidence scores

Return ONLY valid JSON. Example:

{
  ""topics"": [""Camera Features"", ""Battery Management"", ""Display Settings""],
  ""keywords"": [""iphone"", ""pro"", ""camera"", ""battery"", ""display"", ""ios""],
  ""description"": ""User manual for iPhone 15 Pro smartphone with iOS 17.2"",
  ""documentType"": ""manual"",
  ""language"": ""en"",
  ""categories"": [""Electronics"", ""Smartphone"", ""Mobile Device""],
  ""schemaSpecificData"": {
    ""productName"": ""iPhone 15 Pro"",
    ""company"": ""Apple Inc."",
    ""version"": ""iOS 17.2"",
    ""model"": ""A2848"",
    ""releaseDate"": ""2023-09-22""
  },
  ""overallConfidence"": 0.93,
  ""fieldConfidence"": {
    ""productName"": 0.98,
    ""company"": 0.99,
    ""version"": 0.92,
    ""topics"": 0.90,
    ""keywords"": 0.88
  }
}";

    private string BuildTechnicalDocPrompt() => @"You are a technical documentation metadata extraction specialist.

Extract technical metadata from the document:

REQUIRED FIELDS:
- topics: Technical topics covered (3-5 items, e.g., ""React Hooks"", ""API Design"")
- keywords: Technical searchable terms (5-10 items, e.g., ""useState"", ""REST"", ""async"")
- description: Technical summary (max 200 chars)
- documentType: tutorial | reference | guide | documentation | article
- language: Primary language code
- categories: Technology categories (e.g., [""Frontend"", ""Backend"", ""Database""])

SCHEMA-SPECIFIC DATA (store in schemaSpecificData object):
- libraries: Libraries/packages mentioned with versions (array, e.g., [""react@18.2.0"", ""axios@1.4.0""])
- frameworks: Frameworks used (array, e.g., [""React"", ""Express"", ""Django""])
- technologies: Technologies/languages (array, e.g., [""JavaScript"", ""TypeScript"", ""Python""])
- apiVersion: API version if applicable (string, OPTIONAL)

CONFIDENCE:
- overallConfidence: Overall extraction confidence (0.0-1.0)
- fieldConfidence: Per-field confidence scores

Return ONLY valid JSON. Example:

{
  ""topics"": [""React Hooks"", ""State Management"", ""Component Lifecycle""],
  ""keywords"": [""hooks"", ""useState"", ""useEffect"", ""react"", ""components"", ""state""],
  ""description"": ""Advanced guide to React Hooks patterns for state management and side effects"",
  ""documentType"": ""tutorial"",
  ""language"": ""en"",
  ""categories"": [""Frontend"", ""React"", ""JavaScript""],
  ""schemaSpecificData"": {
    ""libraries"": [""react@18.2.0"", ""@tanstack/react-query@4.0.0""],
    ""frameworks"": [""React""],
    ""technologies"": [""JavaScript"", ""TypeScript"", ""HTML"", ""CSS""],
    ""apiVersion"": ""v18""
  },
  ""overallConfidence"": 0.93,
  ""fieldConfidence"": {
    ""topics"": 0.95,
    ""libraries"": 0.90,
    ""frameworks"": 0.92,
    ""technologies"": 0.94,
    ""keywords"": 0.88
  }
}";

    private string BuildArticlePrompt() => @"You are an article/blog post metadata extraction specialist.

Extract article-specific metadata from the document:

REQUIRED FIELDS:
- topics: Main topics covered (3-5 items)
- keywords: Searchable article terms (5-10 items)
- description: Article summary (max 200 chars)
- documentType: Should be ""article"" or ""blog""
- language: Primary language code
- categories: Article categories (e.g., [""Technology"", ""Tutorial"", ""Opinion""])

SCHEMA-SPECIFIC DATA (store in schemaSpecificData object):
- author: Author name (REQUIRED if available)
- publishedDate: Publication date in ISO 8601 format (OPTIONAL)
- tags: Article tags (array, OPTIONAL)
- readingTimeMinutes: Estimated reading time in minutes (integer, OPTIONAL)

CONFIDENCE:
- overallConfidence: Overall extraction confidence (0.0-1.0)
- fieldConfidence: Per-field confidence scores

Return ONLY valid JSON. Example:

{
  ""topics"": [""Machine Learning"", ""Neural Networks"", ""Deep Learning""],
  ""keywords"": [""ai"", ""ml"", ""neural network"", ""tensorflow"", ""training""],
  ""description"": ""Introduction to neural networks and deep learning fundamentals for beginners"",
  ""documentType"": ""article"",
  ""language"": ""en"",
  ""categories"": [""Technology"", ""AI"", ""Tutorial""],
  ""schemaSpecificData"": {
    ""author"": ""John Doe"",
    ""publishedDate"": ""2024-01-15"",
    ""tags"": [""ai"", ""machine-learning"", ""tutorial"", ""beginner""],
    ""readingTimeMinutes"": 8
  },
  ""overallConfidence"": 0.89,
  ""fieldConfidence"": {
    ""topics"": 0.92,
    ""author"": 0.95,
    ""publishedDate"": 0.88,
    ""keywords"": 0.85,
    ""readingTimeMinutes"": 0.75
  }
}";

    private async Task<string> CallOpenAIWithRetryAsync(
        string systemPrompt,
        string userPrompt,
        AIMetadataExtractionOptions options,
        CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>
        {
            ChatMessage.CreateSystemMessage(systemPrompt),
            ChatMessage.CreateUserMessage(userPrompt)
        };

        var chatOptions = new ChatCompletionOptions
        {
            Temperature = 0.3f // Lower temperature for more consistent metadata extraction
        };

        var maxRetries = options.MaxRetries;
        var retryDelay = options.RetryDelayMs;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Apply timeout
                using var timeoutCts = new CancellationTokenSource(options.TimeoutMs);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                _logger.LogDebug("OpenAI API call attempt {Attempt}/{MaxAttempts}", attempt + 1, maxRetries + 1);

                var response = await _client.CompleteChatAsync(messages, chatOptions, linkedCts.Token);
                var content = response.Value.Content[0].Text;

                _logger.LogInformation("OpenAI API call successful on attempt {Attempt}", attempt + 1);
                return content;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // User-requested cancellation, don't retry
                _logger.LogWarning("Metadata extraction cancelled by user");
                throw;
            }
            catch (OperationCanceledException) when (attempt < maxRetries)
            {
                // Timeout, retry
                _logger.LogWarning("OpenAI API call timed out (attempt {Attempt}/{MaxAttempts}), retrying after {Delay}ms",
                    attempt + 1, maxRetries + 1, retryDelay);

                await Task.Delay(retryDelay, cancellationToken);
                retryDelay *= 2; // Exponential backoff
            }
            catch (Exception ex) when (attempt < maxRetries && IsRetryableException(ex))
            {
                // Retryable error (network, rate limit, etc.)
                _logger.LogWarning(ex, "OpenAI API call failed (attempt {Attempt}/{MaxAttempts}), retrying after {Delay}ms",
                    attempt + 1, maxRetries + 1, retryDelay);

                await Task.Delay(retryDelay, cancellationToken);
                retryDelay *= 2; // Exponential backoff
            }
            catch (Exception ex)
            {
                // Non-retryable error or max retries exceeded
                _logger.LogError(ex, "OpenAI API call failed after {Attempts} attempts", attempt + 1);
                throw;
            }
        }

        // Should never reach here
        throw new InvalidOperationException("Retry logic failed unexpectedly");
    }

    private bool IsRetryableException(Exception ex)
    {
        // Retry on network errors, rate limits, and transient server errors
        var exceptionType = ex.GetType().Name;

        return exceptionType.Contains("Http") ||
               exceptionType.Contains("Network") ||
               exceptionType.Contains("Timeout") ||
               exceptionType.Contains("RateLimit") ||
               ex.Message.Contains("429") || // Rate limit
               ex.Message.Contains("500") || // Server error
               ex.Message.Contains("502") || // Bad gateway
               ex.Message.Contains("503") || // Service unavailable
               ex.Message.Contains("504");   // Gateway timeout
    }

    private ExtractedMetadata ParseMetadataResponse(string response, MetadataSchema schema)
    {
        try
        {
            // Extract JSON block from response (remove markdown code blocks if present)
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}') + 1;

            if (jsonStart < 0 || jsonEnd <= jsonStart)
            {
                _logger.LogWarning("No JSON object found in response");
                return CreateFallbackMetadata(schema, 0.3f);
            }

            var jsonString = response.Substring(jsonStart, jsonEnd - jsonStart);

            // Parse JSON to Dictionary first for flexible processing
            var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonString, JsonOptions);

            if (parsed == null)
            {
                _logger.LogWarning("Failed to deserialize JSON response");
                return CreateFallbackMetadata(schema, 0.3f);
            }

            // Build ExtractedMetadata from parsed JSON
            var metadata = new ExtractedMetadata
            {
                Topics = ExtractStringArray(parsed, "topics"),
                Keywords = ExtractStringArray(parsed, "keywords"),
                Description = ExtractString(parsed, "description"),
                DocumentType = ExtractString(parsed, "documentType"),
                Language = ExtractString(parsed, "language") ?? "en",
                Categories = ExtractStringArray(parsed, "categories"),
                OverallConfidence = ExtractFloat(parsed, "overallConfidence", 0.5f),
                Source = MetadataSource.AI,
                ExtractionMethod = "AI"
            };

            // Extract field confidence
            if (parsed.TryGetValue("fieldConfidence", out var fieldConfObj) &&
                fieldConfObj.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in fieldConfObj.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Number)
                    {
                        metadata.FieldConfidence[prop.Name] = (float)prop.Value.GetDouble();
                        metadata.FieldSources[prop.Name] = MetadataSource.AI;
                    }
                }
            }

            // Extract schema-specific data
            if (parsed.TryGetValue("schemaSpecificData", out var schemaDataObj) &&
                schemaDataObj.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in schemaDataObj.EnumerateObject())
                {
                    metadata.SchemaSpecificData[prop.Name] = ConvertJsonElement(prop.Value);
                }
            }

            // Validate essential fields
            if (metadata.Topics.Length == 0 && metadata.Keywords.Length == 0)
            {
                _logger.LogWarning("No topics or keywords extracted, low quality response");
                metadata.OverallConfidence = Math.Min(metadata.OverallConfidence, 0.4f);
            }

            _logger.LogDebug("Successfully parsed metadata: topics={TopicCount}, keywords={KeywordCount}, confidence={Confidence}",
                metadata.Topics.Length, metadata.Keywords.Length, metadata.OverallConfidence);

            return metadata;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "JSON parsing failed, returning fallback metadata");
            return CreateFallbackMetadata(schema, 0.3f);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during metadata parsing");
            return CreateFallbackMetadata(schema, 0.2f);
        }
    }

    private string[] ExtractStringArray(Dictionary<string, JsonElement> parsed, string key)
    {
        if (!parsed.TryGetValue(key, out var element))
            return Array.Empty<string>();

        if (element.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        return element.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString() ?? "")
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();
    }

    private string ExtractString(Dictionary<string, JsonElement> parsed, string key)
    {
        if (!parsed.TryGetValue(key, out var element))
            return string.Empty;

        return element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : string.Empty;
    }

    private float ExtractFloat(Dictionary<string, JsonElement> parsed, string key, float defaultValue = 0f)
    {
        if (!parsed.TryGetValue(key, out var element))
            return defaultValue;

        if (element.ValueKind == JsonValueKind.Number)
            return (float)element.GetDouble();

        return defaultValue;
    }

    private object ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.TryGetInt32(out var intVal) ? intVal : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => element.EnumerateArray()
                .Select(ConvertJsonElement)
                .ToArray(),
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value)),
            _ => element.ToString()
        };
    }

    private ExtractedMetadata CreateFallbackMetadata(MetadataSchema schema, float confidence)
    {
        return new ExtractedMetadata
        {
            OverallConfidence = confidence,
            Source = MetadataSource.AI,
            ExtractionMethod = "AI-ParseFailed",
            Language = "en",
            DocumentType = schema switch
            {
                MetadataSchema.ProductManual => "manual",
                MetadataSchema.TechnicalDoc => "documentation",
                MetadataSchema.Article => "article",
                _ => "unknown"
            }
        };
    }

    public async Task<BatchMetadataExtractionResult> ExtractBatchWithProgressAsync(
        BatchMetadataExtractionRequest request,
        AIMetadataExtractionOptions? options = null,
        IProgress<BatchMetadataExtractionProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (request.Items == null || request.Items.Count == 0)
            throw new ArgumentException("Batch request must contain at least one item", nameof(request));

        options ??= new AIMetadataExtractionOptions();

        var result = new BatchMetadataExtractionResult
        {
            BatchId = request.BatchId,
            StartedAt = DateTime.UtcNow,
            TotalItems = request.Items.Count
        };

        var startTime = DateTime.UtcNow;

        // Report initial progress
        progressCallback?.Report(new BatchMetadataExtractionProgress
        {
            BatchId = request.BatchId,
            CurrentItemIndex = 0,
            TotalItems = request.Items.Count,
            Status = BatchExtractionStatus.Processing,
            Message = "Starting batch metadata extraction..."
        });

        _logger.LogInformation("Starting batch metadata extraction: {BatchId}, {ItemCount} items",
            request.BatchId, request.Items.Count);

        // Create semaphore for concurrency control
        using var semaphore = new System.Threading.SemaphoreSlim(request.MaxConcurrency);

        var tasks = request.Items.Select(async (item, index) =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var itemStartTime = DateTime.UtcNow;

                // Report progress for current item
                progressCallback?.Report(new BatchMetadataExtractionProgress
                {
                    BatchId = request.BatchId,
                    CurrentItemIndex = index + 1,
                    TotalItems = request.Items.Count,
                    Status = BatchExtractionStatus.Processing,
                    CurrentDocumentId = item.DocumentId,
                    SuccessfulItems = result.SuccessfulItems,
                    FailedItems = result.FailedItems,
                    Message = $"Processing document {index + 1}/{request.Items.Count}: {item.DocumentId}",
                    EstimatedTimeRemaining = CalculateEstimatedTime(
                        startTime, index + 1, request.Items.Count)
                });

                try
                {
                    // Determine schema and strategy for this item
                    var itemSchema = item.Schema ?? MetadataSchema.General;
                    var itemStrategy = item.Strategy ?? options.Strategy;
                    var itemOptions = new AIMetadataExtractionOptions
                    {
                        Strategy = itemStrategy,
                        MinConfidence = options.MinConfidence,
                        CustomPrompt = options.CustomPrompt,
                        MaxRetries = options.MaxRetries,
                        RetryDelayMs = options.RetryDelayMs,
                        TimeoutMs = options.TimeoutMs,
                        MaxTokens = options.MaxTokens,
                        CacheTTL = options.CacheTTL
                    };

                    // Generate cache key
                    var cacheKey = GenerateCacheKey(item.Content, itemSchema);

                    // Extract metadata with caching
                    var metadata = await ExtractWithCacheAsync(
                        item.Content,
                        cacheKey,
                        itemSchema,
                        itemOptions,
                        cancellationToken);

                    var itemResult = new MetadataExtractionItemResult
                    {
                        DocumentId = item.DocumentId,
                        Success = true,
                        Metadata = metadata,
                        ProcessingTime = DateTime.UtcNow - itemStartTime,
                        Timestamp = DateTime.UtcNow
                    };

                    lock (result)
                    {
                        result.ItemResults.Add(itemResult);
                        result.SuccessfulItems++;
                    }

                    _logger.LogDebug("Successfully extracted metadata for document: {DocumentId}", item.DocumentId);

                    return itemResult;
                }
                catch (Exception ex) when (request.ContinueOnError)
                {
                    _logger.LogWarning(ex, "Failed to extract metadata for document: {DocumentId}", item.DocumentId);

                    var itemResult = new MetadataExtractionItemResult
                    {
                        DocumentId = item.DocumentId,
                        Success = false,
                        ErrorMessage = ex.Message,
                        ProcessingTime = DateTime.UtcNow - itemStartTime,
                        Timestamp = DateTime.UtcNow
                    };

                    lock (result)
                    {
                        result.ItemResults.Add(itemResult);
                        result.FailedItems++;
                    }

                    return itemResult;
                }
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        // Wait for all tasks to complete
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception ex) when (!request.ContinueOnError)
        {
            _logger.LogError(ex, "Batch metadata extraction failed: {BatchId}", request.BatchId);

            result.CompletedAt = DateTime.UtcNow;

            // Report final error status
            progressCallback?.Report(new BatchMetadataExtractionProgress
            {
                BatchId = request.BatchId,
                CurrentItemIndex = request.Items.Count,
                TotalItems = request.Items.Count,
                Status = BatchExtractionStatus.Failed,
                SuccessfulItems = result.SuccessfulItems,
                FailedItems = result.FailedItems,
                Message = $"Batch extraction failed: {ex.Message}"
            });

            throw;
        }

        result.CompletedAt = DateTime.UtcNow;

        // Calculate statistics
        result.Statistics = CalculateBatchStatistics(result.ItemResults);

        // Report final progress
        var finalStatus = result.FailedItems == 0
            ? BatchExtractionStatus.Completed
            : result.SuccessfulItems > 0
                ? BatchExtractionStatus.PartiallyCompleted
                : BatchExtractionStatus.Failed;

        progressCallback?.Report(new BatchMetadataExtractionProgress
        {
            BatchId = request.BatchId,
            CurrentItemIndex = request.Items.Count,
            TotalItems = request.Items.Count,
            Status = finalStatus,
            SuccessfulItems = result.SuccessfulItems,
            FailedItems = result.FailedItems,
            Message = $"Batch extraction completed: {result.SuccessfulItems} succeeded, {result.FailedItems} failed"
        });

        _logger.LogInformation(
            "Batch metadata extraction completed: {BatchId}, Success={Success}, Failed={Failed}, Time={Time}ms",
            request.BatchId, result.SuccessfulItems, result.FailedItems, result.ProcessingTime.TotalMilliseconds);

        return result;
    }

    private TimeSpan? CalculateEstimatedTime(DateTime startTime, int completedItems, int totalItems)
    {
        if (completedItems == 0)
            return null;

        var elapsed = DateTime.UtcNow - startTime;
        var averageTimePerItem = elapsed.TotalSeconds / completedItems;
        var remainingItems = totalItems - completedItems;
        var estimatedSeconds = averageTimePerItem * remainingItems;

        return TimeSpan.FromSeconds(estimatedSeconds);
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
            AverageProcessingTime = TimeSpan.FromMilliseconds(
                successfulResults.Average(r => r.ProcessingTime.TotalMilliseconds))
        };

        // Calculate topic frequency
        var topicFrequency = new Dictionary<string, int>();
        foreach (var result in successfulResults)
        {
            foreach (var topic in result.Metadata!.Topics)
            {
                if (!string.IsNullOrWhiteSpace(topic))
                {
                    topicFrequency[topic] = topicFrequency.GetValueOrDefault(topic, 0) + 1;
                }
            }
        }
        statistics.TopTopics = topicFrequency
            .OrderByDescending(kv => kv.Value)
            .Take(10)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        // Calculate keyword frequency
        var keywordFrequency = new Dictionary<string, int>();
        foreach (var result in successfulResults)
        {
            foreach (var keyword in result.Metadata!.Keywords)
            {
                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    keywordFrequency[keyword] = keywordFrequency.GetValueOrDefault(keyword, 0) + 1;
                }
            }
        }
        statistics.TopKeywords = keywordFrequency
            .OrderByDescending(kv => kv.Value)
            .Take(20)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        // Calculate document type distribution
        var docTypeDistribution = successfulResults
            .GroupBy(r => r.Metadata!.DocumentType ?? "unknown")
            .ToDictionary(g => g.Key, g => g.Count());
        statistics.DocumentTypeDistribution = docTypeDistribution;

        // Calculate language distribution
        var languageDistribution = successfulResults
            .GroupBy(r => r.Metadata!.Language ?? "unknown")
            .ToDictionary(g => g.Key, g => g.Count());
        statistics.LanguageDistribution = languageDistribution;

        // Calculate extraction method distribution
        var methodDistribution = successfulResults
            .GroupBy(r => r.Metadata!.ExtractionMethod ?? "unknown")
            .ToDictionary(g => g.Key, g => g.Count());
        statistics.ExtractionMethodDistribution = methodDistribution;

        return statistics;
    }
}
