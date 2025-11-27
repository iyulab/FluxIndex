using FluxIndex.AI.Google.Configuration;
using FluxIndex.Core.Application.Interfaces;
using Mscc.GenerativeAI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.AI.Google.Services;

/// <summary>
/// Google Gemini 기반 텍스트 완성 서비스 (Google AI Studio API 사용)
/// </summary>
public class GoogleTextCompletionService : ITextCompletionService
{
    private readonly GoogleOptions _options;
    private readonly ILogger<GoogleTextCompletionService> _logger;
    private readonly IGenerativeAI _googleAI;

    public GoogleTextCompletionService(
        IOptions<GoogleOptions> options,
        ILogger<GoogleTextCompletionService> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrEmpty(_options.ApiKey))
        {
            throw new ArgumentException("Google AI API key is required", nameof(options));
        }

        _googleAI = new GoogleAI(apiKey: _options.ApiKey);
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
            var model = _googleAI.GenerativeModel(model: _options.DefaultModel);

            var generationConfig = new GenerationConfig
            {
                Temperature = temperature,
                TopP = _options.TopP,
                TopK = _options.TopK,
                MaxOutputTokens = maxTokens
            };

            var response = await model.GenerateContent(prompt, generationConfig);

            if (response == null || string.IsNullOrEmpty(response.Text))
            {
                throw new InvalidOperationException("Google Gemini API returned empty response");
            }

            _logger.LogInformation("Google Gemini API call successful. Model: {Model}", _options.DefaultModel);

            return response.Text;
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
            var model = _googleAI.GenerativeModel(model: _options.DefaultModel);

            var generationConfig = new GenerationConfig
            {
                Temperature = _options.Temperature,
                TopP = _options.TopP,
                TopK = _options.TopK,
                MaxOutputTokens = maxTokens,
                ResponseMimeType = "application/json"
            };

            var jsonPrompt = prompt + "\n\nReturn ONLY valid JSON (no markdown, no explanations):";
            var response = await model.GenerateContent(jsonPrompt, generationConfig);

            if (response == null || string.IsNullOrEmpty(response.Text))
            {
                throw new InvalidOperationException("Google Gemini API returned empty response");
            }

            _logger.LogInformation("Google Gemini JSON API call successful. Model: {Model}", _options.DefaultModel);

            return response.Text;
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
