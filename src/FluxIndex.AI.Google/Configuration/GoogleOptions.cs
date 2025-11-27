namespace FluxIndex.AI.Google.Configuration;

/// <summary>
/// Google Gemini API 설정 (Google AI Studio 또는 Vertex AI)
/// </summary>
public class GoogleOptions
{
    /// <summary>
    /// Google AI Studio API Key (for Google AI Studio)
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Google Cloud Project ID (for Vertex AI - optional)
    /// </summary>
    public string? ProjectId { get; set; }

    /// <summary>
    /// Google Cloud Location (for Vertex AI - e.g., "us-central1")
    /// </summary>
    public string Location { get; set; } = "us-central1";

    /// <summary>
    /// Whether to use Vertex AI instead of Google AI Studio
    /// </summary>
    public bool UseVertexAI { get; set; } = false;

    /// <summary>
    /// 기본 모델 (gemini-2.0-flash)
    /// </summary>
    public string DefaultModel { get; set; } = "gemini-2.0-flash";

    /// <summary>
    /// Fast 전략용 모델 (gemini-2.0-flash)
    /// </summary>
    public string FastModel { get; set; } = "gemini-2.0-flash";

    /// <summary>
    /// Deep 전략용 모델 (gemini-2.5-pro-preview-05-06)
    /// </summary>
    public string DeepModel { get; set; } = "gemini-2.5-pro-preview-05-06";

    /// <summary>
    /// 최대 토큰 수
    /// </summary>
    public int MaxTokens { get; set; } = 4096;

    /// <summary>
    /// Temperature (0.0 - 2.0)
    /// </summary>
    public float Temperature { get; set; } = 0.3f;

    /// <summary>
    /// Top P
    /// </summary>
    public float TopP { get; set; } = 1.0f;

    /// <summary>
    /// Top K
    /// </summary>
    public int TopK { get; set; } = 40;

    /// <summary>
    /// 타임아웃 (초)
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}
