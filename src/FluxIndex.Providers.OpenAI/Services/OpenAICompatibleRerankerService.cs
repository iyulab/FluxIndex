using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Base;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Providers.OpenAI.Services;

/// <summary>
/// Adapts any OpenAI-compatible /v1/rerank endpoint to FluxIndex's
/// <see cref="IReranker"/> using the <see cref="RerankerBase"/> template.
/// Supports GPUStack, Cohere-compatible proxies, and any server implementing
/// the OpenAI rerank extension format.
/// </summary>
public sealed partial class OpenAICompatibleRerankerService : RerankerBase, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly ILogger<OpenAICompatibleRerankerService> _logger;
    private readonly bool _ownsHttpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Creates an OpenAI-compatible reranker service.
    /// </summary>
    /// <param name="endpoint">Base API URL (e.g., "http://172.19.10.10/v1").</param>
    /// <param name="apiKey">API key for authentication. Null for unauthenticated endpoints.</param>
    /// <param name="model">Rerank model name (e.g., "qwen3-reranker-0.6b").</param>
    /// <param name="logger">Logger instance.</param>
    public OpenAICompatibleRerankerService(
        string endpoint,
        string? apiKey,
        string model,
        ILogger<OpenAICompatibleRerankerService> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(logger);

        _model = model;
        _logger = logger;
        _ownsHttpClient = true;

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(endpoint.TrimEnd('/') + "/")
        };

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    /// <summary>
    /// Creates an OpenAI-compatible reranker service with a pre-configured HttpClient.
    /// Use this overload for testing or when custom handlers are required.
    /// </summary>
    public OpenAICompatibleRerankerService(
        HttpClient httpClient,
        string model,
        ILogger<OpenAICompatibleRerankerService> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _model = model;
        _logger = logger;
        _ownsHttpClient = false;
    }

    /// <inheritdoc />
    protected override async Task<IEnumerable<(int Index, float Score)>> RerankCoreAsync(
        string query,
        IReadOnlyList<string> documents,
        int topN,
        CancellationToken cancellationToken)
    {
        LogReranking(_logger, _model, query.Length, documents.Count, topN);

        var request = new RerankRequest
        {
            Model = _model,
            Query = query,
            Documents = documents.ToList(),
            TopN = topN
        };

        var response = await _httpClient.PostAsJsonAsync(
            "rerank", request, JsonOptions, cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<RerankResponse>(
            JsonOptions, cancellationToken);

        if (result?.Results is null or { Count: 0 })
        {
            throw new InvalidOperationException(
                $"No rerank results returned from model '{_model}'.");
        }

        return result.Results
            .OrderByDescending(r => r.RelevanceScore)
            .Select(r => (r.Index, r.RelevanceScore));
    }

    /// <inheritdoc />
    public override RerankModelInfo GetModelInfo() => new()
    {
        Name = _model,
        Type = RerankModel.Custom,
        RequiresApiKey = true,
    };

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    #region LoggerMessage

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Reranking with {Model}: {DocumentCount} documents for query (length={QueryLength}), topN={TopN}")]
    private static partial void LogReranking(
        ILogger logger, string model, int queryLength, int documentCount, int topN);

    #endregion

    #region DTO Models

    internal sealed class RerankRequest
    {
        public string Model { get; init; } = string.Empty;
        public string Query { get; init; } = string.Empty;
        public List<string> Documents { get; init; } = [];
        public int? TopN { get; init; }
    }

    internal sealed class RerankResponse
    {
        public List<RerankResultItem>? Results { get; init; }
    }

    internal sealed class RerankResultItem
    {
        public int Index { get; init; }
        public float RelevanceScore { get; init; }
    }

    #endregion
}
