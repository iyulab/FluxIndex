namespace FluxIndex.Core.Application.Configuration;

/// <summary>
/// Configuration options for Query Orchestrator
/// </summary>
public class QueryOrchestratorOptions
{
    /// <summary>
    /// Azure OpenAI endpoint
    /// </summary>
    public string AzureOpenAIEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Azure OpenAI API key
    /// </summary>
    public string AzureOpenAIApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Azure OpenAI deployment name (e.g., "gpt-5-nano", "gpt-35-turbo")
    /// </summary>
    public string DeploymentName { get; set; } = "gpt-5-nano";

    /// <summary>
    /// Maximum tokens for HyDE document generation
    /// </summary>
    public int HyDEMaxTokens { get; set; } = 300;

    /// <summary>
    /// Maximum number of sub-queries for decomposition
    /// </summary>
    public int MaxSubQueries { get; set; } = 3;

    /// <summary>
    /// Temperature for LLM generation (0.0 - 1.0)
    /// </summary>
    public float Temperature { get; set; } = 0.7f;

    /// <summary>
    /// Enable query caching
    /// </summary>
    public bool EnableQueryCache { get; set; } = true;

    /// <summary>
    /// Cache duration for transformed queries
    /// </summary>
    public TimeSpan QueryCacheDuration { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Enable automatic strategy selection based on query analysis
    /// </summary>
    public bool EnableAdaptiveStrategy { get; set; } = true;

    /// <summary>
    /// Minimum confidence score for strategy recommendation
    /// </summary>
    public float MinConfidenceThreshold { get; set; } = 0.7f;
}