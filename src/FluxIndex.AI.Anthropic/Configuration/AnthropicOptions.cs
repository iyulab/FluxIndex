namespace FluxIndex.AI.Anthropic.Configuration;

/// <summary>
/// Anthropic Claude API 설정
/// </summary>
public class AnthropicOptions
{
    /// <summary>
    /// Anthropic API Key
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// 기본 모델 (claude-3-5-sonnet-20241022)
    /// </summary>
    public string DefaultModel { get; set; } = "claude-3-5-sonnet-20241022";

    /// <summary>
    /// Fast 전략용 모델 (claude-3-5-haiku-20241022)
    /// </summary>
    public string FastModel { get; set; } = "claude-3-5-haiku-20241022";

    /// <summary>
    /// Deep 전략용 모델 (claude-3-opus-20240229)
    /// </summary>
    public string DeepModel { get; set; } = "claude-3-opus-20240229";

    /// <summary>
    /// 최대 토큰 수
    /// </summary>
    public int MaxTokens { get; set; } = 4096;

    /// <summary>
    /// Temperature (0.0 - 1.0)
    /// </summary>
    public double Temperature { get; set; } = 0.3;

    /// <summary>
    /// Top P
    /// </summary>
    public double TopP { get; set; } = 1.0;

    /// <summary>
    /// 타임아웃 (초)
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}
