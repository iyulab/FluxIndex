using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.AI.OpenAI.Services;

/// <summary>
/// OpenAI API 클라이언트 구현
/// Azure OpenAI와 OpenAI 모두 지원
/// </summary>
public class OpenAIClient : IOpenAIClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly OpenAIOptions _options;
    private readonly ILogger<OpenAIClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public OpenAIClient(
        HttpClient httpClient,
        IOptions<OpenAIOptions> options,
        ILogger<OpenAIClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        ConfigureHttpClient();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = false
        };
    }

    /// <summary>
    /// 텍스트 완성 요청
    /// </summary>
    public async Task<string> CompleteAsync(
        string prompt,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt cannot be empty", nameof(prompt));

        var request = CreateCompletionRequest(prompt);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            _logger.LogDebug("Sending completion request to OpenAI");

            var response = await _httpClient.PostAsync(
                GetCompletionEndpoint(),
                new StringContent(request, Encoding.UTF8, "application/json"),
                cts.Token);

            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cts.Token);
            var completion = ExtractCompletionText(responseContent);

            _logger.LogDebug("Received completion response ({Length} chars)", completion.Length);

            return completion;
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
        {
            throw new TimeoutException($"OpenAI request timed out after {timeout.TotalSeconds} seconds");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP request to OpenAI failed");
            throw new InvalidOperationException($"OpenAI request failed: {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse OpenAI response");
            throw new InvalidOperationException($"Invalid OpenAI response format: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 클라이언트 상태 확인
    /// </summary>
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var testPrompt = "Test prompt for health check";
            var result = await CompleteAsync(testPrompt, TimeSpan.FromSeconds(10), cancellationToken);
            return !string.IsNullOrWhiteSpace(result);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// HTTP 클라이언트 설정
    /// </summary>
    private void ConfigureHttpClient()
    {
        if (_options.IsAzure)
        {
            _httpClient.DefaultRequestHeaders.Add("api-key", _options.ApiKey);
        }
        else
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_options.ApiKey}");
        }

        if (!string.IsNullOrEmpty(_options.Organization))
        {
            _httpClient.DefaultRequestHeaders.Add("OpenAI-Organization", _options.Organization);
        }
    }

    /// <summary>
    /// 완성 요청 생성
    /// </summary>
    private string CreateCompletionRequest(string prompt)
    {
        var request = new
        {
            model = _options.Model,
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            max_tokens = _options.MaxTokens,
            temperature = _options.Temperature,
            top_p = _options.TopP,
            frequency_penalty = _options.FrequencyPenalty,
            presence_penalty = _options.PresencePenalty
        };

        return JsonSerializer.Serialize(request, _jsonOptions);
    }

    /// <summary>
    /// 완성 엔드포인트 URL 생성
    /// </summary>
    private string GetCompletionEndpoint()
    {
        if (_options.IsAzure)
        {
            return $"{_options.BaseUrl.TrimEnd('/')}/openai/deployments/{_options.DeploymentName}/chat/completions?api-version={_options.ApiVersion}";
        }
        else
        {
            return $"{_options.BaseUrl.TrimEnd('/')}/v1/chat/completions";
        }
    }

    /// <summary>
    /// 응답에서 완성 텍스트 추출
    /// </summary>
    private string ExtractCompletionText(string responseContent)
    {
        var document = JsonDocument.Parse(responseContent);

        if (document.RootElement.TryGetProperty("choices", out var choices) &&
            choices.ValueKind == JsonValueKind.Array &&
            choices.GetArrayLength() > 0)
        {
            var firstChoice = choices[0];
            if (firstChoice.TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var content))
            {
                return content.GetString() ?? string.Empty;
            }
        }

        throw new InvalidOperationException("Invalid OpenAI response format: missing completion content");
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

/// <summary>
/// OpenAI 클라이언트 설정 옵션
/// </summary>
public class OpenAIOptions
{
    /// <summary>
    /// API 키
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// 기본 URL (Azure OpenAI의 경우 리소스 URL)
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.openai.com";

    /// <summary>
    /// Azure OpenAI 사용 여부
    /// </summary>
    public bool IsAzure { get; set; } = false;

    /// <summary>
    /// Azure OpenAI 배포 이름 (Azure 사용 시 필수)
    /// </summary>
    public string DeploymentName { get; set; } = string.Empty;

    /// <summary>
    /// Azure OpenAI API 버전
    /// </summary>
    public string ApiVersion { get; set; } = "2024-02-01";

    /// <summary>
    /// 조직 ID (OpenAI 사용 시 선택사항)
    /// </summary>
    public string Organization { get; set; } = string.Empty;

    /// <summary>
    /// 사용할 모델명
    /// </summary>
    public string Model { get; set; } = "gpt-4";

    /// <summary>
    /// 최대 토큰 수
    /// </summary>
    public int MaxTokens { get; set; } = 1000;

    /// <summary>
    /// 창의성 수준 (0.0 ~ 2.0)
    /// </summary>
    public float Temperature { get; set; } = 0.7f;

    /// <summary>
    /// 핵심 확률 샘플링 (0.0 ~ 1.0)
    /// </summary>
    public float TopP { get; set; } = 1.0f;

    /// <summary>
    /// 빈도 페널티 (-2.0 ~ 2.0)
    /// </summary>
    public float FrequencyPenalty { get; set; } = 0.0f;

    /// <summary>
    /// 존재 페널티 (-2.0 ~ 2.0)
    /// </summary>
    public float PresencePenalty { get; set; } = 0.0f;

    /// <summary>
    /// 설정 유효성 검증
    /// </summary>
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(BaseUrl) &&
        (!IsAzure || !string.IsNullOrWhiteSpace(DeploymentName)) &&
        MaxTokens > 0 &&
        Temperature >= 0.0f && Temperature <= 2.0f &&
        TopP >= 0.0f && TopP <= 1.0f;

    /// <summary>
    /// 테스트용 설정 생성
    /// </summary>
    public static OpenAIOptions CreateForTesting() => new()
    {
        ApiKey = "test-api-key",
        BaseUrl = "https://test.openai.com",
        Model = "gpt-3.5-turbo",
        MaxTokens = 500,
        Temperature = 0.5f
    };

    /// <summary>
    /// Azure OpenAI 설정 생성
    /// </summary>
    public static OpenAIOptions CreateForAzure(
        string apiKey,
        string resourceUrl,
        string deploymentName) => new()
    {
        ApiKey = apiKey,
        BaseUrl = resourceUrl,
        IsAzure = true,
        DeploymentName = deploymentName,
        Model = "gpt-4", // Azure에서는 배포명으로 관리
        MaxTokens = 1500,
        Temperature = 0.3f
    };
}