using FluentAssertions;
using FluxIndex.Extensions.FluxImprover;
using FluxIndex.Extensions.FluxImprover.Services;
using FluxImprover.Services;
using FluxImprover.Enrichment;
using FluxImprover.Evaluation;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using FluxIndexCompletion = FluxIndex.Core.Application.Interfaces.ITextCompletionService;
using FluxImproverCompletion = FluxImprover.Services.ITextCompletionService;

namespace FluxIndex.Extensions.FluxImprover.Tests;

/// <summary>
/// Tests for ServiceCollectionExtensions - DI registration for FluxImprover integration
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddFluxImproverTextCompletion_RegistersAdapter()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockFluxIndexService = new Mock<FluxIndexCompletion>();
        services.AddSingleton(mockFluxIndexService.Object);

        // Act
        services.AddFluxImproverTextCompletion();

        // Assert
        var provider = services.BuildServiceProvider();
        var adapter = provider.GetService<FluxImproverCompletion>();
        adapter.Should().NotBeNull();
        adapter.Should().BeOfType<FluxIndex.Extensions.FluxImprover.Adapters.TextCompletionServiceAdapter>();
    }

    [Fact]
    public void AddFluxImproverTextCompletion_ReusesExistingFluxIndexService()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockFluxIndexService = new Mock<FluxIndexCompletion>();
        services.AddSingleton(mockFluxIndexService.Object);

        // Act
        services.AddFluxImproverTextCompletion();

        // Assert
        var provider = services.BuildServiceProvider();
        var adapter1 = provider.GetService<FluxImproverCompletion>();
        var adapter2 = provider.GetService<FluxImproverCompletion>();

        // Both should get the same adapter instance (singleton)
        adapter1.Should().BeSameAs(adapter2);
    }

    [Fact]
    public void AddFluxImproverTextCompletion_ReturnsServiceCollectionForChaining()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockFluxIndexService = new Mock<FluxIndexCompletion>();
        services.AddSingleton(mockFluxIndexService.Object);

        // Act
        var result = services.AddFluxImproverTextCompletion();

        // Assert
        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddFluxImproverIntegration_RegistersAllServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockFluxIndexService = new Mock<FluxIndexCompletion>();
        services.AddSingleton(mockFluxIndexService.Object);

        // Act
        services.AddFluxImproverIntegration();

        // Assert
        var provider = services.BuildServiceProvider();

        // Should register text completion adapter
        var textCompletion = provider.GetService<FluxImproverCompletion>();
        textCompletion.Should().NotBeNull();
    }

    [Fact]
    public void AddChunkEnrichmentWrapper_RegistersWrapper()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockSummarizationService = new Mock<ISummarizationService>();
        var mockKeywordService = new Mock<IKeywordExtractionService>();
        var enrichmentService = new ChunkEnrichmentService(
            mockSummarizationService.Object,
            mockKeywordService.Object);
        services.AddSingleton(enrichmentService);

        // Act
        services.AddChunkEnrichmentWrapper();

        // Assert
        var provider = services.BuildServiceProvider();
        var wrapper = provider.GetService<ChunkEnrichmentServiceWrapper>();
        wrapper.Should().NotBeNull();
    }

    [Fact]
    public void AddChunkEnrichmentWrapper_ReturnsSameInstanceForMultipleResolutions()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockSummarizationService = new Mock<ISummarizationService>();
        var mockKeywordService = new Mock<IKeywordExtractionService>();
        var enrichmentService = new ChunkEnrichmentService(
            mockSummarizationService.Object,
            mockKeywordService.Object);
        services.AddSingleton(enrichmentService);

        // Act
        services.AddChunkEnrichmentWrapper();

        // Assert
        var provider = services.BuildServiceProvider();
        var wrapper1 = provider.GetService<ChunkEnrichmentServiceWrapper>();
        var wrapper2 = provider.GetService<ChunkEnrichmentServiceWrapper>();
        wrapper1.Should().BeSameAs(wrapper2);
    }

    [Fact]
    public void AddChunkEnrichmentWrapper_ReturnsServiceCollectionForChaining()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockSummarizationService = new Mock<ISummarizationService>();
        var mockKeywordService = new Mock<IKeywordExtractionService>();
        var enrichmentService = new ChunkEnrichmentService(
            mockSummarizationService.Object,
            mockKeywordService.Object);
        services.AddSingleton(enrichmentService);

        // Act
        var result = services.AddChunkEnrichmentWrapper();

        // Assert
        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddRAGEvaluation_RegistersService()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockCompletionService = new Mock<ITextCompletionService>();
        mockCompletionService
            .Setup(s => s.CompleteAsync(It.IsAny<string>(), It.IsAny<CompletionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"score\": 0.85, \"details\": {}}");

        services.AddSingleton(mockCompletionService.Object);
        services.AddSingleton<AnswerabilityEvaluator>();
        services.AddSingleton<FaithfulnessEvaluator>();
        services.AddSingleton<RelevancyEvaluator>();

        // Act
        services.AddRAGEvaluation();

        // Assert
        var provider = services.BuildServiceProvider();
        var evaluationService = provider.GetService<RAGEvaluationService>();
        evaluationService.Should().NotBeNull();
    }

    [Fact]
    public void AddRAGEvaluation_ReturnsSameInstanceForMultipleResolutions()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockCompletionService = new Mock<ITextCompletionService>();
        mockCompletionService
            .Setup(s => s.CompleteAsync(It.IsAny<string>(), It.IsAny<CompletionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"score\": 0.85, \"details\": {}}");

        services.AddSingleton(mockCompletionService.Object);
        services.AddSingleton<AnswerabilityEvaluator>();
        services.AddSingleton<FaithfulnessEvaluator>();
        services.AddSingleton<RelevancyEvaluator>();

        // Act
        services.AddRAGEvaluation();

        // Assert
        var provider = services.BuildServiceProvider();
        var service1 = provider.GetService<RAGEvaluationService>();
        var service2 = provider.GetService<RAGEvaluationService>();
        service1.Should().BeSameAs(service2);
    }

    [Fact]
    public void AddRAGEvaluation_ReturnsServiceCollectionForChaining()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockCompletionService = new Mock<ITextCompletionService>();
        services.AddSingleton(mockCompletionService.Object);
        services.AddSingleton<AnswerabilityEvaluator>();
        services.AddSingleton<FaithfulnessEvaluator>();
        services.AddSingleton<RelevancyEvaluator>();

        // Act
        var result = services.AddRAGEvaluation();

        // Assert
        result.Should().BeSameAs(services);
    }
}
