using FluxIndex.AI.OpenAI.Services;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace FluxIndex.AI.OpenAI.Extensions;

/// <summary>
/// 쿼리 변환 서비스 등록을 위한 확장 메서드
/// </summary>
public static class QueryTransformationServiceCollectionExtensions
{
    /// <summary>
    /// OpenAI 기반 쿼리 변환 서비스 등록 (설정 파일 기반)
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configuration">설정</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddOpenAIQueryTransformation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 기본 OpenAI 클라이언트가 등록되어 있는지 확인
        services.AddHttpClient<IOpenAIClient, OpenAIClient>();

        // HyDE 서비스 옵션 등록
        services.Configure<HyDEServiceOptions>(
            configuration.GetSection("QueryTransformation:HyDE"));

        // QuOTE 서비스 옵션 등록
        services.Configure<QuOTEServiceOptions>(
            configuration.GetSection("QueryTransformation:QuOTE"));

        // 전체 쿼리 변환 옵션 등록
        services.Configure<QueryTransformationOptions>(
            configuration.GetSection("QueryTransformation"));

        // 개별 서비스 등록
        services.AddScoped<HyDEService>();
        services.AddScoped<QuOTEService>();

        // 통합 서비스 등록
        services.AddScoped<IQueryTransformationService, OpenAIQueryTransformationService>();

