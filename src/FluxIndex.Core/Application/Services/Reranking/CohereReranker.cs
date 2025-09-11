using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Core.Application.Services.Reranking;

/// <summary>
/// Cohere Rerank API adapter for multilingual reranking
/// Excellent for Korean and other non-English languages
/// </summary>
public class CohereReranker : IReranker
{
    private readonly HttpClient _httpClient;
    private readonly CohereRerankerOptions _options;
    private readonly ILogger<CohereReranker> _logger;
    private readonly SemaphoreSlim _rateLimitSemaphore;

    private const string RerankEndpoint = "https://api.cohere.ai/v1/rerank";

    public CohereReranker(
        HttpClient httpClient,
        CohereRerankerOptions options,
        ILogger<CohereReranker> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Configure HTTP client
        _httpClient.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        
        // Rate limiting
        _rateLimitSemaphore = new SemaphoreSlim(_options.MaxConcurrentRequests);
    }

    public async Task<IEnumerable<Document>> RerankAsync(
        string query,
        IEnumerable<Document> documents,
        int topK = 10,
        CancellationToken cancellationToken = default)
    {
        var docList = documents.ToList();
        if (!docList.Any())
            return docList;

        _logger.LogDebug("Reranking {Count} documents using Cohere API", docList.Count);

        // Process in batches if necessary (Cohere has limits)
        if (docList.Count > _options.MaxDocumentsPerRequest)
        {
            return await RerankInBatchesAsync(query, docList, topK, cancellationToken);
        }

        return await RerankSingleBatchAsync(query, docList, topK, cancellationToken);
    }

    private async Task<IEnumerable<Document>> RerankInBatchesAsync(
        string query,
        List<Document> documents,
        int topK,
        CancellationToken cancellationToken)
    {
        var allResults = new List<(Document Doc, float Score)>();
        
        // Process documents in batches
        for (int i = 0; i < documents.Count; i += _options.MaxDocumentsPerRequest)
        {
            var batch = documents
                .Skip(i)
                .Take(_options.MaxDocumentsPerRequest)
                .ToList();
            
            var batchResults = await RerankSingleBatchAsync(query, batch, batch.Count, cancellationToken);
            
            foreach (var doc in batchResults)
            {
                allResults.Add((doc, doc.Score));
            }
            
            // Rate limiting between batches
            if (i + _options.MaxDocumentsPerRequest < documents.Count)
            {
                await Task.Delay(_options.RateLimitDelayMs, cancellationToken);
            }
        }
        
        // Sort all results and return top K
        return allResults
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => x.Doc);
    }

    private async Task<IEnumerable<Document>> RerankSingleBatchAsync(
        string query,
        List<Document> documents,
        int topK,
        CancellationToken cancellationToken)
    {
        await _rateLimitSemaphore.WaitAsync(cancellationToken);
        try
        {
            // Prepare request
            var request = new CohereRerankRequest
            {
                Model = _options.Model,
                Query = query,
                Documents = documents.Select(d => d.Content ?? "").ToList(),
                TopN = Math.Min(topK, documents.Count),
                ReturnDocuments = false
            };

            var jsonContent = JsonSerializer.Serialize(request, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, RerankEndpoint)
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };

            // Add custom headers if specified
            if (!string.IsNullOrEmpty(_options.CustomEndpoint))
            {
                httpRequest.RequestUri = new Uri(_options.CustomEndpoint);
            }

            // Send request with retry logic
            var response = await SendWithRetryAsync(httpRequest, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Cohere API error: {StatusCode} - {Error}", 
                    response.StatusCode, errorContent);
                throw new InvalidOperationException($"Cohere API error: {response.StatusCode}");
            }

            // Parse response
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var rerankResponse = JsonSerializer.Deserialize<CohereRerankResponse>(responseContent, 
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            if (rerankResponse?.Results == null)
            {
                _logger.LogWarning("No results returned from Cohere API");
                return documents;
            }

            // Update documents with scores and return in ranked order
            var rerankedDocs = new List<Document>();
            foreach (var result in rerankResponse.Results.OrderByDescending(r => r.RelevanceScore))
            {
                if (result.Index < documents.Count)
                {
                    var doc = documents[result.Index];
                    doc.Score = result.RelevanceScore;
                    
                    // Add metadata
                    doc.Metadata ??= new();
                    doc.Metadata["cohere_score"] = result.RelevanceScore.ToString("F4");
                    doc.Metadata["cohere_position"] = (rerankedDocs.Count + 1).ToString();
                    
                    rerankedDocs.Add(doc);
                }
            }

            _logger.LogDebug("Cohere reranking complete. Top score: {TopScore:F4}", 
                rerankedDocs.FirstOrDefault()?.Score ?? 0);

            // Track API usage for cost monitoring
            TrackApiUsage(documents.Count, query.Length);

            return rerankedDocs;
        }
        finally
        {
            _rateLimitSemaphore.Release();
        }
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        int retryCount = 0;
        TimeSpan delay = TimeSpan.FromSeconds(1);

        while (retryCount < _options.MaxRetries)
        {
            try
            {
                var response = await _httpClient.SendAsync(
                    CloneHttpRequest(request), 
                    cancellationToken);

                if (response.IsSuccessStatusCode || 
                    (int)response.StatusCode < 500 || 
                    response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        _logger.LogWarning("Rate limited by Cohere API, waiting before retry");
                        await Task.Delay(delay * 2, cancellationToken);
                        retryCount++;
                        delay = TimeSpan.FromSeconds(delay.TotalSeconds * 2);
                        continue;
                    }
                    return response;
                }

                retryCount++;
                if (retryCount < _options.MaxRetries)
                {
                    _logger.LogWarning("Request failed with {StatusCode}, retrying in {Delay}s", 
                        response.StatusCode, delay.TotalSeconds);
                    await Task.Delay(delay, cancellationToken);
                    delay = TimeSpan.FromSeconds(delay.TotalSeconds * 2);
                }
            }
            catch (TaskCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (retryCount < _options.MaxRetries - 1)
            {
                _logger.LogWarning(ex, "Request failed, retrying in {Delay}s", delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromSeconds(delay.TotalSeconds * 2);
                retryCount++;
            }
        }

        throw new InvalidOperationException($"Failed to call Cohere API after {_options.MaxRetries} retries");
    }

    private static HttpRequestMessage CloneHttpRequest(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Content = request.Content
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }

    private void TrackApiUsage(int documentCount, int queryLength)
    {
        // Estimate tokens (rough approximation)
        var estimatedTokens = (documentCount * 100) + (queryLength / 4);
        var estimatedCost = estimatedTokens * _options.CostPerThousandTokens / 1000;
        
        _logger.LogInformation("Cohere API usage: {Documents} documents, ~{Tokens} tokens, ~${Cost:F4}",
            documentCount, estimatedTokens, estimatedCost);
    }
}

