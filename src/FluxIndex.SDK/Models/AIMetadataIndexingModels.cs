using FluxIndex.Core.Models;

namespace FluxIndex.SDK;

/// <summary>
/// AI 메타데이터 추출 확장 (IndexingOptions 확장용)
/// 별도 파일로 정의 후 나중에 IndexingOptions에 병합
/// </summary>
public static class IndexingOptionsExtensions
{
    /// <summary>
    /// AI 메타데이터 추출 활성화 여부 확인
    /// </summary>
    public static bool ShouldExtractAIMetadata(this IndexingOptions options)
    {
        return options.CustomOptions.TryGetValue("EnableAIMetadataExtraction", out var value)
            && value is bool enabled && enabled;
    }

    /// <summary>
    /// AI 메타데이터 추출 옵션 설정
    /// </summary>
    public static IndexingOptions WithAIMetadataExtraction(
        this IndexingOptions options,
        MetadataSchema schema = MetadataSchema.General,
        MetadataExtractionStrategy strategy = MetadataExtractionStrategy.Smart,
        float minConfidence = 0.6f)
    {
        options.CustomOptions["EnableAIMetadataExtraction"] = true;
        options.CustomOptions["MetadataSchema"] = schema.ToString();
        options.CustomOptions["MetadataExtractionStrategy"] = strategy.ToString();
        options.CustomOptions["MinMetadataConfidence"] = minConfidence;
        return options;
    }

    /// <summary>
    /// 커스텀 프롬프트로 AI 메타데이터 추출 설정
    /// </summary>
    public static IndexingOptions WithCustomMetadataPrompt(
        this IndexingOptions options,
        string customPrompt,
        MetadataExtractionStrategy strategy = MetadataExtractionStrategy.Smart)
    {
        options.CustomOptions["EnableAIMetadataExtraction"] = true;
        options.CustomOptions["MetadataSchema"] = MetadataSchema.Custom.ToString();
        options.CustomOptions["MetadataExtractionStrategy"] = strategy.ToString();
        options.CustomOptions["CustomMetadataPrompt"] = customPrompt;
        return options;
    }

    /// <summary>
    /// AI 메타데이터 추출 스키마 조회
    /// </summary>
    public static MetadataSchema GetMetadataSchema(this IndexingOptions options)
    {
        if (options.CustomOptions.TryGetValue("MetadataSchema", out var value) && value is string schemaStr)
        {
            return Enum.TryParse<MetadataSchema>(schemaStr, out var schema) ? schema : MetadataSchema.General;
        }
        return MetadataSchema.General;
    }

    /// <summary>
    /// AI 메타데이터 추출 전략 조회
    /// </summary>
    public static MetadataExtractionStrategy GetMetadataExtractionStrategy(this IndexingOptions options)
    {
        if (options.CustomOptions.TryGetValue("MetadataExtractionStrategy", out var value) && value is string strategyStr)
        {
            return Enum.TryParse<MetadataExtractionStrategy>(strategyStr, out var strategy)
                ? strategy
                : MetadataExtractionStrategy.Smart;
        }
        return MetadataExtractionStrategy.Smart;
    }

    /// <summary>
    /// 최소 신뢰도 임계값 조회
    /// </summary>
    public static float GetMinMetadataConfidence(this IndexingOptions options)
    {
        if (options.CustomOptions.TryGetValue("MinMetadataConfidence", out var value))
        {
            return value switch
            {
                float f => f,
                double d => (float)d,
                _ => 0.6f
            };
        }
        return 0.6f;
    }

    /// <summary>
    /// 커스텀 프롬프트 조회
    /// </summary>
    public static string? GetCustomMetadataPrompt(this IndexingOptions options)
    {
        return options.CustomOptions.TryGetValue("CustomMetadataPrompt", out var value) && value is string prompt
            ? prompt
            : null;
    }
}

/// <summary>
/// IndexingResult에 AI 메타데이터 추출 결과 확장
/// </summary>
public static class IndexingResultExtensions
{
    /// <summary>
    /// AI 추출 메타데이터 설정
    /// </summary>
    public static void SetExtractedMetadata(this IndexingResult result, ExtractedMetadata metadata)
    {
        result.Metadata["AIExtractedMetadata"] = metadata;
        result.Metadata["MetadataExtractionMethod"] = metadata.ExtractionMethod;
        result.Metadata["MetadataConfidence"] = metadata.OverallConfidence;
    }

    /// <summary>
    /// AI 추출 메타데이터 조회
    /// </summary>
    public static ExtractedMetadata? GetExtractedMetadata(this IndexingResult result)
    {
        return result.Metadata.TryGetValue("AIExtractedMetadata", out var value) && value is ExtractedMetadata metadata
            ? metadata
            : null;
    }

    /// <summary>
    /// 메타데이터 추출 성공 여부
    /// </summary>
    public static bool HasExtractedMetadata(this IndexingResult result)
    {
        return result.Metadata.ContainsKey("AIExtractedMetadata");
    }
}