        return services;
    }

    /// <summary>
    /// OpenAI 기반 쿼리 변환 서비스 등록 (직접 설정)
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configureOpenAI">OpenAI 옵션 설정</param>
    /// <param name="configureHyDE">HyDE 옵션 설정</param>
    /// <param name="configureQuOTE">QuOTE 옵션 설정</param>
    /// <param name="configureGeneral">일반 쿼리 변환 옵션 설정</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddOpenAIQueryTransformation(
        this IServiceCollection services,
        Action<OpenAIOptions> configureOpenAI,
        Action<HyDEServiceOptions>? configureHyDE = null,
        Action<QuOTEServiceOptions>? configureQuOTE = null,
        Action<QueryTransformationOptions>? configureGeneral = null)
    {
        // OpenAI 클라이언트 등록
        services.Configure(configureOpenAI);
        services.AddHttpClient<IOpenAIClient, OpenAIClient>();

        // HyDE 서비스 옵션
        if (configureHyDE != null)
            services.Configure(configureHyDE);
        else
            services.Configure<HyDEServiceOptions>(_ => { });

        // QuOTE 서비스 옵션
        if (configureQuOTE != null)
            services.Configure(configureQuOTE);
        else
            services.Configure<QuOTEServiceOptions>(_ => { });

        // 일반 쿼리 변환 옵션
        if (configureGeneral != null)
            services.Configure(configureGeneral);
        else
            services.Configure<QueryTransformationOptions>(_ => { });

        // 개별 서비스 등록
        services.AddScoped<HyDEService>();
        services.AddScoped<QuOTEService>();

        // 통합 서비스 등록
        services.AddScoped<IQueryTransformationService, OpenAIQueryTransformationService>();

        return services;
    }

    /// <summary>
    /// Azure OpenAI 기반 쿼리 변환 서비스 등록
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="apiKey">Azure OpenAI API 키</param>
    /// <param name="resourceUrl">Azure OpenAI 리소스 URL</param>
    /// <param name="deploymentName">배포 이름</param>
    /// <param name="configureHyDE">HyDE 옵션 설정</param>
    /// <param name="configureQuOTE">QuOTE 옵션 설정</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddAzureOpenAIQueryTransformation(
        this IServiceCollection services,
        string apiKey,
        string resourceUrl,
        string deploymentName,
        Action<HyDEServiceOptions>? configureHyDE = null,
        Action<QuOTEServiceOptions>? configureQuOTE = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key cannot be empty", nameof(apiKey));

        if (string.IsNullOrWhiteSpace(resourceUrl))
            throw new ArgumentException("Resource URL cannot be empty", nameof(resourceUrl));

        if (string.IsNullOrWhiteSpace(deploymentName))
            throw new ArgumentException("Deployment name cannot be empty", nameof(deploymentName));

        return services.AddOpenAIQueryTransformation(
            openAI =>
            {
                openAI.ApiKey = apiKey;
                openAI.BaseUrl = resourceUrl;
                openAI.IsAzure = true;
                openAI.DeploymentName = deploymentName;
            },
            configureHyDE,
            configureQuOTE);
    }

    /// <summary>
    /// 테스트용 쿼리 변환 서비스 등록
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="mockClient">Mock OpenAI 클라이언트</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddTestQueryTransformation(
        this IServiceCollection services,
        IOpenAIClient? mockClient = null)
    {
        // 테스트용 옵션 등록
        services.Configure<OpenAIOptions>(_ => OpenAIOptions.CreateForTesting());
        services.Configure<HyDEServiceOptions>(_ => HyDEServiceOptions.CreateForTesting());
        services.Configure<QuOTEServiceOptions>(_ => QuOTEServiceOptions.CreateForTesting());
        services.Configure<QueryTransformationOptions>(_ => QueryTransformationOptions.CreateForTesting());

        if (mockClient != null)
        {
            // Mock 클라이언트 사용
            services.AddSingleton(mockClient);
        }
        else
        {
            // 기본 HTTP 클라이언트 등록
            services.AddHttpClient<IOpenAIClient, OpenAIClient>();
        }

        // 개별 서비스 등록
        services.AddScoped<HyDEService>();
        services.AddScoped<QuOTEService>();

        // 통합 서비스 등록
        services.AddScoped<IQueryTransformationService, OpenAIQueryTransformationService>();

        return services;
    }

    /// <summary>
    /// HyDE만 등록 (QuOTE 없이)
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configureOpenAI">OpenAI 옵션 설정</param>
    /// <param name="configureHyDE">HyDE 옵션 설정</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddHyDEOnly(
        this IServiceCollection services,
        Action<OpenAIOptions> configureOpenAI,
        Action<HyDEServiceOptions>? configureHyDE = null)
    {
        // OpenAI 클라이언트 등록
        services.Configure(configureOpenAI);
        services.AddHttpClient<IOpenAIClient, OpenAIClient>();

        // HyDE 서비스 옵션
        if (configureHyDE != null)
            services.Configure(configureHyDE);
        else
            services.Configure<HyDEServiceOptions>(_ => { });

        // HyDE 서비스만 등록
        services.AddScoped<HyDEService>();

        return services;
    }

    /// <summary>
    /// QuOTE만 등록 (HyDE 없이)
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configureOpenAI">OpenAI 옵션 설정</param>
    /// <param name="configureQuOTE">QuOTE 옵션 설정</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddQuOTEOnly(
        this IServiceCollection services,
        Action<OpenAIOptions> configureOpenAI,
        Action<QuOTEServiceOptions>? configureQuOTE = null)
    {
        // OpenAI 클라이언트 등록
        services.Configure(configureOpenAI);
        services.AddHttpClient<IOpenAIClient, OpenAIClient>();

        // QuOTE 서비스 옵션
        if (configureQuOTE != null)
            services.Configure(configureQuOTE);
        else
            services.Configure<QuOTEServiceOptions>(_ => { });

        // QuOTE 서비스만 등록
        services.AddScoped<QuOTEService>();

        return services;
    }

    /// <summary>
    /// 설정 검증
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection ValidateQueryTransformationConfiguration(this IServiceCollection services)
    {
        services.AddOptions<OpenAIOptions>()
            .Validate(options => options.IsValid, "Invalid OpenAI configuration")
            .ValidateOnStart();

        services.AddOptions<HyDEServiceOptions>()
            .Validate(options => options.IsValid, "Invalid HyDE service configuration")
            .ValidateOnStart();

        services.AddOptions<QuOTEServiceOptions>()
            .Validate(options => options.IsValid, "Invalid QuOTE service configuration")
            .ValidateOnStart();

        services.AddOptions<QueryTransformationOptions>()
            .Validate(options => options.IsValid, "Invalid query transformation configuration")
            .ValidateOnStart();

        return services;
    }
}

