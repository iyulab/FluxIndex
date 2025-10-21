using FluxIndex.Extensions.FileFlux;
using FileFlux;
using FileFlux.Domain;
using FileFlux.Infrastructure.Quality;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxIndex.Extensions.FileFlux.Tests.Integration;

/// <summary>
/// Integration tests for FileFlux 0.3.0 quality analysis features (Phase 2)
/// Tests DI registration and interface availability
/// </summary>
public class FileFluxQualityAnalysisTests
{
    [Fact]
    public void IDocumentQualityAnalyzer_ShouldBeRegistered()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFileFluxIntegration();

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var qualityAnalyzer = serviceProvider.GetService<IDocumentQualityAnalyzer>();

        // Assert
        Assert.NotNull(qualityAnalyzer);
        Assert.IsAssignableFrom<IDocumentQualityAnalyzer>(qualityAnalyzer);
    }

    [Fact]
    public void ChunkQualityEngine_ShouldBeRegistered()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFileFluxIntegration();

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var qualityEngine = serviceProvider.GetService<ChunkQualityEngine>();

        // Assert
        Assert.NotNull(qualityEngine);
    }

    [Fact]
    public void IDocumentQualityAnalyzer_EvaluateChunksAsync_MethodExists()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFileFluxIntegration();
        var serviceProvider = services.BuildServiceProvider();
        var qualityAnalyzer = serviceProvider.GetRequiredService<IDocumentQualityAnalyzer>();

        // Act & Assert
        var method = qualityAnalyzer.GetType().GetMethod("EvaluateChunksAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<ChunkingQualityMetrics>), method.ReturnType);
    }

    [Fact]
    public void IDocumentQualityAnalyzer_GenerateQABenchmarkAsync_MethodExists()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFileFluxIntegration();
        var serviceProvider = services.BuildServiceProvider();
        var qualityAnalyzer = serviceProvider.GetRequiredService<IDocumentQualityAnalyzer>();

        // Act & Assert
        var method = qualityAnalyzer.GetType().GetMethod("GenerateQABenchmarkAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<QABenchmark>), method.ReturnType);
    }

    [Fact]
    public void IDocumentQualityAnalyzer_AnalyzeQualityAsync_MethodExists()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFileFluxIntegration();
        var serviceProvider = services.BuildServiceProvider();
        var qualityAnalyzer = serviceProvider.GetRequiredService<IDocumentQualityAnalyzer>();

        // Act & Assert
        var method = qualityAnalyzer.GetType().GetMethod("AnalyzeQualityAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<DocumentQualityReport>), method.ReturnType);
    }

    [Fact]
    public void IDocumentQualityAnalyzer_BenchmarkChunkingAsync_MethodExists()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFileFluxIntegration();
        var serviceProvider = services.BuildServiceProvider();
        var qualityAnalyzer = serviceProvider.GetRequiredService<IDocumentQualityAnalyzer>();

        // Act & Assert
        var method = qualityAnalyzer.GetType().GetMethod("BenchmarkChunkingAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<QualityBenchmarkResult>), method.ReturnType);
    }

    [Fact]
    public void ChunkingQualityMetrics_Properties_ShouldExist()
    {
        // Act
        var metrics = new ChunkingQualityMetrics
        {
            AverageCompleteness = 0.85,
            ContentConsistency = 0.90,
            BoundaryQuality = 0.75,
            SizeDistribution = 0.80,
            OverlapEffectiveness = 0.70
        };

        // Assert
        Assert.Equal(0.85, metrics.AverageCompleteness);
        Assert.Equal(0.90, metrics.ContentConsistency);
        Assert.Equal(0.75, metrics.BoundaryQuality);
        Assert.Equal(0.80, metrics.SizeDistribution);
        Assert.Equal(0.70, metrics.OverlapEffectiveness);
    }

    [Fact]
    public void QABenchmark_Properties_ShouldExist()
    {
        // Act
        var benchmark = new QABenchmark
        {
            DocumentId = "test",
            DocumentPath = "test.md",
            Questions = new List<GeneratedQuestion>(),
            AnswerabilityScore = 0.95
        };

        // Assert
        Assert.Equal("test", benchmark.DocumentId);
        Assert.Equal("test.md", benchmark.DocumentPath);
        Assert.NotNull(benchmark.Questions);
        Assert.Equal(0.95, benchmark.AnswerabilityScore);
    }

    [Fact]
    public void QuestionType_Enum_ShouldHaveAllTypes()
    {
        // Assert - Verify all 5 question types exist
        Assert.True(Enum.IsDefined(typeof(QuestionType), QuestionType.Factual));
        Assert.True(Enum.IsDefined(typeof(QuestionType), QuestionType.Conceptual));
        Assert.True(Enum.IsDefined(typeof(QuestionType), QuestionType.Analytical));
        Assert.True(Enum.IsDefined(typeof(QuestionType), QuestionType.Procedural));
        Assert.True(Enum.IsDefined(typeof(QuestionType), QuestionType.Comparative));
    }
}
