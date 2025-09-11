namespace FluxIndex.AI.OpenAI;

/// <summary>
/// OpenAI 서비스 설정 옵션
/// </summary>
public class OpenAIOptions
{
    /// <summary>
    /// OpenAI 또는 Azure OpenAI API 키
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
    
    /// <summary>
    /// Azure OpenAI 엔드포인트 (Azure 사용 시)
    /// 예: https://your-resource.openai.azure.com/
    /// </summary>
    public string? AzureEndpoint { get; set; }
    
    /// <summary>
    /// Azure OpenAI 배포 이름 (Azure 사용 시)
    /// </summary>
    public string? DeploymentName { get; set; }
    
    /// <summary>
    /// 임베딩 모델 이름
    /// OpenAI: text-embedding-3-small, text-embedding-3-large, text-embedding-ada-002
    /// Azure: deployment name을 사용
    /// </summary>
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    
    /// <summary>
    /// 최대 토큰 수
    /// </summary>
    public int MaxTokens { get; set; } = 8191;
    
    /// <summary>
    /// 배치 처리 크기
    /// </summary>
    public int BatchSize { get; set; } = 100;
    
    /// <summary>
    /// 최대 재시도 횟수
    /// </summary>
    public int MaxRetries { get; set; } = 3;
    
    /// <summary>
    /// 최대 동시 요청 수
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 10;
    
    /// <summary>
    /// Rate limit 지연 시간 (밀리초)
    /// </summary>
    public int RateLimitDelay { get; set; } = 1000;
}