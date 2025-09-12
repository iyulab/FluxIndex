using FileFlux;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FileFluxIndexSample;

/// <summary>
/// FileFlux용 OpenAI Vision API를 사용한 이미지-텍스트 변환 서비스
/// </summary>
public class OpenAIImageToTextService : IImageToTextService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly IConfiguration _configuration;

    public OpenAIImageToTextService(
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _apiKey = configuration["OpenAI:ApiKey"] 
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") 
            ?? throw new InvalidOperationException("OpenAI API key not configured");
        
        _httpClient.BaseAddress = new Uri("https://api.openai.com/v1/");
        _httpClient.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", _apiKey);
    }

    public async Task<string> ExtractTextFromImageAsync(
        byte[] imageData,
        string mimeType = "image/png",
        CancellationToken cancellationToken = default)
    {
        // Base64 인코딩
        var base64Image = Convert.ToBase64String(imageData);
        var dataUrl = $"data:{mimeType};base64,{base64Image}";

        var request = new
        {
            model = "gpt-5-nano",
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = "Extract and describe all text and important visual information from this image. Format the output as structured text suitable for document search and retrieval." },
                        new { type = "image_url", image_url = new { url = dataUrl } }
                    }
                }
            },
            max_tokens = 1000
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

    public async Task<ImageAnalysisResult> AnalyzeImageAsync(
        byte[] imageData,
        string mimeType = "image/png",
        CancellationToken cancellationToken = default)
    {
        var base64Image = Convert.ToBase64String(imageData);
        var dataUrl = $"data:{mimeType};base64,{base64Image}";

        var request = new
        {
            model = "gpt-5-nano",
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = @"Analyze this image and provide:
1. Main subject/content type (diagram, photo, chart, etc.)
2. Any text present
3. Key visual elements
4. Suggested keywords for indexing

Respond in JSON format:
{
  ""contentType"": ""string"",
  ""textContent"": ""string"",
  ""visualElements"": [""element1"", ""element2""],
  ""keywords"": [""keyword1"", ""keyword2""]
}" },
                        new { type = "image_url", image_url = new { url = dataUrl } }
                    }
                }
            },
            max_tokens = 500
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
            .GetString() ?? "{}";

        try
        {
            using var analysisDoc = JsonDocument.Parse(messageContent);
            var root = analysisDoc.RootElement;
            
            return new ImageAnalysisResult
            {
                ContentType = root.TryGetProperty("contentType", out var ct) ? ct.GetString() ?? "unknown" : "unknown",
                TextContent = root.TryGetProperty("textContent", out var tc) ? tc.GetString() ?? "" : "",
                VisualElements = root.TryGetProperty("visualElements", out var ve) 
                    ? ve.EnumerateArray().Select(e => e.GetString() ?? "").ToList()
                    : new List<string>(),
                Keywords = root.TryGetProperty("keywords", out var kw)
                    ? kw.EnumerateArray().Select(e => e.GetString() ?? "").ToList()
                    : new List<string>()
            };
        }
        catch
        {
            // 파싱 실패 시 기본 결과 반환
            return new ImageAnalysisResult
            {
                ContentType = "image",
                TextContent = messageContent,
                VisualElements = new List<string>(),
                Keywords = new List<string>()
            };
        }
    }

    public async Task<bool> ContainsTextAsync(
        byte[] imageData,
        CancellationToken cancellationToken = default)
    {
        var base64Image = Convert.ToBase64String(imageData);
        var dataUrl = $"data:image/png;base64,{base64Image}";

        var request = new
        {
            model = "gpt-5-nano",
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = "Does this image contain any readable text? Answer with just 'yes' or 'no'." },
                        new { type = "image_url", image_url = new { url = dataUrl } }
                    }
                }
            },
            max_tokens = 10
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
            .GetString() ?? "";

        return messageContent.ToLower().Contains("yes");
    }
}

public class ImageAnalysisResult
{
    public string ContentType { get; set; } = string.Empty;
    public string TextContent { get; set; } = string.Empty;
    public List<string> VisualElements { get; set; } = new();
    public List<string> Keywords { get; set; } = new();
}