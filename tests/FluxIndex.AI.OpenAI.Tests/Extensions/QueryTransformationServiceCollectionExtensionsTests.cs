using FluxIndex.AI.OpenAI.Extensions;
using FluxIndex.AI.OpenAI.Services;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.Collections.Generic;
using Xunit;

namespace FluxIndex.AI.OpenAI.Tests.Extensions;

/// <summary>
/// 쿼리 변환 서비스 등록 확장 메서드 테스트
/// </summary>
public class QueryTransformationServiceCollectionExtensionsTests
{
    [Fact]
    public void AddOpenAIQueryTransformation_WithConfiguration_RegistersAllServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var configDict = new Dictionary<string, string>
        {
            { "QueryTransformation:HyDE:Timeout", "00:00:45" },
            { "QueryTransformation:HyDE:MaxRetries", "3" },
            { "QueryTransformation:QuOTE:Timeout", "00:00:30" },
            { "QueryTransformation:QuOTE:MaxRetries", "2" }
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        // Act
        services.AddOpenAIQueryTransformation(configuration);
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider.GetService<HyDEService>());
        Assert.NotNull(provider.GetService<QuOTEService>());
        Assert.NotNull(provider.GetService<IQueryTransformationService>());
        Assert.IsType<OpenAIQueryTransformationService>(provider.GetService<IQueryTransformationService>());

        // 옵션 확인
        var hydeOptions = provider.GetService<IOptions<HyDEServiceOptions>>()?.Value;
        Assert.NotNull(hydeOptions);
        Assert.Equal(TimeSpan.FromSeconds(45), hydeOptions.Timeout);
        Assert.Equal(3, hydeOptions.MaxRetries);