/// <summary>
/// Configuration for Cohere Reranker
/// </summary>
public class CohereRerankerOptions
{
    /// <summary>
    /// Cohere API key
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Rerank model to use
    /// </summary>
    public string Model { get; set; } = "rerank-multilingual-v2.0";

    /// <summary>
    /// Custom API endpoint (optional)
    /// </summary>
    public string? CustomEndpoint { get; set; }

    /// <summary>
    /// Maximum documents per API request
    /// </summary>
    public int MaxDocumentsPerRequest { get; set; } = 100;

    /// <summary>
    /// Maximum concurrent API requests
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 3;

    /// <summary>
    /// Rate limit delay between batches (ms)
    /// </summary>
    public int RateLimitDelayMs { get; set; } = 100;

    /// <summary>
    /// Maximum retry attempts
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Cost per 1000 tokens (for tracking)
    /// </summary>
    public decimal CostPerThousandTokens { get; set; } = 0.001m;
}

/// <summary>
/// Cohere Rerank API request model
/// </summary>
internal class CohereRerankRequest
{
    public string Model { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public List<string> Documents { get; set; } = new();
    public int TopN { get; set; }
    public bool ReturnDocuments { get; set; }
}

/// <summary>
/// Cohere Rerank API response model
/// </summary>
internal class CohereRerankResponse
{
    public List<CohereRerankResult> Results { get; set; } = new();
    public CohereApiMeta? Meta { get; set; }
}

/// <summary>
/// Individual rerank result
/// </summary>
internal class CohereRerankResult
{
    public int Index { get; set; }
    public float RelevanceScore { get; set; }
}

/// <summary>
/// API metadata
/// </summary>
internal class CohereApiMeta
{
    public CohereApiBilledUnits? BilledUnits { get; set; }
}

/// <summary>
/// Billing information
/// </summary>
internal class CohereApiBilledUnits
{
    public int SearchUnits { get; set; }
}