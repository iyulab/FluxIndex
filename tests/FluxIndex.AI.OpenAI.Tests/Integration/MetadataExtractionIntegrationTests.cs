using FluxIndex.AI.OpenAI.Extensions;
using FluxIndex.AI.OpenAI.Services;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace FluxIndex.AI.OpenAI.Tests.Integration;

/// <summary>
/// 메타데이터 추출 통합 테스트
/// 실제 서비스 구성과 의존성 주입 테스트
/// </summary>
public class MetadataExtractionIntegrationTests : IDisposable
{
    private readonly IHost _host;
    private readonly IServiceScope _scope;

    public MetadataExtractionIntegrationTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenAI:ApiKey"] = "test-api-key",
                ["OpenAI:BaseUrl"] = "https://test.openai.com",
                ["OpenAI:Model"] = "gpt-3.5-turbo",
                ["OpenAI:MaxTokens"] = "500",
                ["OpenAI:Temperature"] = "0.5",
                ["MetadataExtraction:MaxKeywords"] = "5",
                ["MetadataExtraction:MaxEntities"] = "5",
                ["MetadataExtraction:MaxQuestions"] = "3",
                ["MetadataExtraction:BatchSize"] = "2",
                ["MetadataExtraction:Timeout"] = "00:00:10",
                ["MetadataExtraction:MaxRetries"] = "1",
                ["MetadataExtraction:MaxConcurrency"] = "1"
            })
            .Build();

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IConfiguration>(configuration);
                services.AddLogging(builder => builder.AddConsole());

                // Mock OpenAI 클라이언트 등록
                var mockClient = new Mock<IOpenAIClient>();
                mockClient.Setup(c => c.CompleteAsync(
                        It.IsAny<string>(),
                        It.IsAny<TimeSpan>(),
                        It.IsAny<System.Threading.CancellationToken>()))
                    .ReturnsAsync(GetMockJsonResponse());

                mockClient.Setup(c => c.IsHealthyAsync(
                        It.IsAny<System.Threading.CancellationToken>()))
                    .ReturnsAsync(true);

                services.AddTestOpenAIMetadataExtraction(mockClient.Object);
                services.ValidateOpenAIConfiguration();
            })
            .Build();

        _scope = _host.Services.CreateScope();
    }

    [Fact]
    public void ServiceRegistration_AllServices_AreRegistered()
    {
        // Act & Assert
        var metadataService = _scope.ServiceProvider.GetService<IMetadataEnrichmentService>();
        var openAIClient = _scope.ServiceProvider.GetService<IOpenAIClient>();
        var logger = _scope.ServiceProvider.GetService<ILogger<OpenAIMetadataEnrichmentService>>();

        Assert.NotNull(metadataService);
        Assert.NotNull(openAIClient);
        Assert.NotNull(logger);
        Assert.IsType<OpenAIMetadataEnrichmentService>(metadataService);
    }

    [Fact]
    public async Task MetadataExtraction_EndToEnd_WorksCorrectly()
    {
        // Arrange
        var service = _scope.ServiceProvider.GetRequiredService<IMetadataEnrichmentService>();
        var content = "This is a comprehensive test document about artificial intelligence and machine learning applications in modern software development.";

        // Act
        var result = await service.ExtractMetadataAsync(content);

        // Assert
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.Title));
        Assert.False(string.IsNullOrWhiteSpace(result.Summary));
        Assert.True(result.IsValid);
        Assert.True(result.QualityScore > 0);
    }

    [Fact]
    public async Task BatchExtraction_EndToEnd_WorksCorrectly()
    {
        // Arrange
        var service = _scope.ServiceProvider.GetRequiredService<IMetadataEnrichmentService>();
        var contents = new List<string>
        {
            "First document about software architecture patterns and design principles.",
            "Second document covering database optimization and performance tuning strategies."
        };

        var options = new BatchProcessingOptions
        {
            Size = 2,
            DelayBetweenBatches = TimeSpan.FromMilliseconds(10),
            ContinueOnFailure = false
        };

        // Act
        var results = await service.ExtractBatchAsync(contents, options);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results, metadata =>
        {
            Assert.False(string.IsNullOrWhiteSpace(metadata.Title));
            Assert.True(metadata.IsValid);
        });
    }

    [Fact]
    public async Task HealthCheck_ServiceConfiguration_ReturnsHealthy()
    {
        // Arrange
        var service = _scope.ServiceProvider.GetRequiredService<IMetadataEnrichmentService>();

        // Act
        var isHealthy = await service.IsHealthyAsync();

        // Assert
        Assert.True(isHealthy);
    }

    [Fact]
    public async Task Statistics_ServiceUsage_ReturnsValidStatistics()
    {
        // Arrange
        var service = _scope.ServiceProvider.GetRequiredService<IMetadataEnrichmentService>();

        // Act
        var statistics = await service.GetStatisticsAsync();

        // Assert
        Assert.NotNull(statistics);
        Assert.True(statistics.TotalProcessedChunks >= 0);
        Assert.True(statistics.SuccessfulExtractions >= 0);
        Assert.True(statistics.FailedExtractions >= 0);
        Assert.True(statistics.SuccessRate >= 0.0f && statistics.SuccessRate <= 1.0f);
    }

    [Fact]
    public void Configuration_Validation_PassesWithValidSettings()
    {
        // Act & Assert - Should not throw during service construction
        var metadataOptions = _scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MetadataExtractionOptions>>();
        var openAIOptions = _scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpenAIOptions>>();

        Assert.True(metadataOptions.Value.IsValid);
        Assert.True(openAIOptions.Value.IsValid);
    }

    [Fact]
    public async Task ErrorHandling_ServiceFailure_HandlesGracefully()
    {
        // Arrange
        var mockClient = new Mock<IOpenAIClient>();
        mockClient.Setup(c => c.CompleteAsync(
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<System.Threading.CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Service temporarily unavailable"));

        var failingService = OpenAIMetadataEnrichmentService.CreateForTesting(
            mockClient.Object,
            MetadataExtractionOptions.CreateForTesting());

        // Act & Assert
        var exception = await Assert.ThrowsAsync<MetadataExtractionException>(() =>
            failingService.ExtractMetadataAsync("Test content"));

        Assert.Equal(MetadataExtractionErrorType.Unknown, exception.ErrorType);
        Assert.Contains("Service temporarily unavailable", exception.Message);
    }

    private static string GetMockJsonResponse()
    {
        return """
        {
            "title": "Integration Test Document",
            "summary": "A comprehensive test document for integration testing of metadata extraction service",
            "keywords": ["integration", "test", "metadata", "extraction", "service"],
            "entities": ["TestService", "MetadataExtractor", "IntegrationTest"],
            "generated_questions": [
                "What is the purpose of this integration test?",
                "How does the metadata extraction service work?",
                "What are the key components being tested?"
            ],
            "quality_score": 0.87
        }
        """;
    }

    public void Dispose()
    {
        _scope?.Dispose();
        _host?.Dispose();
    }
}

/// <summary>
/// Azure OpenAI 특화 통합 테스트
/// </summary>
public class AzureOpenAIIntegrationTests : IDisposable
{
    private readonly IHost _host;
    private readonly IServiceScope _scope;

    public AzureOpenAIIntegrationTests()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging(builder => builder.AddConsole());

                // Mock Azure OpenAI 클라이언트
                var mockClient = new Mock<IOpenAIClient>();
                mockClient.Setup(c => c.CompleteAsync(
                        It.IsAny<string>(),
                        It.IsAny<TimeSpan>(),
                        It.IsAny<System.Threading.CancellationToken>()))
                    .ReturnsAsync(GetAzureMockResponse());

                services.AddAzureOpenAIMetadataExtraction(
                    "test-azure-key",
                    "https://test-resource.openai.azure.com",
                    "gpt-4-deployment",
                    options =>
                    {
                        options.MaxKeywords = 8;
                        options.MaxEntities = 10;
                        options.EnableDebugLogging = true;
                    });

                // Replace with mock client
                services.AddSingleton(mockClient.Object);
            })
            .Build();

        _scope = _host.Services.CreateScope();
    }

    [Fact]
    public void AzureConfiguration_ServiceRegistration_ConfiguresCorrectly()
    {
        // Act
        var service = _scope.ServiceProvider.GetService<IMetadataEnrichmentService>();
        var openAIOptions = _scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpenAIOptions>>();

        // Assert
        Assert.NotNull(service);
        Assert.True(openAIOptions.Value.IsAzure);
        Assert.Equal("gpt-4-deployment", openAIOptions.Value.DeploymentName);
        Assert.Equal("https://test-resource.openai.azure.com", openAIOptions.Value.BaseUrl);
    }

    [Fact]
    public async Task AzureOpenAI_MetadataExtraction_WorksWithAzureConfiguration()
    {
        // Arrange
        var service = _scope.ServiceProvider.GetRequiredService<IMetadataEnrichmentService>();
        var content = "Azure OpenAI test document with enterprise features and security compliance.";

        // Act
        var result = await service.ExtractMetadataAsync(content);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("Azure", result.Title);
        Assert.True(result.IsValid);
    }

    private static string GetAzureMockResponse()
    {
        return """
        {
            "title": "Azure OpenAI Enterprise Document",
            "summary": "Enterprise-grade document processed through Azure OpenAI with enhanced security and compliance",
            "keywords": ["azure", "openai", "enterprise", "security", "compliance", "cloud"],
            "entities": ["Azure", "OpenAI", "Microsoft", "Enterprise", "Cloud"],
            "generated_questions": [
                "What are the enterprise benefits of Azure OpenAI?",
                "How does Azure ensure security and compliance?",
                "What makes Azure OpenAI different from standard OpenAI?"
            ],
            "quality_score": 0.92
        }
        """;
    }

    public void Dispose()
    {
        _scope?.Dispose();
        _host?.Dispose();
    }
}