using FluxIndex.AI.Google.Configuration;
using FluxIndex.AI.Google.Services;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.AI.Google.Extensions;

/// <summary>
/// Google Gemini AI 서비스 등록 확장 메서드
/// Note: Google Gemini does not provide embedding APIs via this SDK, use LocalEmbedder for embeddings
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Google Gemini 텍스트 완성 서비스 등록
    /// Note: Embedding service must be registered separately (recommend LocalEmbedder)
    /// </summary>
    public static IServiceCollection AddGoogleTextCompletion(
        this IServiceCollection services,
        Action<GoogleOptions> configureOptions)
    {
        services.Configure(configureOptions);
        services.AddSingleton<ITextCompletionService, GoogleTextCompletionService>();
        services.AddMemoryCache();

        return services;
    }

    /// <summary>
    /// Google Gemini 서비스 등록 (텍스트 완성만)
    /// Note: Embedding service must be registered separately (recommend LocalEmbedder)
    /// </summary>
    public static IServiceCollection AddGoogleServices(
        this IServiceCollection services,
        Action<GoogleOptions> configureOptions)
    {
        return services.AddGoogleTextCompletion(configureOptions);
    }
}
