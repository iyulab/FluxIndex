using FluxIndex.AI.Google.Configuration;
using FluxIndex.Core.Application.Interfaces;
using Google.Cloud.AIPlatform.V1;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.AI.Google.Services;

/// <summary>
/// Google Gemini 기반 텍스트 완성 서비스
/// </summary>
public class GoogleTextCompletionService : ITextCompletionService
{
    private readonly GoogleOptions _options;
    private readonly ILogger<GoogleTextCompletionService> _logger;
    private readonly PredictionServiceClient _client;

    public GoogleTextCompletionService(
        IOptions<GoogleOptions> options,
        ILogger<GoogleTextCompletionService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = PredictionServiceClient.Create();
    }

    public async Task<string> GenerateCompletionAsync(
        string prompt,
        int maxTokens = 500,
        float temperature = 0.7f,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Calling Google Gemini API with model: {Model}", _options.DefaultModel);

        try
        {
            var endpoint = $"projects/{_options.ProjectId}/locations/{_options.Location}/publishers/google/models/{_options.DefaultModel}";

            var content = new Content
            {
                Parts = { new Part { Text = prompt } }
            };

            var generateContentRequest = new GenerateContentRequest
            {
                Model = endpoint,
                Contents = { content },
                GenerationConfig = new GenerationConfig
                {
                    Temperature = temperature,
                    TopP = _options.TopP,
                    TopK = _options.TopK,
                    MaxOutputTokens = maxTokens
                }
            };

            var response = await _client.GenerateContentAsync(generateContentRequest, cancellationToken);

            if (response?.Candidates == null || response.Candidates.Count == 0)
            {
                throw new InvalidOperationException("Google Gemini API returned empty response");
            }

            var candidate = response.Candidates[0];
            if (candidate.Content?.Parts == null || candidate.Content.Parts.Count == 0)
            {
                throw new InvalidOperationException("Google Gemini API response does not contain text content");
            }

            var text = candidate.Content.Parts[0].Text;
            _logger.LogInformation("Google Gemini API call successful");

            return text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Google Gemini API");
            throw;
        }
    }

    public async Task<string> GenerateJsonCompletionAsync(
        string prompt,
        int maxTokens = 500,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Calling Google Gemini API for JSON completion with model: {Model}", _options.DefaultModel);

        try
        {
            var endpoint = $"projects/{_options.ProjectId}/locations/{_options.Location}/publishers/google/models/{_options.DefaultModel}";

            var content = new Content
            {
                Parts = { new Part { Text = prompt + "\n\nReturn ONLY valid JSON (no markdown, no explanations):" } }
            };

            var generateContentRequest = new GenerateContentRequest
            {
                Model = endpoint,
                Contents = { content },
                GenerationConfig = new GenerationConfig
                {
                    Temperature = _options.Temperature,
                    TopP = _options.TopP,
                    TopK = _options.TopK,
                    MaxOutputTokens = maxTokens
                }
            };

            var response = await _client.GenerateContentAsync(generateContentRequest, cancellationToken);

            if (response?.Candidates == null || response.Candidates.Count == 0)
            {
                throw new InvalidOperationException("Google Gemini API returned empty response");
            }

            var candidate = response.Candidates[0];
            if (candidate.Content?.Parts == null || candidate.Content.Parts.Count == 0)
            {
                throw new InvalidOperationException("Google Gemini API response does not contain text content");
            }

            var text = candidate.Content.Parts[0].Text;
            _logger.LogInformation("Google Gemini JSON API call successful");

            return text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Google Gemini API for JSON completion");
            throw;
        }
    }

    public int CountTokens(string text)
    {
        // Rough approximation for Gemini: 1 token ≈ 4 characters
        return text.Length / 4;
    }
}
