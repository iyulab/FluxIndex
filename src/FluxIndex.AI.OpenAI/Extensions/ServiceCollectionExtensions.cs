using FluxIndex.AI.OpenAI.Services;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;

namespace FluxIndex.AI.OpenAI.Extensions;

/// <summary>
/// OpenAI 메타데이터 추출 서비스 등록을 위한 확장 메서드
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// OpenAI 메타데이터 추출 서비스 등록
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configuration">설정</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddOpenAIMetadataExtraction(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // OpenAI 옵션 등록
        services.Configure<OpenAIOptions>(
            configuration.GetSection("OpenAI"));

        // 메타데이터 추출 옵션 등록
        services.Configure<MetadataExtractionOptions>(
            configuration.GetSection("MetadataExtraction"));

        // HTTP 클라이언트 등록
        services.AddHttpClient<IOpenAIClient, OpenAIClient>((serviceProvider, client) =>
        {
            client.Timeout = TimeSpan.FromMinutes(2);
            client.DefaultRequestHeaders.Add("User-Agent", "FluxIndex/1.0");
        });

        // 메타데이터 추출 서비스 등록
        services.AddScoped<IMetadataEnrichmentService, OpenAIMetadataEnrichmentService>();

        return services;
    }

    /// <summary>
    /// OpenAI 메타데이터 추출 서비스 등록 (직접 설정)
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configureOpenAI">OpenAI 옵션 설정</param>
    /// <param name="configureExtraction">메타데이터 추출 옵션 설정</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddOpenAIMetadataExtraction(
        this IServiceCollection services,
        Action<OpenAIOptions> configureOpenAI,
        Action<MetadataExtractionOptions>? configureExtraction = null)
    {
        // OpenAI 옵션 등록
        services.Configure(configureOpenAI);

        // 메타데이터 추출 옵션 등록
        if (configureExtraction != null)
        {
            services.Configure(configureExtraction);
        }
        else
        {
            services.Configure<MetadataExtractionOptions>(_ => { });
        }

        // HTTP 클라이언트 등록
        services.AddHttpClient<IOpenAIClient, OpenAIClient>((serviceProvider, client) =>
        {
            client.Timeout = TimeSpan.FromMinutes(2);
            client.DefaultRequestHeaders.Add("User-Agent", "FluxIndex/1.0");
        });

        // 메타데이터 추출 서비스 등록
        services.AddScoped<IMetadataEnrichmentService, OpenAIMetadataEnrichmentService>();

        return services;
    }

    /// <summary>
    /// Azure OpenAI 메타데이터 추출 서비스 등록
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="apiKey">Azure OpenAI API 키</param>
    /// <param name="resourceUrl">Azure OpenAI 리소스 URL</param>
    /// <param name="deploymentName">배포 이름</param>
    /// <param name="configureExtraction">메타데이터 추출 옵션 설정</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddAzureOpenAIMetadataExtraction(
        this IServiceCollection services,
        string apiKey,
        string resourceUrl,
        string deploymentName,
        Action<MetadataExtractionOptions>? configureExtraction = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key cannot be empty", nameof(apiKey));

        if (string.IsNullOrWhiteSpace(resourceUrl))
            throw new ArgumentException("Resource URL cannot be empty", nameof(resourceUrl));

        if (string.IsNullOrWhiteSpace(deploymentName))
            throw new ArgumentException("Deployment name cannot be empty", nameof(deploymentName));

        return services.AddOpenAIMetadataExtraction(
            options =>
            {
                options.ApiKey = apiKey;
                options.BaseUrl = resourceUrl;
                options.IsAzure = true;
                options.DeploymentName = deploymentName;
            },
            configureExtraction);
    }

    /// <summary>
    /// 테스트용 OpenAI 메타데이터 추출 서비스 등록
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="mockClient">Mock OpenAI 클라이언트</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddTestOpenAIMetadataExtraction(
        this IServiceCollection services,
        IOpenAIClient? mockClient = null)
    {
        // 테스트용 옵션 등록
        services.Configure<OpenAIOptions>(_ => OpenAIOptions.CreateForTesting());
        services.Configure<MetadataExtractionOptions>(_ => MetadataExtractionOptions.CreateForTesting());

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

        // 메타데이터 추출 서비스 등록
        services.AddScoped<IMetadataEnrichmentService, OpenAIMetadataEnrichmentService>();

        return services;
    }

    /// <summary>
    /// OpenAI 클라이언트만 등록 (메타데이터 추출 서비스 제외)
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configureOptions">OpenAI 옵션 설정</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddOpenAIClient(
        this IServiceCollection services,
        Action<OpenAIOptions> configureOptions)
    {
        services.Configure(configureOptions);

        services.AddHttpClient<IOpenAIClient, OpenAIClient>((serviceProvider, client) =>
        {
            client.Timeout = TimeSpan.FromMinutes(2);
            client.DefaultRequestHeaders.Add("User-Agent", "FluxIndex/1.0");
        });

        return services;
    }
}

/// <summary>
/// 설정 검증을 위한 확장 메서드
/// </summary>
public static class ConfigurationValidationExtensions
{
    /// <summary>
    /// OpenAI 설정 유효성 검증
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection ValidateOpenAIConfiguration(this IServiceCollection services)
    {
        services.AddOptions<OpenAIOptions>()
            .Validate(options => options.IsValid, "Invalid OpenAI configuration")
            .ValidateOnStart();

        services.AddOptions<MetadataExtractionOptions>()
            .Validate(options => options.IsValid, "Invalid metadata extraction configuration")
            .ValidateOnStart();

        return services;
    }
}