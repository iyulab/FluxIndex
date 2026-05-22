using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluxIndex.Core.Application.Services.Base;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Providers.OpenAI.Services;

/// <summary>
/// Adapts any OpenAI-compatible /v1/embeddings endpoint to FluxIndex's
/// <see cref="FluxIndex.Core.Application.Interfaces.IEmbeddingService"/>.
/// Supports OpenAI, GPUStack, Ollama, and any OpenAI-compatible embedding server.
/// </summary>
public sealed partial class OpenAICompatibleEmbeddingService : EmbeddingServiceBase, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly int _dimension;
    private readonly ILogger<OpenAICompatibleEmbeddingService> _logger;
    private readonly bool _ownsHttpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Creates an OpenAI-compatible embedding service.
    /// </summary>
    /// <param name="endpoint">Base API URL (e.g., "https://api.openai.com/v1").</param>
    /// <param name="apiKey">API key for authentication. Null for unauthenticated endpoints.</param>
    /// <param name="model">Embedding model name (e.g., "text-embedding-3-small").</param>
    /// <param name="dimension">Expected embedding vector dimension (e.g., 1024 for qwen3-embedding-0.6b).</param>
    /// <param name="logger">Logger instance.</param>
    public OpenAICompatibleEmbeddingService(
        string endpoint,
        string? apiKey,
        string model,
        int dimension,
        ILogger<OpenAICompatibleEmbeddingService> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);
        ArgumentNullException.ThrowIfNull(logger);

        _model = model;
        _dimension = dimension;
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
    /// Creates an OpenAI-compatible embedding service for a well-known model without specifying dimension.
    /// The dimension is resolved automatically from <see cref="WellKnownOpenAIModels"/>.
    /// </summary>
    /// <param name="endpoint">Base API URL (e.g., "https://api.openai.com/v1").</param>
    /// <param name="apiKey">API key for authentication. Null for unauthenticated endpoints.</param>
    /// <param name="model">Embedding model name. Must be listed in <see cref="WellKnownOpenAIModels"/>.</param>
    /// <param name="logger">Logger instance.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="model"/> is not in the well-known list.
    /// Specify the dimension explicitly via the overload that accepts <c>int dimension</c>.</exception>
    public OpenAICompatibleEmbeddingService(
        string endpoint,
        string? apiKey,
        string model,
        ILogger<OpenAICompatibleEmbeddingService> logger)
        : this(endpoint, apiKey, model, ResolveWellKnownDimension(model), logger)
    {
    }

    /// <summary>
    /// Creates an OpenAI-compatible embedding service with a pre-configured HttpClient.
    /// Use this overload for testing or when custom handlers are required.
    /// </summary>
    public OpenAICompatibleEmbeddingService(
        HttpClient httpClient,
        string model,
        int dimension,
        ILogger<OpenAICompatibleEmbeddingService> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _model = model;
        _dimension = dimension;
        _logger = logger;
        _ownsHttpClient = false;
    }

    /// <inheritdoc />
    protected override async Task<float[]> EmbedCoreAsync(string text, CancellationToken cancellationToken)
    {
        LogEmbedding(_logger, _model, text.Length);

        var request = new EmbeddingRequest { Model = _model, Input = text };
        var response = await _httpClient.PostAsJsonAsync(
            "embeddings", request, JsonOptions, cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(
            JsonOptions, cancellationToken);

        var embedding = result?.Data?.FirstOrDefault()?.Embedding
            ?? throw new InvalidOperationException(
                $"No embedding returned from model '{_model}'.");

        return embedding;
    }

    /// <inheritdoc />
    public override int GetEmbeddingDimension() => _dimension;

    /// <inheritdoc />
    public override string GetModelName() => _model;

    /// <inheritdoc />
    protected override string GetProviderName() => "OpenAI";

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    private static int ResolveWellKnownDimension(string model)
    {
        if (WellKnownOpenAIModels.TryGetEmbeddingDimension(model, out int dimension))
            return dimension;

        throw new ArgumentException(
            $"Embedding dimension for model '{model}' is not in the well-known list. " +
            $"Use the overload that accepts an explicit 'int dimension' parameter, " +
            $"or call WellKnownOpenAIModels.TryGetEmbeddingDimension() to check available models.",
            nameof(model));
    }

    #region LoggerMessage

    [LoggerMessage(Level = LogLevel.Debug, Message = "Embedding with {Model} (text: {Length} chars)")]
    private static partial void LogEmbedding(ILogger logger, string model, int length);

    #endregion

    #region DTO Models

    internal sealed class EmbeddingRequest
    {
        public string Model { get; init; } = string.Empty;
        public string Input { get; init; } = string.Empty;
    }

    internal sealed class EmbeddingResponse
    {
        public List<EmbeddingDataDto>? Data { get; init; }
    }

    internal sealed class EmbeddingDataDto
    {
        public float[]? Embedding { get; init; }
    }

    #endregion
}
