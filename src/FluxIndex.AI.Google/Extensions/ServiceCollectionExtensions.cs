using FluxIndex.AI.Google.Configuration;
using FluxIndex.AI.Google.Services;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.AI.Google.Extensions;

/// <summary>
/// Google Gemini AI 서비스 등록 확장 메서드
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Google Gemini 텍스트 완성 서비스 등록
    /// </summary>
    public static IServiceCollection AddGoogleTextCompletion(
        this IServiceCollection services,
        Action<GoogleOptions> configureOptions)
    {
        services.Configure(configureOptions);
        services.AddSingleton<ITextCompletionService, GoogleTextCompletionService>();

        return services;
    }
}
