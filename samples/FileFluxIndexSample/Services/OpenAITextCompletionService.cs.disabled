using FileFlux;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FileFluxIndexSample;

/// <summary>
/// FileFlux용 OpenAI 텍스트 완성 서비스 구현
/// </summary>
public class OpenAITextCompletionService : ITextCompletionService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly IConfiguration _configuration;

    public OpenAITextCompletionService(
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _apiKey = configuration["OpenAI:ApiKey"] 
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") 
            ?? throw new InvalidOperationException("OpenAI API key not configured");
        _model = configuration["OpenAI:Model"] ?? "gpt-5-nano";
        
        _httpClient.BaseAddress = new Uri("https://api.openai.com/v1/");
        _httpClient.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", _apiKey);
    }

    public async Task<string> CompleteAsync(
        string prompt,
        int maxTokens = 1000,
        double temperature = 0.7,
        CancellationToken cancellationToken = default)
    {
        var request = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = "You are a helpful assistant that processes and analyzes documents." },
                new { role = "user", content = prompt }
            },
            max_tokens = maxTokens,
            temperature = temperature
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("chat/completions", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseJson);
        
        var messageContent = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return messageContent ?? string.Empty;
    }

    public async Task<ChunkQualityScore> EvaluateChunkQualityAsync(
        string chunk,
        CancellationToken cancellationToken = default)
    {
        var prompt = $@"Evaluate the quality of this text chunk for RAG retrieval:

{chunk}

Rate the following aspects from 0.0 to 1.0:
1. Content Completeness: Does the chunk contain complete thoughts/ideas?
2. Information Density: How much useful information is contained?
3. Coherence: Is the text coherent and well-structured?

Respond in JSON format:
{{
  ""completeness"": 0.0-1.0,
  ""density"": 0.0-1.0,
  ""coherence"": 0.0-1.0,
  ""overall"": 0.0-1.0
}}";

        var result = await CompleteAsync(prompt, 200, 0.3, cancellationToken);
        
        try
        {
            using var doc = JsonDocument.Parse(result);
            return new ChunkQualityScore
            {
                ContentCompleteness = doc.RootElement.GetProperty("completeness").GetDouble(),
                InformationDensity = doc.RootElement.GetProperty("density").GetDouble(),
                Coherence = doc.RootElement.GetProperty("coherence").GetDouble(),
                OverallScore = doc.RootElement.GetProperty("overall").GetDouble()
            };
        }
        catch
        {
            // 파싱 실패 시 기본값 반환
            return new ChunkQualityScore
            {
                ContentCompleteness = 0.5,
                InformationDensity = 0.5,
                Coherence = 0.5,
                OverallScore = 0.5
            };
        }
    }

    public async Task<string> SummarizeChunkAsync(
        string chunk,
        int maxLength = 100,
        CancellationToken cancellationToken = default)
    {
        var prompt = $@"Summarize the following text chunk in {maxLength} words or less:

{chunk}

Summary:";

        return await CompleteAsync(prompt, maxLength * 2, 0.5, cancellationToken);
    }

    public async Task<List<string>> ExtractKeywordsAsync(
        string chunk,
        int maxKeywords = 10,
        CancellationToken cancellationToken = default)
    {
        var prompt = $@"Extract up to {maxKeywords} key terms or phrases from this text:

{chunk}

Return only the keywords as a comma-separated list:";

        var result = await CompleteAsync(prompt, 100, 0.3, cancellationToken);
        
        return result.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(k => k.Trim())
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .ToList();
    }
}

public class ChunkQualityScore
{
    public double ContentCompleteness { get; set; }
    public double InformationDensity { get; set; }
    public double Coherence { get; set; }
    public double OverallScore { get; set; }
}