        var quoteOptions = provider.GetService<IOptions<QuOTEServiceOptions>>()?.Value;
        Assert.NotNull(quoteOptions);
        Assert.Equal(TimeSpan.FromSeconds(30), quoteOptions.Timeout);
        Assert.Equal(2, quoteOptions.MaxRetries);
    }

    [Fact]
    public void AddOpenAIQueryTransformation_WithDirectConfiguration_RegistersServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOpenAIQueryTransformation(
            openAI =>
            {
                openAI.ApiKey = "test-key";
                openAI.BaseUrl = "https://api.openai.com";
            },
            hyde =>
            {
                hyde.Timeout = TimeSpan.FromSeconds(60);
                hyde.MaxRetries = 5;
                hyde.DefaultMaxLength = 400;
            },
            quote =>
            {
                quote.Timeout = TimeSpan.FromSeconds(45);
                quote.MaxRetries = 3;
                quote.DefaultMaxExpansions = 5;
            },
            general =>
            {
                general.EnableParallelProcessing = true;
                general.MaxConcurrentRequests = 10;
            });

        var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider.GetService<HyDEService>());
        Assert.NotNull(provider.GetService<QuOTEService>());
        Assert.NotNull(provider.GetService<IQueryTransformationService>());

        // OpenAI 옵션 확인
        var openAIOptions = provider.GetService<IOptions<OpenAIOptions>>()?.Value;
        Assert.NotNull(openAIOptions);
        Assert.Equal("test-key", openAIOptions.ApiKey);
        Assert.Equal("https://api.openai.com", openAIOptions.BaseUrl);

        // HyDE 옵션 확인
        var hydeOptions = provider.GetService<IOptions<HyDEServiceOptions>>()?.Value;
        Assert.NotNull(hydeOptions);
        Assert.Equal(TimeSpan.FromSeconds(60), hydeOptions.Timeout);
        Assert.Equal(5, hydeOptions.MaxRetries);
        Assert.Equal(400, hydeOptions.DefaultMaxLength);

        // QuOTE 옵션 확인
        var quoteOptions = provider.GetService<IOptions<QuOTEServiceOptions>>()?.Value;
        Assert.NotNull(quoteOptions);
        Assert.Equal(TimeSpan.FromSeconds(45), quoteOptions.Timeout);
        Assert.Equal(3, quoteOptions.MaxRetries);
        Assert.Equal(5, quoteOptions.DefaultMaxExpansions);

        // 일반 옵션 확인
        var generalOptions = provider.GetService<IOptions<QueryTransformationOptions>>()?.Value;
        Assert.NotNull(generalOptions);
        Assert.True(generalOptions.EnableParallelProcessing);
        Assert.Equal(10, generalOptions.MaxConcurrentRequests);
    }

    [Fact]
    public void AddAzureOpenAIQueryTransformation_ValidParameters_RegistersServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var apiKey = "azure-api-key";
        var resourceUrl = "https://test-resource.openai.azure.com";
        var deploymentName = "gpt-4";

        // Act
        services.AddAzureOpenAIQueryTransformation(
            apiKey,
            resourceUrl,
            deploymentName,
            hyde =>
            {
                hyde.Timeout = TimeSpan.FromSeconds(30);
                hyde.DefaultDocumentStyle = "technical";
            },
            quote =>
            {
                quote.DefaultMaxExpansions = 4;
                quote.DefaultDiversityLevel = 0.8f;
            });

        var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider.GetService<HyDEService>());
        Assert.NotNull(provider.GetService<QuOTEService>());
        Assert.NotNull(provider.GetService<IQueryTransformationService>());

        // Azure OpenAI 옵션 확인
        var openAIOptions = provider.GetService<IOptions<OpenAIOptions>>()?.Value;
        Assert.NotNull(openAIOptions);
        Assert.Equal(apiKey, openAIOptions.ApiKey);
        Assert.Equal(resourceUrl, openAIOptions.BaseUrl);
        Assert.True(openAIOptions.IsAzure);
        Assert.Equal(deploymentName, openAIOptions.DeploymentName);

        // HyDE 옵션 확인
        var hydeOptions = provider.GetService<IOptions<HyDEServiceOptions>>()?.Value;
        Assert.NotNull(hydeOptions);
        Assert.Equal(TimeSpan.FromSeconds(30), hydeOptions.Timeout);
        Assert.Equal("technical", hydeOptions.DefaultDocumentStyle);

        // QuOTE 옵션 확인
        var quoteOptions = provider.GetService<IOptions<QuOTEServiceOptions>>()?.Value;
        Assert.NotNull(quoteOptions);
        Assert.Equal(4, quoteOptions.DefaultMaxExpansions);
        Assert.Equal(0.8f, quoteOptions.DefaultDiversityLevel);
    }

    [Theory]
    [InlineData("", "valid-url", "deployment")]
    [InlineData("   ", "valid-url", "deployment")]
    [InlineData("key", "", "deployment")]
    [InlineData("key", "   ", "deployment")]
    [InlineData("key", "valid-url", "")]
    [InlineData("key", "valid-url", "   ")]
    public void AddAzureOpenAIQueryTransformation_InvalidParameters_ThrowsArgumentException(
        string apiKey, string resourceUrl, string deploymentName)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            services.AddAzureOpenAIQueryTransformation(apiKey, resourceUrl, deploymentName));
    }

    [Fact]
    public void AddTestQueryTransformation_WithoutMockClient_RegistersServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddTestQueryTransformation();
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider.GetService<HyDEService>());
        Assert.NotNull(provider.GetService<QuOTEService>());
        Assert.NotNull(provider.GetService<IQueryTransformationService>());

        // 테스트 옵션 확인
        var openAIOptions = provider.GetService<IOptions<OpenAIOptions>>()?.Value;
        Assert.NotNull(openAIOptions);
        Assert.True(openAIOptions.IsTestMode);

        var hydeOptions = provider.GetService<IOptions<HyDEServiceOptions>>()?.Value;
        Assert.NotNull(hydeOptions);
        Assert.Equal(TimeSpan.FromSeconds(10), hydeOptions.Timeout);
        Assert.Equal(1, hydeOptions.MaxRetries);
        Assert.True(hydeOptions.EnableDebugLogging);

        var quoteOptions = provider.GetService<IOptions<QuOTEServiceOptions>>()?.Value;
        Assert.NotNull(quoteOptions);
        Assert.Equal(2, quoteOptions.DefaultMaxExpansions);
        Assert.Equal(3, quoteOptions.DefaultMaxRelatedQuestions);
    }

    [Fact]
    public void AddTestQueryTransformation_WithMockClient_UsesMockClient()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockClient = new Mock<IOpenAIClient>();

        // Act
        services.AddTestQueryTransformation(mockClient.Object);
        var provider = services.BuildServiceProvider();

        // Assert
        var registeredClient = provider.GetService<IOpenAIClient>();
        Assert.Same(mockClient.Object, registeredClient);
    }

    [Fact]
    public void AddHyDEOnly_RegistersOnlyHyDE()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHyDEOnly(
            openAI =>
            {
                openAI.ApiKey = "test-key";
                openAI.BaseUrl = "https://api.openai.com";
            },
            hyde =>
            {
                hyde.Timeout = TimeSpan.FromSeconds(40);
                hyde.DefaultMaxLength = 350;
            });

        var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider.GetService<HyDEService>());
        Assert.Null(provider.GetService<QuOTEService>());
        Assert.Null(provider.GetService<IQueryTransformationService>());

        // HyDE 옵션 확인
        var hydeOptions = provider.GetService<IOptions<HyDEServiceOptions>>()?.Value;
        Assert.NotNull(hydeOptions);
        Assert.Equal(TimeSpan.FromSeconds(40), hydeOptions.Timeout);
        Assert.Equal(350, hydeOptions.DefaultMaxLength);
    }

    [Fact]
    public void AddQuOTEOnly_RegistersOnlyQuOTE()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddQuOTEOnly(
            openAI =>
            {
                openAI.ApiKey = "test-key";
                openAI.BaseUrl = "https://api.openai.com";
            },
            quote =>
            {
                quote.Timeout = TimeSpan.FromSeconds(35);
                quote.DefaultMaxExpansions = 6;
            });

        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Null(provider.GetService<HyDEService>());
        Assert.NotNull(provider.GetService<QuOTEService>());
        Assert.Null(provider.GetService<IQueryTransformationService>());

        // QuOTE 옵션 확인
        var quoteOptions = provider.GetService<IOptions<QuOTEServiceOptions>>()?.Value;
        Assert.NotNull(quoteOptions);
        Assert.Equal(TimeSpan.FromSeconds(35), quoteOptions.Timeout);
        Assert.Equal(6, quoteOptions.DefaultMaxExpansions);
    }

    [Fact]
    public void ValidateQueryTransformationConfiguration_ValidOptions_DoesNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();
        services.Configure<OpenAIOptions>(opt =>
        {
            opt.ApiKey = "valid-key";
            opt.BaseUrl = "https://api.openai.com";
        });
        services.Configure<HyDEServiceOptions>(opt =>
        {
            opt.Timeout = TimeSpan.FromSeconds(30);
            opt.MaxRetries = 2;
        });
        services.Configure<QuOTEServiceOptions>(opt =>
        {
            opt.Timeout = TimeSpan.FromSeconds(30);
            opt.DefaultMaxExpansions = 3;
        });
        services.Configure<QueryTransformationOptions>(opt =>
        {
            opt.MaxConcurrentRequests = 5;
        });

        // Act & Assert
        Assert.NotNull(services.ValidateQueryTransformationConfiguration());
    }

    [Fact]
    public void ServiceRegistration_MultipleCalls_DoesNotCauseConflicts()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act - 여러 번 등록
        services.AddOpenAIQueryTransformation(openAI =>
        {
            openAI.ApiKey = "key1";
        });

        services.AddOpenAIQueryTransformation(openAI =>
        {
            openAI.ApiKey = "key2"; // 두 번째 등록은 첫 번째를 덮어씀
        });

        var provider = services.BuildServiceProvider();

        // Assert
        var service = provider.GetService<IQueryTransformationService>();
        Assert.NotNull(service);
        Assert.IsType<OpenAIQueryTransformationService>(service);

        // 마지막에 등록된 옵션이 사용되는지 확인
        var openAIOptions = provider.GetService<IOptions<OpenAIOptions>>()?.Value;
        Assert.NotNull(openAIOptions);
        Assert.Equal("key2", openAIOptions.ApiKey);
    }

    [Fact]
    public void ServiceLifetime_RegisteredAsScoped()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddOpenAIQueryTransformation(openAI =>
        {
            openAI.ApiKey = "test-key";
        });

        var provider = services.BuildServiceProvider();

        // Act
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var service1a = scope1.ServiceProvider.GetService<IQueryTransformationService>();
        var service1b = scope1.ServiceProvider.GetService<IQueryTransformationService>();
        var service2a = scope2.ServiceProvider.GetService<IQueryTransformationService>();

        // Assert
        Assert.Same(service1a, service1b); // 같은 스코프 내에서는 동일한 인스턴스
        Assert.NotSame(service1a, service2a); // 다른 스코프에서는 다른 인스턴스
    }

    [Fact]
    public void DefaultOptions_WhenNotConfigured_UseDefaults()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act - 최소한의 설정으로 등록
        services.AddOpenAIQueryTransformation(openAI =>
        {
            openAI.ApiKey = "test-key";
        });

        var provider = services.BuildServiceProvider();

        // Assert
        var hydeOptions = provider.GetService<IOptions<HyDEServiceOptions>>()?.Value;
        Assert.NotNull(hydeOptions);
        Assert.Equal(TimeSpan.FromSeconds(30), hydeOptions.Timeout);
        Assert.Equal(2, hydeOptions.MaxRetries);
        Assert.Equal("informative", hydeOptions.DefaultDocumentStyle);

        var quoteOptions = provider.GetService<IOptions<QuOTEServiceOptions>>()?.Value;
        Assert.NotNull(quoteOptions);
        Assert.Equal(TimeSpan.FromSeconds(30), quoteOptions.Timeout);
        Assert.Equal(2, quoteOptions.MaxRetries);
        Assert.Equal(3, quoteOptions.DefaultMaxExpansions);
    }

    [Fact]
    public void HttpClientRegistration_RegisteredCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOpenAIQueryTransformation(openAI =>
        {
            openAI.ApiKey = "test-key";
        });

        var provider = services.BuildServiceProvider();

        // Assert
        var httpClientFactory = provider.GetService<IHttpClientFactory>();
        Assert.NotNull(httpClientFactory);

        // HttpClient가 IOpenAIClient와 함께 등록되었는지 확인
        var openAIClient = provider.GetService<IOpenAIClient>();
        Assert.NotNull(openAIClient);
    }
}