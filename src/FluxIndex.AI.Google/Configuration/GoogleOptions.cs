namespace FluxIndex.AI.Google.Configuration;

/// <summary>
/// Google Gemini API 설정
/// </summary>
public class GoogleOptions
{
    /// <summary>
    /// Google Cloud Project ID
    /// </summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>
    /// Google Cloud Location (e.g., "us-central1")
    /// </summary>
    public string Location { get; set; } = "us-central1";

    /// <summary>
    /// 기본 모델 (gemini-1.5-pro)
    /// </summary>
    public string DefaultModel { get; set; } = "gemini-1.5-pro";

    /// <summary>
    /// Fast 전략용 모델 (gemini-1.5-flash)
    /// </summary>
    public string FastModel { get; set; } = "gemini-1.5-flash";

    /// <summary>
    /// Deep 전략용 모델 (gemini-1.5-pro)
    /// </summary>
    public string DeepModel { get; set; } = "gemini-1.5-pro";

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
