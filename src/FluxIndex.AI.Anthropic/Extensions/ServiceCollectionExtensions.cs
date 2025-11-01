using FluxIndex.AI.Anthropic.Configuration;
using FluxIndex.AI.Anthropic.Services;
using FluxIndex.Core.Interfaces;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.AI.Anthropic.Extensions;

/// <summary>
/// Anthropic Claude AI 서비스 등록 확장 메서드
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Anthropic Claude 메타데이터 추출기 등록
    /// </summary>
    public static IServiceCollection AddAnthropicMetadataExtractor(
        this IServiceCollection services,
        Action<AnthropicOptions> configureOptions)
    {
        services.Configure(configureOptions);
        services.AddSingleton<ITextCompletionService, AnthropicTextCompletionService>();
        services.AddSingleton<IMetadataExtractor, AnthropicMetadataExtractor>();

        return services;
    }

    /// <summary>
    /// Anthropic Claude 텍스트 완성 서비스만 등록
    /// </summary>
    public static IServiceCollection AddAnthropicTextCompletion(
        this IServiceCollection services,
        Action<AnthropicOptions> configureOptions)
    {
        services.Configure(configureOptions);
        services.AddSingleton<ITextCompletionService, AnthropicTextCompletionService>();

        return services;
    }
}
